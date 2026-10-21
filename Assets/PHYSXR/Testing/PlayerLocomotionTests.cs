using System;
using PHYSXR.Game.Flow;

namespace PHYSXR.Testing
{
    // LOCO suite: desktop sprint stamina and jump rules (SprintStamina,
    // JumpRules). The CharacterController side (grounding, ceilings) is
    // checked in the player build by EncounterSelfTest.
    public static class PlayerLocomotionTests
    {
        public static int RunAll()
        {
            int passed = 0;
            int failed = 0;

            void Run(string name, Action test)
            {
                try
                {
                    test();
                    passed++;
                    Console.WriteLine("PASS: " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL: " + name + " - " + ex.Message);
                }
            }

            Run("LOCO-01_Walk_Default", L01);
            Run("LOCO-02_Sprint_OnlyHeldAndMovingForward", L02);
            Run("LOCO-03_Stamina_Lasts4s_ThenExhausted", L03);
            Run("LOCO-04_Exhausted_NoSprintUntil25Percent", L04);
            Run("LOCO-05_Regen_WaitsDelay_FullIn3s", L05);
            Run("LOCO-06_Reset_FullBar", L06);
            Run("LOCO-07_Jump_GroundedOnly_NoAirJump", L07);
            Run("LOCO-08_Jump_TakeoffReachesHeight", L08);

            Console.WriteLine("PlayerLocomotionTests: " + passed + " passed, " + failed + " failed");
            return failed;
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void AssertNear(float expected, float actual, float tolerance, string context)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new Exception(context + ": expected " + expected + " but got " + actual);
        }

        private const float Dt = 0.02f;

        // Steps n frames; returns the last speed.
        private static float Steps(SprintStamina s, int n, bool sprint, bool forward)
        {
            float speed = 0f;
            for (int i = 0; i < n; i++)
                speed = s.Step(sprint, forward, Dt);
            return speed;
        }

        private static void L01()
        {
            var s = new SprintStamina();
            AssertNear(SprintStamina.WalkSpeed, s.Step(false, true, Dt), 1e-6f, "walk");
            AssertTrue(!s.Sprinting && s.Stamina01 == 1f, "full bar, walking");
            AssertTrue(SprintStamina.SprintSpeed > SprintStamina.WalkSpeed * 1.5f, "sprint is clearly faster");
        }

        private static void L02()
        {
            var s = new SprintStamina();
            AssertNear(SprintStamina.SprintSpeed, s.Step(true, true, Dt), 1e-6f, "held + forward sprints");
            AssertTrue(s.Sprinting, "sprinting flag");
            AssertNear(SprintStamina.WalkSpeed, s.Step(true, false, Dt), 1e-6f, "held but not moving forward walks");
            AssertNear(SprintStamina.WalkSpeed, s.Step(false, true, Dt), 1e-6f, "released walks");
        }

        private static void L03()
        {
            var s = new SprintStamina();
            int frames = (int)(SprintStamina.StaminaSeconds / Dt);
            Steps(s, frames - 5, true, true);
            AssertTrue(s.Sprinting && !s.Exhausted, "still sprinting just before 4 s");
            Steps(s, 10, true, true);
            AssertTrue(s.Exhausted && s.Stamina01 <= 0f, "exhausted at 4 s");
            AssertNear(SprintStamina.WalkSpeed, s.Step(true, true, Dt), 1e-6f, "walk while exhausted");
        }

        private static void L04()
        {
            var s = new SprintStamina();
            Steps(s, (int)(SprintStamina.StaminaSeconds / Dt) + 5, true, true);
            AssertTrue(s.Exhausted, "exhausted");
            // Keep holding: delay is reset only by sprinting, so regen starts.
            float t = 0f;
            while (s.Exhausted && t < 10f)
            {
                s.Step(true, true, Dt);
                t += Dt;
            }
            AssertTrue(!s.Exhausted, "recovers");
            // The step that clears exhaustion (at >= 25%) also spends one
            // frame of sprint, since the key is still held.
            float oneFrame = Dt / SprintStamina.StaminaSeconds;
            AssertTrue(s.Stamina01 >= SprintStamina.RestartFraction - oneFrame - 1e-4f, "only at 25% (" + s.Stamina01 + ")");
            AssertTrue(s.Sprinting, "the held key sprints again on recovery");
            float expected = SprintStamina.RegenDelaySeconds + SprintStamina.RestartFraction * SprintStamina.RegenSeconds;
            AssertNear(expected, t, 0.1f, "time to recover");
            AssertNear(SprintStamina.SprintSpeed, s.Step(true, true, Dt), 1e-6f, "sprint resumes");
        }

        private static void L05()
        {
            var s = new SprintStamina();
            Steps(s, 50, true, true); // 1 s of sprint: 75% left
            float after = s.Stamina01;
            AssertNear(0.75f, after, 0.01f, "1 s costs a quarter");
            Steps(s, (int)(0.7f / Dt), false, true);
            AssertNear(after, s.Stamina01, 1e-6f, "no regen during the 0.8 s delay");
            Steps(s, (int)(SprintStamina.RegenSeconds / Dt) + 10, false, false);
            AssertNear(1f, s.Stamina01, 1e-6f, "full again");
        }

        private static void L06()
        {
            var s = new SprintStamina();
            Steps(s, 300, true, true);
            s.Reset();
            AssertTrue(s.Stamina01 == 1f && !s.Exhausted && !s.Sprinting, "reset");
        }

        private static void L07()
        {
            AssertTrue(JumpRules.CanJump(true, true, -1f), "grounded press jumps");
            AssertTrue(!JumpRules.CanJump(false, true, -1f), "no press, no jump");
            AssertTrue(!JumpRules.CanJump(true, false, -2f), "no jump in the air (falling)");
            AssertTrue(!JumpRules.CanJump(true, false, 2f), "no jump in the air (rising)");
            AssertTrue(!JumpRules.CanJump(true, true, 3f), "no re-jump while still rising");
        }

        private static void L08()
        {
            float g = -9.81f;
            float v = JumpRules.TakeoffSpeed(JumpRules.JumpHeight, g);
            // Integrate like DesktopMotor (gravity applied before the move).
            float y = 0f, peak = 0f;
            for (int i = 0; i < 200 && (i == 0 || y > 0f); i++)
            {
                v += g * 0.005f;
                y += v * 0.005f;
                peak = Math.Max(peak, y);
            }
            AssertNear(JumpRules.JumpHeight, peak, 0.03f, "peak height");
            AssertTrue(JumpRules.JumpHeight < 1.2f, "jump stays modest (furniture, not walls)");
        }
    }
}
