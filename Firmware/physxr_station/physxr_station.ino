// PHYS-XR sensing station firmware (ESP32 + VL53L0X + HC-SR04 + MPU6050).
//
// Sends the semantic PhysicalState contract v1 (Technical Spec section E) to
// Unity over USB serial, one '\n'-terminated line per message:
//
//   v=1;dev=phys-xr-01;boot=<u32>;seq=<n>;ts=<ms since boot>;presence=NEAR;
//   confidence=HIGH;disturbance=false;eventType=;eventId=0;tEvent=0
//
// plus display-only diagnostic lines that start with '#':
//
//   #HELLO ...   firmware identity, pins and detected I2C devices (boot + every 5 s)
//   #DIAG  ...   raw readings, validity and ages for the bench/overlay (4 Hz)
//   #CAL   ...   MPU6050 calibration progress and result
//   #ERR   ...   a sensor failed to initialise or stopped answering
//
// Unity never feeds '#' lines into PhysicalState: raw values stay below the
// semantic boundary (Hardware Specification v1 section 2, Appendix B).
//
// Execution model: timed cooperative superloop, no delay() in the loop, no
// unbounded waits. The ultrasonic echo is timed with a pin-change interrupt
// so that the 25 ms worst-case echo wait never blocks IMU sampling or serial.
//
// Libraries: Wire (ESP32 core) and "VL53L0X" by Pololu (Library Manager).
// Board: any classic ESP32 dev board (ESP32-WROOM-32), arduino-esp32 core 3.x.
//
// Pin facts and open items are recorded in Firmware/README.md. In short:
//   HC-SR04 TRIG = GPIO 25, ECHO = GPIO 26 (from the team's working bench
//   prototype; ECHO is 5 V on an HC-SR04 and MUST go through a divider).
//   I2C SDA = GPIO 21. SCL is unresolved between the sources (GPIO 23 in the
//   user's latest code, GPIO 22 in the schematic poster and the I2C scanner),
//   so setup() probes 23 then 22 and reports the one that answered in #HELLO.

#include <Wire.h>
#include <VL53L0X.h>
#include <esp_random.h>

#include "physxr_semantics.h"

using namespace physxr;

// ------------------------------------------------------------------ config

static const char* const FIRMWARE_VERSION = "1.0.0";
static const char* const DEVICE_ID = "phys-xr-01";  // Unity's expected device ID
static const uint32_t SERIAL_BAUD = 115200;

static const uint8_t PIN_TRIG = 25;
static const uint8_t PIN_ECHO = 26;
static const uint8_t PIN_SDA = 21;
static const uint8_t SCL_CANDIDATES[] = {23, 22};
static const uint32_t I2C_CLOCK_HZ = 100000;  // spec 4.3: 100 kHz for bring-up
static const uint16_t I2C_TIMEOUT_MS = 10;

static const uint8_t MPU_ADDRESSES[] = {0x68, 0x69};
static const uint8_t TOF_ADDRESS = 0x29;  // VL53L0X default

// Service periods (spec section 8 starting targets).
static const uint32_t IMU_PERIOD_MS = 10;          // 100 Hz
static const uint32_t TOF_PERIOD_MS = 40;          // 25 Hz continuous ranging
static const uint32_t US_PERIOD_MS = 66;           // 15 Hz (HC-SR04 needs >= 60 ms per cycle)
static const uint32_t US_ECHO_TIMEOUT_US = 25000;  // ~4.3 m round trip
static const uint32_t STATE_PERIOD_MS = 100;       // 10 Hz, plus immediately on change
static const uint32_t DIAG_PERIOD_MS = 250;
static const uint32_t HELLO_PERIOD_MS = 5000;
static const uint32_t REINIT_PERIOD_MS = 2000;

// A sensor's last valid sample older than this makes it invalid for fusion.
static const uint32_t TOF_FRESH_MS = 250;
static const uint32_t US_FRESH_MS = 300;
static const uint8_t FAILS_BEFORE_UNHEALTHY = 10;

// Validity limits.
static const float US_MIN_CM = 2.0f;    // HC-SR04 datasheet range 2-400 cm
static const float US_MAX_CM = 400.0f;
static const uint16_t TOF_FAR_MM = 8190;  // VL53L0X reports >= 8190 when nothing is in range
static const uint16_t TOF_MAX_MM = 1200;  // beyond this a "valid" reading is treated as far

// UNCALIBRATED starting thresholds. Calibrate on the mounted station
// (Firmware/README.md, "Calibration") and record the values used.
static const ProximityThresholds PROXIMITY = {
    30.0f,  // VERY_NEAR enter <= 30 cm
    35.0f,  // VERY_NEAR exit  >  35 cm
    80.0f,  // NEAR enter      <= 80 cm
    90.0f,  // NEAR exit       >  90 cm
};

static const DisturbanceConfig DISTURBANCE = {
    200,     // calibration samples (2 s at 100 Hz)
    0.03f,   // reject the baseline if the station moved (std-dev > 0.03 g)
    0.7f,    // plausible stationary |A| ...
    1.3f,    // ... in g
    0.08f,   // THRESHOLD_ON floor (g)
    6.0f,    // or 6 x baseline noise, whichever is larger
    0.5f,    // THRESHOLD_OFF = 0.5 x ON
    3,       // 3 consecutive samples (30 ms) above ON
    1500,    // re-arm no sooner than 1.5 s after an event
};

static const uint8_t EVENT_REPEAT_LINES = 3;  // event repeated on 3 lines; Unity dedups

// ------------------------------------------------------------------ state

static VL53L0X tof;
static uint8_t sclPin = 0;
static uint8_t mpuAddress = 0;
static bool mpuPresent = false;
static bool tofPresent = false;

static uint32_t bootId = 0;
static uint32_t sequence = 0;
static uint32_t nextEventId = 1;

struct SensorTrack {
  bool healthy = false;
  bool lastValid = false;
  uint32_t lastValidMs = 0;
  bool everValid = false;
  float value = 0.0f;  // filtered value of the last valid sample
  uint8_t fails = 0;
  uint32_t samples = 0;
  uint32_t invalid = 0;
};

static SensorTrack tofTrack, usTrack, imuTrack;
static bool tofFar = false;  // last ToF sample was "nothing in range"
static Median3 tofMedian, usMedian;
static ProximityClassifier tofClass(PROXIMITY), usClass(PROXIMITY);
static DisturbanceDetector disturbance(DISTURBANCE);
static float imuMagnitude = 0.0f;
static uint8_t lastCalStage = 255;

static Presence sentPresence = PRESENCE_NONE;
static Confidence sentConfidence = CONFIDENCE_LOW;
static bool stateDirty = true;
static uint32_t activeEventId = 0, activeEventTs = 0;
static uint8_t activeEventLinesLeft = 0;

static uint32_t nextImuMs = 0, nextTofMs = 0, nextUsMs = 0, nextStateMs = 0, nextDiagMs = 0, nextHelloMs = 0;
static uint32_t nextMpuReinitMs = 0, nextTofReinitMs = 0;
static uint32_t loopMaxUs = 0;

// Ultrasonic echo timing (written in the ISR).
static volatile uint32_t echoRiseUs = 0, echoFallUs = 0;
static volatile bool echoRose = false, echoDone = false;
static bool usPending = false;
static uint32_t usTriggerUs = 0;

static char line[256];

// ------------------------------------------------------------------ helpers

// Device time for the contract: ms since boot, never 0 (Unity rejects ts <= 0).
static uint32_t deviceTime() {
  uint32_t t = millis();
  return t == 0 ? 1 : t;
}

static bool due(uint32_t now, uint32_t& next, uint32_t period) {
  if ((int32_t)(now - next) < 0) return false;
  next = now + period;
  return true;
}

static void sendLine(const char* text) {
  Serial.print(text);
  Serial.print('\n');
}

static bool i2cPresent(uint8_t address) {
  Wire.beginTransmission(address);
  return Wire.endTransmission() == 0;
}

static bool i2cWrite(uint8_t address, uint8_t reg, uint8_t value) {
  Wire.beginTransmission(address);
  Wire.write(reg);
  Wire.write(value);
  return Wire.endTransmission() == 0;
}

static bool i2cRead(uint8_t address, uint8_t reg, uint8_t* out, uint8_t count) {
  Wire.beginTransmission(address);
  Wire.write(reg);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(address, count) != count) return false;
  for (uint8_t i = 0; i < count; i++) out[i] = Wire.read();
  return true;
}

// ------------------------------------------------------------------ I2C bus

static bool startBus(uint8_t scl) {
  Wire.end();
  if (!Wire.begin(PIN_SDA, scl, I2C_CLOCK_HZ)) return false;
  Wire.setTimeOut(I2C_TIMEOUT_MS);
  for (uint8_t a : MPU_ADDRESSES)
    if (i2cPresent(a)) return true;
  return i2cPresent(TOF_ADDRESS);
}

static void selectBus() {
  for (uint8_t scl : SCL_CANDIDATES) {
    if (startBus(scl)) {
      sclPin = scl;
      return;
    }
  }
  // Nothing answered on any candidate: keep the first (the user's latest code)
  // and report it; drivers retry every REINIT_PERIOD_MS.
  sclPin = SCL_CANDIDATES[0];
  startBus(sclPin);
  snprintf(line, sizeof(line), "#ERR sensor=i2c;msg=no_device_on_sda%u_scl%u_or_scl%u", PIN_SDA, SCL_CANDIDATES[0],
           SCL_CANDIDATES[1]);
  sendLine(line);
}

// ------------------------------------------------------------------ MPU6050

static bool initMpu() {
  mpuPresent = false;
  for (uint8_t a : MPU_ADDRESSES) {
    if (!i2cPresent(a)) continue;
    // PWR_MGMT_1: wake, PLL with X gyro reference. CONFIG: DLPF 44 Hz.
    // ACCEL_CONFIG: +/-2 g (16384 LSB/g). Gyro is not read (spec section 6).
    if (i2cWrite(a, 0x6B, 0x01) && i2cWrite(a, 0x1A, 0x03) && i2cWrite(a, 0x1C, 0x00)) {
      mpuAddress = a;
      mpuPresent = true;
      imuTrack.healthy = true;
      imuTrack.fails = 0;
      disturbance.recalibrate();
      sendLine("#CAL state=start;msg=keep_station_still");
      return true;
    }
  }
  imuTrack.healthy = false;
  return false;
}

static void serviceImu(uint32_t now) {
  if (!mpuPresent) {
    if (due(now, nextMpuReinitMs, REINIT_PERIOD_MS) && !initMpu()) sendLine("#ERR sensor=imu;msg=not_found");
    return;
  }

  uint8_t raw[6];
  imuTrack.samples++;
  if (!i2cRead(mpuAddress, 0x3B, raw, 6)) {
    imuTrack.invalid++;
    imuTrack.lastValid = false;
    if (++imuTrack.fails >= FAILS_BEFORE_UNHEALTHY) {
      // Unhealthy IMU never emits a disturbance; re-init (and recalibrate) later.
      imuTrack.healthy = false;
      mpuPresent = false;
      nextMpuReinitMs = now + REINIT_PERIOD_MS;
      sendLine("#ERR sensor=imu;msg=read_failed");
    }
    return;
  }

  float ax = (int16_t)((raw[0] << 8) | raw[1]) / 16384.0f;
  float ay = (int16_t)((raw[2] << 8) | raw[3]) / 16384.0f;
  float az = (int16_t)((raw[4] << 8) | raw[5]) / 16384.0f;
  imuMagnitude = sqrtf(ax * ax + ay * ay + az * az);
  imuTrack.fails = 0;
  imuTrack.healthy = true;
  imuTrack.lastValid = true;
  imuTrack.everValid = true;
  imuTrack.lastValidMs = now;
  imuTrack.value = imuMagnitude;

  if (disturbance.update(ax, ay, az, now)) {
    activeEventId = nextEventId++;
    activeEventTs = deviceTime();
    activeEventLinesLeft = EVENT_REPEAT_LINES;
    stateDirty = true;
  }

  uint8_t stage = disturbance.stage();
  if (stage != lastCalStage) {
    if (stage == DIST_ARMED && (lastCalStage == DIST_CALIBRATING || lastCalStage == 255)) {
      snprintf(line, sizeof(line), "#CAL state=done;base=%.4f;noise=%.4f;on=%.4f;off=%.4f;rejects=%u",
               disturbance.baselineG(), disturbance.noiseG(), disturbance.onG(), disturbance.offG(),
               disturbance.calibrationRejects());
      sendLine(line);
    } else if (stage == DIST_FAULT) {
      snprintf(line, sizeof(line), "#CAL state=fault;base=%.4f;msg=implausible_baseline", disturbance.baselineG());
      sendLine(line);
    }
    lastCalStage = stage;
  }
}

// ------------------------------------------------------------------ VL53L0X

static bool initTof() {
  tofPresent = false;
  if (!i2cPresent(TOF_ADDRESS)) return false;
  tof.setBus(&Wire);
  tof.setTimeout(30);
  if (!tof.init()) return false;
  tof.startContinuous(TOF_PERIOD_MS);
  tofPresent = true;
  tofTrack.healthy = true;
  tofTrack.fails = 0;
  return true;
}

static void tofFailure(uint32_t now) {
  tofTrack.invalid++;
  tofTrack.lastValid = false;
  if (++tofTrack.fails >= FAILS_BEFORE_UNHEALTHY) {
    tofTrack.healthy = false;
    tofPresent = false;
    nextTofReinitMs = now + REINIT_PERIOD_MS;
    sendLine("#ERR sensor=tof;msg=read_failed");
  }
}

static void serviceTof(uint32_t now) {
  if (!tofPresent) {
    if (due(now, nextTofReinitMs, REINIT_PERIOD_MS) && !initTof()) sendLine("#ERR sensor=tof;msg=not_found");
    return;
  }

  // Non-blocking: read only when a measurement is ready.
  uint8_t ready = tof.readReg(VL53L0X::RESULT_INTERRUPT_STATUS);
  if (tof.last_status != 0) {
    tofFailure(now);
    return;
  }
  if ((ready & 0x07) == 0) return;

  uint8_t rangeStatus = (tof.readReg(VL53L0X::RESULT_RANGE_STATUS) >> 3) & 0x0F;
  uint16_t mm = tof.readRangeContinuousMillimeters();
  tofTrack.samples++;
  if (tof.timeoutOccurred() || tof.last_status != 0) {
    tofFailure(now);
    return;
  }
  tofTrack.fails = 0;
  tofTrack.healthy = true;

  if (mm >= TOF_FAR_MM || (rangeStatus == 11 && mm > TOF_MAX_MM)) {
    // Measured, nothing within range: a valid "far" reading -> NONE.
    tofFar = true;
    tofMedian.reset();
    tofClass.updateFar();
  } else if (rangeStatus == 11) {
    // Device range status 11 = range valid (VL53L0X API).
    tofFar = false;
    tofTrack.value = tofMedian.push(mm / 10.0f);
    tofClass.update(tofTrack.value);
  } else {
    // Sigma/signal/other failures: invalid sample, not "far".
    tofTrack.invalid++;
    tofTrack.lastValid = false;
    return;
  }
  tofTrack.lastValid = true;
  tofTrack.everValid = true;
  tofTrack.lastValidMs = now;
}

// ------------------------------------------------------------------ HC-SR04

static void IRAM_ATTR onEcho() {
  uint32_t t = micros();
  if (digitalRead(PIN_ECHO)) {
    echoRiseUs = t;
    echoRose = true;
  } else if (echoRose) {
    echoFallUs = t;
    echoDone = true;
  }
}

static void serviceUltrasonic(uint32_t now) {
  if (usPending) {
    bool done = echoDone;
    if (done) {
      usPending = false;
      usTrack.samples++;
      uint32_t width = echoFallUs - echoRiseUs;
      float cm = width * 0.0343f / 2.0f;  // speed of sound, out and back
      if (cm >= US_MIN_CM && cm <= US_MAX_CM) {
        usTrack.value = usMedian.push(cm);
        usClass.update(usTrack.value);
        usTrack.lastValid = true;
        usTrack.everValid = true;
        usTrack.lastValidMs = now;
        usTrack.fails = 0;
        usTrack.healthy = true;
      } else {
        usTrack.invalid++;
        usTrack.lastValid = false;
      }
    } else if (micros() - usTriggerUs > US_ECHO_TIMEOUT_US + 5000) {
      // No echo (or echo never ended): invalid sample per spec section 5.
      usPending = false;
      usTrack.samples++;
      usTrack.invalid++;
      usTrack.lastValid = false;
      if (++usTrack.fails >= FAILS_BEFORE_UNHEALTHY) usTrack.healthy = false;
    }
  }

  if (!usPending && due(now, nextUsMs, US_PERIOD_MS)) {
    echoRose = false;
    echoDone = false;
    digitalWrite(PIN_TRIG, LOW);
    delayMicroseconds(2);
    digitalWrite(PIN_TRIG, HIGH);
    delayMicroseconds(10);  // HC-SR04: 10 us trigger pulse
    digitalWrite(PIN_TRIG, LOW);
    usTriggerUs = micros();
    usPending = true;
  }
}

// ------------------------------------------------------------------ semantics + output

static bool fresh(const SensorTrack& s, uint32_t now, uint32_t windowMs) {
  return s.healthy && s.everValid && (now - s.lastValidMs) <= windowMs;
}

static void serviceState(uint32_t now) {
  bool tofValid = fresh(tofTrack, now, TOF_FRESH_MS);
  bool usValid = fresh(usTrack, now, US_FRESH_MS);
  FusedProximity fused = fuseProximity(tofValid, tofClass.current(), usValid, usClass.current());

  if (fused.presence != sentPresence || fused.confidence != sentConfidence) stateDirty = true;
  bool periodic = due(now, nextStateMs, STATE_PERIOD_MS);
  if (!stateDirty && !periodic) return;

  bool withEvent = activeEventLinesLeft > 0;
  sequence++;
  if (formatStateLine(line, sizeof(line), DEVICE_ID, bootId, sequence, deviceTime(), fused.presence,
                      fused.confidence, withEvent, activeEventId, activeEventTs) > 0)
    sendLine(line);
  if (withEvent) activeEventLinesLeft--;

  sentPresence = fused.presence;
  sentConfidence = fused.confidence;
  stateDirty = false;
  if (!periodic) nextStateMs = now + STATE_PERIOD_MS;
}

static uint32_t ageOf(const SensorTrack& s, uint32_t now) { return s.everValid ? now - s.lastValidMs : 0xFFFFFFFFu; }

static void serviceDiag(uint32_t now) {
  if (!due(now, nextDiagMs, DIAG_PERIOD_MS)) return;
  snprintf(line, sizeof(line),
           "#DIAG ts=%lu;tof_ok=%d;tof_far=%d;tof_cm=%.1f;tof_age=%ld;tof_cls=%s;us_ok=%d;us_cm=%.1f;us_age=%ld;"
           "us_cls=%s;imu_ok=%d;imu_g=%.3f;imu_d=%.4f;imu_on=%.4f;imu_cal=%u;evt=%lu;seq=%lu;loop_us=%lu",
           (unsigned long)deviceTime(), fresh(tofTrack, now, TOF_FRESH_MS) ? 1 : 0, tofFar ? 1 : 0, tofTrack.value,
           tofTrack.everValid ? (long)ageOf(tofTrack, now) : -1L, presenceName(tofClass.current()),
           fresh(usTrack, now, US_FRESH_MS) ? 1 : 0, usTrack.value, usTrack.everValid ? (long)ageOf(usTrack, now) : -1L,
           presenceName(usClass.current()), imuTrack.healthy ? 1 : 0, imuMagnitude, disturbance.filteredG(),
           disturbance.onG(), (unsigned)disturbance.stage(), (unsigned long)(nextEventId - 1),
           (unsigned long)sequence, (unsigned long)loopMaxUs);
  sendLine(line);
  loopMaxUs = 0;
}

static void sendHello() {
  snprintf(line, sizeof(line),
           "#HELLO proto=1;fw=%s;dev=%s;boot=%lu;baud=%lu;sda=%u;scl=%u;trig=%u;echo=%u;mpu=%s;tof=%s",
           FIRMWARE_VERSION, DEVICE_ID, (unsigned long)bootId, (unsigned long)SERIAL_BAUD, PIN_SDA, sclPin, PIN_TRIG,
           PIN_ECHO, mpuPresent ? (mpuAddress == 0x68 ? "0x68" : "0x69") : "none", tofPresent ? "0x29" : "none");
  sendLine(line);
}

// ------------------------------------------------------------------ Arduino

void setup() {
  Serial.setTxBufferSize(2048);  // keep prints from blocking the loop
  Serial.begin(SERIAL_BAUD);

  bootId = esp_random();
  if (bootId == 0) bootId = 1;

  pinMode(PIN_TRIG, OUTPUT);
  digitalWrite(PIN_TRIG, LOW);
  pinMode(PIN_ECHO, INPUT);
  attachInterrupt(digitalPinToInterrupt(PIN_ECHO), onEcho, CHANGE);

  selectBus();
  if (!initMpu()) sendLine("#ERR sensor=imu;msg=not_found");
  if (!initTof()) sendLine("#ERR sensor=tof;msg=not_found");
  sendHello();

  uint32_t now = millis();
  nextImuMs = nextTofMs = nextUsMs = nextStateMs = nextDiagMs = now;
  nextHelloMs = now + HELLO_PERIOD_MS;
  nextMpuReinitMs = nextTofReinitMs = now + REINIT_PERIOD_MS;
}

void loop() {
  uint32_t start = micros();
  uint32_t now = millis();

  if (due(now, nextImuMs, IMU_PERIOD_MS)) serviceImu(now);
  if (due(now, nextTofMs, 5)) serviceTof(now);  // poll data-ready often; ranging runs at TOF_PERIOD_MS
  serviceUltrasonic(now);
  serviceState(now);
  serviceDiag(now);
  if (due(now, nextHelloMs, HELLO_PERIOD_MS)) sendHello();

  uint32_t took = micros() - start;
  if (took > loopMaxUs) loopMaxUs = took;
}
