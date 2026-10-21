using System;

namespace PHYSXR.Game.Flow
{
    // Plain C# rules for desktop sprint and jump, kept out of DesktopMotor so
    // the standalone harness can test them (PlayerLocomotionTests).
    //
    // Sprint: hold the sprint intent while moving forward. Walking is 1.8 m/s,
    // sprinting 3.2 m/s. A full bar lasts 4 s of continuous sprint; it starts
    // refilling 0.8 s after the sprint stops and refills from empty in 3 s.
    // Running the bar dry exhausts the player, who cannot sprint again until
    // it is back to 25%. The ghost's lunge (3.5 m/s) is still faster.
    public class SprintStamina
    {
        public const float WalkSpeed = 1.8f;
        public const float SprintSpeed = 3.2f;
        public const float StaminaSeconds = 4f;
        public const float RegenDelaySeconds = 0.8f;
        public const float RegenSeconds = 3f;
        public const float RestartFraction = 0.25f;

        private float sinceSprint = RegenDelaySeconds;

        // 0 (empty) to 1 (full).
        public float Stamina01 { get; private set; } = 1f;

        // True if the last Step ran at sprint speed.
        public bool Sprinting { get; private set; }

        // True from the moment the bar empties until it refills to 25%.
        public bool Exhausted { get; private set; }

        // Advances the bar by one step and returns the planar speed to use.
        // movingForward: the move intent has a forward component.
        public float Step(bool sprintHeld, bool movingForward, float deltaSeconds)
        {
            if (deltaSeconds < 0f)
                deltaSeconds = 0f;

            if (Exhausted && Stamina01 >= RestartFraction)
                Exhausted = false;

            Sprinting = sprintHeld && movingForward && !Exhausted && Stamina01 > 0f;
            if (Sprinting)
            {
                sinceSprint = 0f;
                Stamina01 = Math.Max(0f, Stamina01 - deltaSeconds / StaminaSeconds);
                if (Stamina01 <= 0f)
                    Exhausted = true;
                return SprintSpeed;
            }

            sinceSprint += deltaSeconds;
            if (sinceSprint >= RegenDelaySeconds)
                Stamina01 = Math.Min(1f, Stamina01 + deltaSeconds / RegenSeconds);
            return WalkSpeed;
        }

        // Full bar, not sprinting (respawn).
        public void Reset()
        {
            Stamina01 = 1f;
            Sprinting = false;
            Exhausted = false;
            sinceSprint = RegenDelaySeconds;
        }
    }

    // Jump: one jump per press, only from the ground and never while still
    // rising, so holding or mashing the key cannot climb in mid-air.
    public static class JumpRules
    {
        public const float JumpHeight = 0.9f;

        // Vertical take-off speed that reaches `height` under `gravity` (negative).
        public static float TakeoffSpeed(float height, float gravity)
        {
            return (float)Math.Sqrt(2.0 * Math.Max(0f, height) * Math.Abs(gravity));
        }

        public static bool CanJump(bool pressed, bool grounded, float verticalSpeed)
        {
            return pressed && grounded && verticalSpeed <= 0f;
        }
    }
}
