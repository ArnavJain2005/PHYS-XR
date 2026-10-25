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
// Execution model: timed cooperative superloop with a 1 ms Wi-Fi yield. The
// ultrasonic echo is timed with a pin-change interrupt, so its 25 ms echo
// timeout never blocks IMU sampling or serial.
//
// Libraries: Wire and WiFi/WebServer (ESP32 core), "VL53L0X" by Pololu.
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
#include <WiFi.h>
#include <WebServer.h>

// Embedded PHYS-XR semantic core. Kept in this file so the sketch has no
// project-local header dependency; originally physxr_semantics.h in the repo.
//
// Everything in this header is plain C++ with no Arduino dependency, so the
// same code is compiled into the ESP32 sketch and into the host test
// (Firmware/tests/semantics_test.cpp). It turns validated sensor samples into
// the semantic PhysicalState the Unity side consumes (Hardware Specification
// v1 sections 6, 7 and 14; Technical Spec section E, contract v1):
//
//   ProximityClassifier  distance (cm) -> NONE / NEAR / VERY_NEAR, with
//                        separate enter/exit thresholds (hysteresis)
//   fuseProximity        ToF (primary) + ultrasonic (secondary) -> one
//                        presence + LOW/HIGH confidence, per the spec table;
//                        raw distances are never averaged
//   Median3              light filter for valid distance samples
//   DisturbanceDetector  MPU6050 accelerometer magnitude -> startup baseline,
//                        |A - baseline|, 3-sample median, two thresholds,
//                        consecutive-sample qualification, re-arm interval
//   formatStateLine      the contract v1 key=value line
//
// All thresholds are STARTING VALUES, not calibrated constants. The hardware
// specification leaves them to calibration on the mounted station
// (sections 5, 6.2 and 20); see Firmware/README.md for the procedure.

#ifndef PHYSXR_SEMANTICS_H
#define PHYSXR_SEMANTICS_H

#include <math.h>
#include <stdint.h>
#include <stdio.h>

namespace physxr {

enum Presence : uint8_t { PRESENCE_NONE = 0, PRESENCE_NEAR = 1, PRESENCE_VERY_NEAR = 2 };
enum Confidence : uint8_t { CONFIDENCE_LOW = 0, CONFIDENCE_HIGH = 1 };

// Wire names: these must match PHYSXR.Core.Enums.PresenceLevel /
// ConfidenceLevel exactly, because Unity's PacketValidator rejects anything else.
inline const char* presenceName(Presence p) {
  switch (p) {
    case PRESENCE_NEAR: return "NEAR";
    case PRESENCE_VERY_NEAR: return "VERY_NEAR";
    default: return "NONE";
  }
}

inline const char* confidenceName(Confidence c) { return c == CONFIDENCE_HIGH ? "HIGH" : "LOW"; }

// ---------------------------------------------------------------- proximity

struct ProximityThresholds {
  float veryNearEnterCm;  // enter VERY_NEAR at or below this distance
  float veryNearExitCm;   // leave VERY_NEAR above this distance
  float nearEnterCm;      // enter NEAR at or below this distance
  float nearExitCm;       // leave NEAR (to NONE) above this distance
};

// Per-sensor classifier with hysteresis: a distance between an enter and an
// exit threshold keeps the previous class, so noise around a boundary cannot
// make the class chatter.
class ProximityClassifier {
 public:
  explicit ProximityClassifier(const ProximityThresholds& t) : t_(t), current_(PRESENCE_NONE) {}

  Presence update(float distanceCm) {
    switch (current_) {
      case PRESENCE_VERY_NEAR:
        if (distanceCm > t_.nearExitCm) current_ = PRESENCE_NONE;
        else if (distanceCm > t_.veryNearExitCm) current_ = PRESENCE_NEAR;
        break;
      case PRESENCE_NEAR:
        if (distanceCm <= t_.veryNearEnterCm) current_ = PRESENCE_VERY_NEAR;
        else if (distanceCm > t_.nearExitCm) current_ = PRESENCE_NONE;
        break;
      default:
        if (distanceCm <= t_.veryNearEnterCm) current_ = PRESENCE_VERY_NEAR;
        else if (distanceCm <= t_.nearEnterCm) current_ = PRESENCE_NEAR;
        break;
    }
    return current_;
  }

  // "Nothing within range" from a sensor that did measure (ToF out of range).
  Presence updateFar() {
    current_ = PRESENCE_NONE;
    return current_;
  }

  Presence current() const { return current_; }
  void reset() { current_ = PRESENCE_NONE; }

 private:
  ProximityThresholds t_;
  Presence current_;
};

struct FusedProximity {
  Presence presence;
  Confidence confidence;
};

// Hardware Specification v1 section 7.1 / firmware workstream section 5.1:
//   both valid and equal         -> that class, HIGH
//   both valid and different     -> the less aggressive class, LOW
//   exactly one valid            -> that sensor's class, LOW
//   neither valid                -> NONE, LOW
// "Less aggressive" is the lower class (NONE < NEAR < VERY_NEAR), the initial
// policy the spec asks for until it is calibrated against the geometry.
inline FusedProximity fuseProximity(bool tofValid, Presence tof, bool usValid, Presence us, bool tofPrimary = false) {
  FusedProximity out;
  if (tofValid && usValid) {
    if (tof == us) {
      out.presence = tof;
      out.confidence = CONFIDENCE_HIGH;
    } else {
      out.presence = tofPrimary && tof != PRESENCE_NONE && us == PRESENCE_NONE ? tof : (tof < us ? tof : us);
      out.confidence = CONFIDENCE_LOW;
    }
  } else if (tofValid) {
    out.presence = tof;
    out.confidence = CONFIDENCE_LOW;
  } else if (usValid) {
    out.presence = us;
    out.confidence = CONFIDENCE_LOW;
  } else {
    out.presence = PRESENCE_NONE;
    out.confidence = CONFIDENCE_LOW;
  }
  return out;
}

// Median of the last three valid samples: rejects a single outlier (a false
// echo, one bad ToF frame) without lagging much at 15-25 Hz.
class Median3 {
 public:
  Median3() : count_(0), next_(0) { v_[0] = v_[1] = v_[2] = 0.0f; }

  float push(float x) {
    v_[next_] = x;
    next_ = (next_ + 1) % 3;
    if (count_ < 3) count_++;
    if (count_ == 1) return x;
    if (count_ == 2) return (v_[0] + v_[1]) * 0.5f;
    float a = v_[0], b = v_[1], c = v_[2];
    if (a > b) { float t = a; a = b; b = t; }
    if (b > c) { float t = b; b = c; c = t; }
    if (a > b) { float t = a; a = b; b = t; }
    return b;
  }

  void reset() { count_ = 0; next_ = 0; }

 private:
  float v_[3];
  uint8_t count_;
  uint8_t next_;
};

// ---------------------------------------------------------------- disturbance

struct DisturbanceConfig {
  uint16_t calibrationSamples;  // stationary samples for the baseline
  float maxCalibrationNoiseG;   // baseline rejected (and retried) above this std-dev
  float minBaselineG;           // plausible stationary |A| range; outside -> sensor fault
  float maxBaselineG;
  float onFloorG;               // THRESHOLD_ON is at least this ...
  float onNoiseMultiplier;      // ... or this many baseline std-devs, whichever is larger
  float offRatio;               // THRESHOLD_OFF = THRESHOLD_ON * offRatio (< 1)
  uint8_t qualifySamples;       // consecutive filtered samples above ON to qualify
  uint32_t minEventIntervalMs;  // re-arm no sooner than this after an event
};

enum DisturbanceStage : uint8_t {
  DIST_CALIBRATING = 0,  // collecting the stationary baseline; no events
  DIST_ARMED = 1,        // waiting for a qualified movement
  DIST_LATCHED = 2,      // event emitted; waiting for D < OFF and the interval
  DIST_FAULT = 3         // baseline implausible; no events until recalibrated
};

// Hardware Specification v1 section 6.2:
//   A = sqrt(ax^2 + ay^2 + az^2), D = |A - A_baseline|, D_f = median3(D)
//   D_f > ON for qualifySamples consecutive samples -> one event, latch
//   D_f < OFF and minEventInterval elapsed -> re-arm
// Magnitude only, so the mounting orientation and axis mapping do not matter.
// The filter is the spec's "small window" option, a 3-sample median: a
// low-pass would smear one spike over the following samples and let it pass
// the consecutive-sample qualification; a median removes it.
class DisturbanceDetector {
 public:
  explicit DisturbanceDetector(const DisturbanceConfig& c) : c_(c) { recalibrate(); }

  void recalibrate() {
    stage_ = DIST_CALIBRATING;
    n_ = 0;
    sum_ = 0.0;
    sumSq_ = 0.0;
    baseline_ = 0.0f;
    noise_ = 0.0f;
    onG_ = c_.onFloorG;
    offG_ = c_.onFloorG * c_.offRatio;
    filtered_ = 0.0f;
    window_.reset();
    above_ = 0;
    lastEventMs_ = 0;
    hasEvent_ = false;
    calibrationRejects_ = 0;
  }

  // One accelerometer sample in g. Returns true exactly once per qualified
  // disturbance (the caller then assigns the event ID).
  bool update(float ax, float ay, float az, uint32_t nowMs) {
    float a = sqrtf(ax * ax + ay * ay + az * az);
    if (!isfinite(a)) return false;

    if (stage_ == DIST_CALIBRATING) {
      sum_ += a;
      sumSq_ += (double)a * a;
      n_++;
      if (n_ >= c_.calibrationSamples) finishCalibration();
      return false;
    }
    if (stage_ == DIST_FAULT) return false;

    filtered_ = window_.push(fabsf(a - baseline_));

    if (stage_ == DIST_ARMED) {
      above_ = filtered_ > onG_ ? (uint8_t)(above_ + 1) : 0;
      if (above_ >= c_.qualifySamples) {
        stage_ = DIST_LATCHED;
        above_ = 0;
        lastEventMs_ = nowMs;
        hasEvent_ = true;
        return true;
      }
    } else if (stage_ == DIST_LATCHED) {
      if (filtered_ < offG_ && (uint32_t)(nowMs - lastEventMs_) >= c_.minEventIntervalMs) stage_ = DIST_ARMED;
    }
    return false;
  }

  DisturbanceStage stage() const { return stage_; }
  bool calibrated() const { return stage_ == DIST_ARMED || stage_ == DIST_LATCHED; }
  float baselineG() const { return baseline_; }
  float noiseG() const { return noise_; }
  float onG() const { return onG_; }
  float offG() const { return offG_; }
  float filteredG() const { return filtered_; }
  uint16_t calibrationRejects() const { return calibrationRejects_; }

 private:
  void finishCalibration() {
    double mean = sum_ / n_;
    double var = sumSq_ / n_ - mean * mean;
    if (var < 0.0) var = 0.0;
    float sd = (float)sqrt(var);

    if (sd > c_.maxCalibrationNoiseG) {
      // The station moved while calibrating: try again rather than store a
      // baseline that would hide or invent disturbances.
      calibrationRejects_++;
      n_ = 0;
      sum_ = 0.0;
      sumSq_ = 0.0;
      return;
    }

    baseline_ = (float)mean;
    noise_ = sd;
    if (baseline_ < c_.minBaselineG || baseline_ > c_.maxBaselineG) {
      stage_ = DIST_FAULT;
      return;
    }

    float k = noise_ * c_.onNoiseMultiplier;
    onG_ = k > c_.onFloorG ? k : c_.onFloorG;
    offG_ = onG_ * c_.offRatio;
    filtered_ = 0.0f;
    window_.reset();
    above_ = 0;
    stage_ = DIST_ARMED;
  }

  DisturbanceConfig c_;
  DisturbanceStage stage_;
  uint16_t n_;
  double sum_;
  double sumSq_;
  float baseline_;
  float noise_;
  float onG_;
  float offG_;
  float filtered_;
  Median3 window_;
  uint8_t above_;
  uint32_t lastEventMs_;
  bool hasEvent_;
  uint16_t calibrationRejects_;
};

// ---------------------------------------------------------------- wire format

// Contract v1 (Technical Spec section E), one line per message, '\n'
// terminated by the caller. Field order matches ProvisionalTextPacketFormat.EncodeV1
// on the Unity side. Returns the length written, or -1 if it did not fit.
inline int formatStateLine(char* buf, size_t size, const char* deviceId, uint32_t boot, uint32_t seq,
                           uint32_t ts, Presence presence, Confidence confidence, bool withEvent,
                           uint32_t eventId, uint32_t eventTs) {
  int n = snprintf(buf, size,
                   "v=1;dev=%s;boot=%lu;seq=%lu;ts=%lu;presence=%s;confidence=%s;disturbance=%s;"
                   "eventType=%s;eventId=%lu;tEvent=%lu",
                   deviceId, (unsigned long)boot, (unsigned long)seq, (unsigned long)ts, presenceName(presence),
                   confidenceName(confidence), withEvent ? "true" : "false", withEvent ? "DISTURBANCE" : "",
                   (unsigned long)(withEvent ? eventId : 0), (unsigned long)(withEvent ? eventTs : 0));
  return (n < 0 || (size_t)n >= size) ? -1 : n;
}

}  // namespace physxr

#endif  // PHYSXR_SEMANTICS_H

using namespace physxr;

// ------------------------------------------------------------------ config

static const char* const FIRMWARE_VERSION = "1.0.0";
static const char* const DEVICE_ID = "phys-xr-01";  // Unity's expected device ID
static const uint32_t SERIAL_BAUD = 115200;
// USB belongs to Unity. The other laptop joins this ESP32 access point and
// opens http://192.168.4.1/ in a browser; it never opens the USB COM port.
static const char* const DASHBOARD_SSID = "PHYS-XR-Station";
static const char* const DASHBOARD_PASSWORD = "CHANGE_ME_BEFORE_FLASHING"; // set your own (8-63 characters) before flashing; never commit a real one

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

// Fusion when both sensors are valid but only the ToF sees something
// (physxr_semantics.h, fuseProximity). false = the spec's conservative rule
// (NONE, LOW). Set true only if calibration shows the ultrasonic missing a
// hand the ToF sees; the result is then the ToF class at LOW confidence.
static const bool FUSION_TOF_PRIMARY = false;

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
static WebServer dashboard(80);
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
static float accelX = 0.0f, accelY = 0.0f, accelZ = 0.0f;
static float gyroX = 0.0f, gyroY = 0.0f, gyroZ = 0.0f;
static uint8_t lastCalStage = 255;

static Presence sentPresence = PRESENCE_NONE;
static Confidence sentConfidence = CONFIDENCE_LOW;
static FusedProximity currentState = {PRESENCE_NONE, CONFIDENCE_LOW};
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

static char line[512];

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

  // The first six bytes drive the unchanged disturbance detector. The other
  // eight supply the temperature skip and dashboard gyro readings.
  uint8_t raw[14];
  imuTrack.samples++;
  if (!i2cRead(mpuAddress, 0x3B, raw, 14)) {
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
  accelX = ax; accelY = ay; accelZ = az;
  gyroX = (int16_t)((raw[8] << 8) | raw[9]) / 131.0f;
  gyroY = (int16_t)((raw[10] << 8) | raw[11]) / 131.0f;
  gyroZ = (int16_t)((raw[12] << 8) | raw[13]) / 131.0f;
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
  FusedProximity fused = fuseProximity(tofValid, tofClass.current(), usValid, usClass.current(), FUSION_TOF_PRIMARY);
  currentState = fused;

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

// ------------------------------------------------------------------ dashboard

// Read-only browser view. It displays cached samples and the exact fused
// semantic state sent to Unity. It does not sample I2C or consume the COM port.
static const char DASHBOARD_HTML[] PROGMEM = R"HTML(<!doctype html>
<html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PHYS-XR Station</title>
<style>
:root{color-scheme:dark;font:16px system-ui,sans-serif;background:#0a111e;color:#e8f1ff}
body{max-width:1100px;margin:auto;padding:25px}h1{font-size:2rem;margin:.2em 0}p{color:#aabdd2}
.top{display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;align-items:center}
.pill{border:1px solid #547798;border-radius:30px;padding:8px 14px;color:#9be8cc}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(235px,1fr));gap:16px;margin-top:22px}
.card{border:1px solid #29415a;border-radius:16px;padding:20px;background:#142238}
.label{color:#9fbbd2;font-size:.9rem}.value{font-size:2rem;font-weight:700;margin:10px 0}
.detail{line-height:1.8;color:#bfd1e2}.note{margin-top:25px;font-size:.9rem}
</style>
<div class="top"><div><h1>PHYS-XR sensing station</h1><p>Live readings and firmware state · ESP32 access point</p></div><span class="pill" id="connection">Connecting…</span></div>
<main class="grid">
<section class="card"><div class="label">VL53L0X · laser distance</div><div class="value" id="tof">—</div><div class="detail" id="tofDetails">Waiting for data</div></section>
<section class="card"><div class="label">HC-SR04 · ultrasonic distance</div><div class="value" id="sonar">—</div><div class="detail" id="sonarDetails">Waiting for data</div></section>
<section class="card"><div class="label">MPU6050 · movement</div><div class="value" id="imu">—</div><div class="detail" id="imuDetails">Waiting for data</div></section>
<section class="card"><div class="label">Game input · fused physical state</div><div class="value" id="presence">—</div><div class="detail" id="stateDetails">Waiting for data</div></section>
</main><p class="note">Presence and confidence are the physical state sent to Unity. Player position, ghost action and other in-game virtual state come from Unity and are not available on the ESP32.</p>
<script>
const el=id=>document.getElementById(id);
const number=(x,d=1)=>typeof x==='number'?x.toFixed(d):'—';
async function refresh(){try{
  const response=await fetch('/api',{cache:'no-store'});if(!response.ok)throw Error('HTTP '+response.status);
  const x=await response.json();
  el('connection').textContent='LIVE · '+x.clients+' Wi-Fi client(s)';
  el('tof').textContent=x.tof.ok?x.tof.far?'Out of range':number(x.tof.cm)+' cm':'Unavailable';
  el('tofDetails').textContent='Class: '+(x.tof.ok?x.tof.class:'—')+' · sample age: '+(x.tof.age_ms??'—')+' ms';
  el('sonar').textContent=x.sonar.ok?number(x.sonar.cm)+' cm':'No valid echo';
  el('sonarDetails').textContent='Class: '+(x.sonar.ok?x.sonar.class:'—')+' · sample age: '+(x.sonar.age_ms??'—')+' ms';
  el('imu').textContent=x.imu.ok?number(x.imu.magnitude_g,3)+' g':'Unavailable';
  el('imuDetails').textContent=x.imu.ok?'Accel X/Y/Z: '+[x.imu.ax,x.imu.ay,x.imu.az].map(v=>number(v,2)).join(' / ')+' g · Gyro X/Y/Z: '+[x.imu.gx,x.imu.gy,x.imu.gz].map(v=>number(v,1)).join(' / ')+' °/s · deviation: '+number(x.imu.deviation_g,3)+' g · calibration: '+x.imu.calibration:'Calibration: '+x.imu.calibration;
  el('presence').textContent=x.state.presence;
  el('stateDetails').textContent='Confidence: '+x.state.confidence+' · event ID: '+x.state.last_event_id+' · boot: '+x.boot+' · seq: '+x.seq;
}catch(e){el('connection').textContent='DISCONNECTED · '+e.message}}
refresh();setInterval(refresh,500);
</script></html>)HTML";

static void jsonNumber(char* dest, size_t size, bool valid, float value, unsigned decimals) {
  if (valid && isfinite(value)) snprintf(dest, size, "%.*f", decimals, value);
  else snprintf(dest, size, "null");
}

static void serveApi() {
  uint32_t now = millis();
  bool tofOk = fresh(tofTrack, now, TOF_FRESH_MS);
  bool usOk = fresh(usTrack, now, US_FRESH_MS);
  bool imuOk = fresh(imuTrack, now, 100);
  char tofCm[24], usCm[24], tofAge[16], usAge[16];
  jsonNumber(tofCm, sizeof(tofCm), tofOk && !tofFar, tofTrack.value, 1);
  jsonNumber(usCm, sizeof(usCm), usOk, usTrack.value, 1);
  if (tofOk) snprintf(tofAge, sizeof(tofAge), "%lu", (unsigned long)ageOf(tofTrack, now));
  else snprintf(tofAge, sizeof(tofAge), "null");
  if (usOk) snprintf(usAge, sizeof(usAge), "%lu", (unsigned long)ageOf(usTrack, now));
  else snprintf(usAge, sizeof(usAge), "null");

  char response[1024];
  int length = snprintf(response, sizeof(response),
      "{\"boot\":%lu,\"seq\":%lu,\"clients\":%u,"
      "\"tof\":{\"ok\":%s,\"far\":%s,\"cm\":%s,\"age_ms\":%s,\"class\":\"%s\"},"
      "\"sonar\":{\"ok\":%s,\"cm\":%s,\"age_ms\":%s,\"class\":\"%s\"},"
      "\"imu\":{\"ok\":%s,\"magnitude_g\":%.3f,\"ax\":%.3f,\"ay\":%.3f,\"az\":%.3f,"
      "\"gx\":%.2f,\"gy\":%.2f,\"gz\":%.2f,\"deviation_g\":%.4f,\"calibration\":%u},"
      "\"state\":{\"presence\":\"%s\",\"confidence\":\"%s\",\"last_event_id\":%lu}}",
      (unsigned long)bootId, (unsigned long)sequence, (unsigned)WiFi.softAPgetStationNum(),
      tofOk ? "true" : "false", tofFar ? "true" : "false", tofCm, tofAge, presenceName(tofClass.current()),
      usOk ? "true" : "false", usCm, usAge, presenceName(usClass.current()),
      imuOk ? "true" : "false", imuMagnitude, accelX, accelY, accelZ, gyroX, gyroY, gyroZ,
      disturbance.filteredG(), (unsigned)disturbance.stage(),
      presenceName(currentState.presence), confidenceName(currentState.confidence),
      (unsigned long)(nextEventId - 1));
  dashboard.sendHeader("Cache-Control", "no-store");
  if (length < 0 || (size_t)length >= sizeof(response)) dashboard.send(500, "text/plain", "Response too large");
  else dashboard.send(200, "application/json", response);
}

static bool dashboardReady = false;
static void startDashboard() {
  WiFi.mode(WIFI_AP);
  if (!WiFi.softAP(DASHBOARD_SSID, DASHBOARD_PASSWORD)) {
    sendLine("#ERR sensor=wifi;msg=ap_start_failed");
    return;
  }
  dashboard.on("/", HTTP_GET, []() { dashboard.send_P(200, "text/html; charset=utf-8", DASHBOARD_HTML); });
  dashboard.on("/api", HTTP_GET, serveApi);
  dashboard.begin();
  dashboardReady = true;
  snprintf(line, sizeof(line), "#DASH ssid=%s;url=http://%s/", DASHBOARD_SSID, WiFi.softAPIP().toString().c_str());
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
  startDashboard();

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
  if (dashboardReady) dashboard.handleClient();

  uint32_t took = micros() - start;
  if (took > loopMaxUs) loopMaxUs = took;
  delay(1);  // yield to the ESP32 Wi-Fi task without changing sensor deadlines
}
