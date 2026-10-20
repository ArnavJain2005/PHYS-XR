using UnityEngine;

namespace PHYSXR.Game.Player
{
    // Collision-aware walking and first-person look on a CharacterController.
    // It knows nothing about devices: PlayerController passes it intents from
    // whichever IPlayerInput is active. Yaw turns the body; pitch turns only
    // the head (camera) and is clamped.
    [RequireComponent(typeof(CharacterController))]
    public class DesktopMotor : MonoBehaviour
    {
        [SerializeField] private float walkSpeed = 2.2f;
        [SerializeField] private float gravity = -9.81f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        private CharacterController characterController;
        private Transform head;
        private float pitch;
        private float verticalSpeed;

        public float WalkSpeed => walkSpeed;

        public float Pitch => pitch;

        public float MinPitch => minPitch;

        public float MaxPitch => maxPitch;

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
            if (deltaTime <= 0f)
                return;

            Vector2 intent = Vector2.ClampMagnitude(moveIntent, 1f);
            Vector3 planar = (transform.right * intent.x + transform.forward * intent.y) * walkSpeed;

            if (characterController.isGrounded && verticalSpeed < 0f)
                verticalSpeed = -1f;
            verticalSpeed += gravity * deltaTime;

            characterController.Move((planar + Vector3.up * verticalSpeed) * deltaTime);
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
            ApplyPitch();
        }

        private void ApplyPitch()
        {
            if (head != null)
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }
}
