using PHYSXR.Game.Flow;
using UnityEngine;

namespace PHYSXR.Game.Player
{
    // Collision-aware walking and first-person look on a CharacterController.
    // It knows nothing about devices: PlayerController passes it intents from
    // whichever IPlayerInput is active. Yaw turns the body; pitch turns only
    // the head (camera) and is clamped.
    //
    // Sprint and jump follow SprintStamina and JumpRules (Game/Flow): walk
    // 1.8 m/s, sprint 3.2 m/s with a 4 s stamina bar, jump 0.9 m from the
    // ground only. Hitting a ceiling ends the rise at once.
    [RequireComponent(typeof(CharacterController))]
    public class DesktopMotor : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = SprintStamina.WalkSpeed;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float jumpHeight = JumpRules.JumpHeight;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private readonly SprintStamina stamina = new SprintStamina();
        private CharacterController characterController;
        private Transform head;
        private float pitch;
        private float verticalSpeed;

        public float WalkSpeed => walkSpeed;

        public float SprintSpeed => SprintStamina.SprintSpeed;

        public float Pitch => pitch;

        public float MinPitch => minPitch;

        public float MaxPitch => maxPitch;

        public SprintStamina Stamina => stamina;

        public bool Sprinting => stamina.Sprinting;

        public bool Grounded => characterController != null && characterController.isGrounded;

        public float VerticalSpeed => verticalSpeed;

        // Counts jumps actually taken (self-tests).
        public int Jumps { get; private set; }

        // Set on the step the body lands after being airborne.
        public bool LandedThisStep { get; private set; }

        public void Initialize(Transform head)
        {
            this.head = head;
            characterController = GetComponent<CharacterController>();
            ApplyPitch();
        }

        public void Look(Vector2 lookDegrees)
        {
            transform.Rotate(0f, lookDegrees.x, 0f, Space.World);
            pitch = Mathf.Clamp(pitch - lookDegrees.y, minPitch, maxPitch);
            ApplyPitch();
        }

        public void Move(Vector2 moveIntent, float deltaTime)
        {
            Move(moveIntent, deltaTime, false, false);
        }

        public void Move(Vector2 moveIntent, float deltaTime, bool sprintHeld, bool jumpPressed)
        {
            LandedThisStep = false;
            if (deltaTime <= 0f)
                return;

            Vector2 intent = Vector2.ClampMagnitude(moveIntent, 1f);
            stamina.Step(sprintHeld, intent.y > 0.1f, deltaTime);
            float speed = stamina.Sprinting ? SprintStamina.SprintSpeed : walkSpeed;
            Vector3 planar = (transform.right * intent.x + transform.forward * intent.y) * speed;

            bool grounded = characterController.isGrounded;
            if (grounded && verticalSpeed < 0f)
                verticalSpeed = -1f;
            if (JumpRules.CanJump(jumpPressed, grounded, verticalSpeed))
            {
                verticalSpeed = JumpRules.TakeoffSpeed(jumpHeight, gravity);
                Jumps++;
            }
            verticalSpeed += gravity * deltaTime;

            CollisionFlags flags = characterController.Move((planar + Vector3.up * verticalSpeed) * deltaTime);
            if ((flags & CollisionFlags.Above) != 0 && verticalSpeed > 0f)
                verticalSpeed = 0f;
            LandedThisStep = !grounded && characterController.isGrounded;
        }

        // Places the body at a pose with level pitch. CharacterController
        // overrides direct transform writes unless it is briefly disabled.
        public void Teleport(Vector3 position, float yawDegrees)
        {
            characterController.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            characterController.enabled = true;
            pitch = 0f;
            verticalSpeed = 0f;
            stamina.Reset();
            ApplyPitch();
        }

        private void ApplyPitch()
        {
            if (head != null)
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
