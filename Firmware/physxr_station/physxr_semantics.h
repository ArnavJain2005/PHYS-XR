// PHYS-XR sensing station: semantic core.
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
inline FusedProximity fuseProximity(bool tofValid, Presence tof, bool usValid, Presence us) {
  FusedProximity out;
  if (tofValid && usValid) {
    if (tof == us) {
      out.presence = tof;
      out.confidence = CONFIDENCE_HIGH;
    } else {
      out.presence = tof < us ? tof : us;
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
