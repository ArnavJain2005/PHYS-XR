// Host test for Firmware/physxr_station/physxr_semantics.h (no ESP32 needed).
//
//   g++ -std=c++17 -Wall -Wextra -I Firmware/physxr_station Firmware/tests/semantics_test.cpp -o semantics_test
//   ./semantics_test        (exit code = failures)
//
// The golden lines at the end are also validated by Unity's PacketValidator
// in Assets/PHYSXR/Testing/SerialTransportTests.cs, so a change on either
// side of the contract fails a test.

#include <math.h>
#include <stdio.h>
#include <string.h>

#include "physxr_semantics.h"

using namespace physxr;

static int failures = 0;
static int checks = 0;

#define CHECK(cond, msg)                                       \
  do {                                                         \
    checks++;                                                  \
    if (!(cond)) {                                             \
      failures++;                                              \
      printf("FAIL: %s (%s:%d)\n", msg, __FILE__, __LINE__);   \
    }                                                          \
  } while (0)

static const ProximityThresholds T = {30.0f, 35.0f, 80.0f, 90.0f};

static const DisturbanceConfig D = {200, 0.03f, 0.7f, 1.3f, 0.08f, 6.0f, 0.5f, 3, 1500};

// Deterministic pseudo-noise in [-1, 1].
static float noise(uint32_t& s) {
  s = s * 1664525u + 1013904223u;
  return ((s >> 8) & 0xFFFF) / 32767.5f - 1.0f;
}

static void testClassifierHysteresis() {
  ProximityClassifier c(T);
  CHECK(c.update(200) == PRESENCE_NONE, "far is NONE");
  CHECK(c.update(80) == PRESENCE_NEAR, "80 cm enters NEAR");
  CHECK(c.update(85) == PRESENCE_NEAR, "85 cm inside NEAR exit band stays NEAR");
  CHECK(c.update(90) == PRESENCE_NEAR, "90 cm (exit threshold) stays NEAR");
  CHECK(c.update(90.5f) == PRESENCE_NONE, "above 90 cm leaves NEAR");
  CHECK(c.update(85) == PRESENCE_NONE, "85 cm from NONE does not enter NEAR");
  CHECK(c.update(30) == PRESENCE_VERY_NEAR, "30 cm enters VERY_NEAR directly");
  CHECK(c.update(34) == PRESENCE_VERY_NEAR, "34 cm stays VERY_NEAR");
  CHECK(c.update(36) == PRESENCE_NEAR, "36 cm drops to NEAR");
  CHECK(c.update(33) == PRESENCE_NEAR, "33 cm from NEAR does not re-enter VERY_NEAR");
  CHECK(c.update(29) == PRESENCE_VERY_NEAR, "29 cm re-enters VERY_NEAR");
  CHECK(c.update(120) == PRESENCE_NONE, "jump to far from VERY_NEAR gives NONE");
  CHECK(c.updateFar() == PRESENCE_NONE, "far reading is NONE");

  // Noisy boundary: readings jittering +/-4 cm around 80 cm after entering NEAR
  // must not chatter back to NONE.
  ProximityClassifier n(T);
  n.update(78);
  uint32_t s = 7;
  int changes = 0;
  Presence last = n.current();
  for (int i = 0; i < 500; i++) {
    Presence p = n.update(82.0f + 4.0f * noise(s));
    if (p != last) changes++;
    last = p;
  }
  CHECK(changes == 0, "jitter around the NEAR threshold does not chatter");
}

static void testFusionTable() {
  FusedProximity f;
  f = fuseProximity(true, PRESENCE_NONE, true, PRESENCE_NONE);
  CHECK(f.presence == PRESENCE_NONE && f.confidence == CONFIDENCE_HIGH, "NONE+NONE -> NONE HIGH");
  f = fuseProximity(true, PRESENCE_NEAR, true, PRESENCE_NEAR);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_HIGH, "NEAR+NEAR -> NEAR HIGH");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, true, PRESENCE_VERY_NEAR);
  CHECK(f.presence == PRESENCE_VERY_NEAR && f.confidence == CONFIDENCE_HIGH, "VN+VN -> VN HIGH");
  f = fuseProximity(true, PRESENCE_NEAR, true, PRESENCE_VERY_NEAR);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_LOW, "NEAR+VN -> NEAR LOW (conservative)");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, true, PRESENCE_NEAR);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_LOW, "VN+NEAR -> NEAR LOW (conservative)");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, true, PRESENCE_NONE);
  CHECK(f.presence == PRESENCE_NONE && f.confidence == CONFIDENCE_LOW, "VN+NONE -> NONE LOW");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, false, PRESENCE_NONE);
  CHECK(f.presence == PRESENCE_VERY_NEAR && f.confidence == CONFIDENCE_LOW, "ToF only -> ToF class LOW");
  f = fuseProximity(false, PRESENCE_VERY_NEAR, true, PRESENCE_NEAR);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_LOW, "US only -> US class LOW");
  f = fuseProximity(false, PRESENCE_VERY_NEAR, false, PRESENCE_VERY_NEAR);
  CHECK(f.presence == PRESENCE_NONE && f.confidence == CONFIDENCE_LOW, "both invalid -> NONE LOW, stale class ignored");
}

// FUSION_TOF_PRIMARY: only the ToF-sees / ultrasonic-NONE case changes.
static void testFusionToFPrimary() {
  FusedProximity f;
  f = fuseProximity(true, PRESENCE_NEAR, true, PRESENCE_NONE, true);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_LOW, "ToF-primary: NEAR+NONE -> NEAR LOW");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, true, PRESENCE_NONE, true);
  CHECK(f.presence == PRESENCE_VERY_NEAR && f.confidence == CONFIDENCE_LOW, "ToF-primary: VN+NONE -> VN LOW");
  f = fuseProximity(true, PRESENCE_NONE, true, PRESENCE_NEAR, true);
  CHECK(f.presence == PRESENCE_NONE && f.confidence == CONFIDENCE_LOW, "ToF-primary: ultrasonic alone never creates presence");
  f = fuseProximity(true, PRESENCE_VERY_NEAR, true, PRESENCE_NEAR, true);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_LOW, "ToF-primary: VN+NEAR still the conservative class");
  f = fuseProximity(true, PRESENCE_NEAR, true, PRESENCE_NEAR, true);
  CHECK(f.presence == PRESENCE_NEAR && f.confidence == CONFIDENCE_HIGH, "ToF-primary: agreement still HIGH");
  f = fuseProximity(false, PRESENCE_NEAR, false, PRESENCE_NEAR, true);
  CHECK(f.presence == PRESENCE_NONE && f.confidence == CONFIDENCE_LOW, "ToF-primary: both invalid -> NONE LOW");
}

static void testMedian() {
  Median3 m;
  CHECK(fabsf(m.push(50) - 50) < 1e-4f, "first sample passes through");
  m.push(52);
  CHECK(fabsf(m.push(400) - 52) < 1e-4f, "single outlier rejected");
  CHECK(fabsf(m.push(51) - 52) < 1e-4f, "median of 51,52,400 is 52");
  CHECK(fabsf(m.push(53) - 53) < 1e-4f, "outlier leaves the window");
}

static uint32_t calibrate(DisturbanceDetector& d, uint32_t t, float g, float noiseG, uint32_t& s) {
  for (int i = 0; i < 200; i++, t += 10) d.update(0.01f, -0.02f, g + noiseG * noise(s), t);
  return t;
}

static void testDisturbance() {
  uint32_t s = 42;
  DisturbanceDetector d(D);
  CHECK(d.stage() == DIST_CALIBRATING, "starts calibrating");
  uint32_t t = 0;
  // A big movement during calibration produces no event.
  bool any = false;
  for (int i = 0; i < 199; i++, t += 10) any |= d.update(0, 0, 1.0f + (i == 50 ? 1.5f : 0.0f), t);
  CHECK(!any, "no event while calibrating");

  DisturbanceDetector moving(D);
  t = 0;
  for (int i = 0; i < 200; i++, t += 10) moving.update(0, 0, 1.0f + 0.2f * noise(s), t);
  CHECK(moving.stage() == DIST_CALIBRATING && moving.calibrationRejects() == 1, "noisy calibration rejected and retried");

  DisturbanceDetector still(D);
  t = calibrate(still, 0, 1.0f, 0.004f, s);
  CHECK(still.stage() == DIST_ARMED, "calibrates when still");
  CHECK(fabsf(still.baselineG() - 1.0f) < 0.01f, "baseline ~1 g");
  CHECK(still.onG() >= 0.08f - 1e-6f, "ON threshold has the floor");
  CHECK(fabsf(still.offG() - still.onG() * 0.5f) < 1e-6f, "OFF = 0.5 ON");

  // Stationary for 60 s: no events.
  int events = 0;
  for (int i = 0; i < 6000; i++, t += 10) events += still.update(0.01f, -0.02f, 1.0f + 0.004f * noise(s), t);
  CHECK(events == 0, "stationary device generates no events");

  // One-sample spike: not qualified.
  events += still.update(0, 0, 2.5f, t);
  t += 10;
  for (int i = 0; i < 50; i++, t += 10) events += still.update(0.01f, -0.02f, 1.0f, t);
  CHECK(events == 0, "single-sample spike is not an event");

  // A deliberate shake (~150 ms at +0.4 g): exactly one event.
  for (int i = 0; i < 15; i++, t += 10) events += still.update(0, 0, 1.4f, t);
  for (int i = 0; i < 100; i++, t += 10) events += still.update(0.01f, -0.02f, 1.0f, t);
  CHECK(events == 1, "one movement -> one event");

  // Continuous shaking for 1 s right after: still within the 1.5 s re-arm.
  for (int i = 0; i < 30; i++, t += 10) events += still.update(0, 0, i % 2 ? 1.5f : 0.5f, t);
  CHECK(events == 1, "no retrigger before re-arm interval / below OFF");

  // Settle, then a second movement after re-arm: second event.
  for (int i = 0; i < 300; i++, t += 10) events += still.update(0.01f, -0.02f, 1.0f, t);
  for (int i = 0; i < 15; i++, t += 10) events += still.update(0, 0, 1.4f, t);
  CHECK(events == 2, "second movement after re-arm -> second event");

  // Implausible baseline (sensor fault): no events ever.
  DisturbanceDetector broken(D);
  t = calibrate(broken, 0, 0.0f, 0.0f, s);
  CHECK(broken.stage() == DIST_FAULT, "0 g baseline is a fault");
  events = 0;
  for (int i = 0; i < 100; i++, t += 10) events += broken.update(0, 0, 2.0f, t);
  CHECK(events == 0, "faulted detector emits nothing");

  CHECK(!still.update(NAN, 0, 0, t), "NaN sample ignored");
}

static void testFormat() {
  char buf[256];
  int n = formatStateLine(buf, sizeof(buf), "phys-xr-01", 2864130047u, 1234, 987654, PRESENCE_NEAR, CONFIDENCE_HIGH,
                          false, 9, 9);
  CHECK(n > 0, "state line fits");
  CHECK(strcmp(buf,
                "v=1;dev=phys-xr-01;boot=2864130047;seq=1234;ts=987654;presence=NEAR;confidence=HIGH;"
                "disturbance=false;eventType=;eventId=0;tEvent=0") == 0,
        "golden state line");
  formatStateLine(buf, sizeof(buf), "phys-xr-01", 2864130047u, 1235, 987702, PRESENCE_VERY_NEAR, CONFIDENCE_LOW,
                  true, 17, 987690);
  CHECK(strcmp(buf,
               "v=1;dev=phys-xr-01;boot=2864130047;seq=1235;ts=987702;presence=VERY_NEAR;confidence=LOW;"
               "disturbance=true;eventType=DISTURBANCE;eventId=17;tEvent=987690") == 0,
        "golden event line");
  CHECK(formatStateLine(buf, 40, "phys-xr-01", 1, 1, 1, PRESENCE_NONE, CONFIDENCE_LOW, false, 0, 0) == -1,
        "truncation reported");
  CHECK(strlen(buf) < 512, "under Unity's 512-byte limit");
}

int main() {
  testClassifierHysteresis();
  testFusionTable();
  testFusionToFPrimary();
  testMedian();
  testDisturbance();
  testFormat();
  printf("semantics_test: %d checks, %d failed\n", checks, failures);
  return failures;
}
