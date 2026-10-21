using UnityEngine;
using UnityEngine.InputSystem;

namespace PHYSXR.Game.Input
{
    // Windows keyboard/mouse adapter (GDD 7.1): WASD move, mouse look,
    // E interact, F lantern pulse (the designated defensive key), Esc
    // pause/resume. Mouse sensitivity and invert-Y come from GameSettings.
    //
    // Move, Look and Interact come from the project-wide actions asset
    // (Assets/InputSystem_Actions.inputactions, "Player" map), which is left
    // unmodified. That asset has no pause action, so Pause is a local action
    // bound to Escape. If the project-wide asset is missing, equivalent
    // local bindings are created so the game is still playable.
    public class DesktopInputAdapter : MonoBehaviour, IPlayerInput
    {
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        [SerializeField] private float mouseDegreesPerPixel = 0.1f;

        [Tooltip("Degrees per second at full right-stick deflection.")]
        [SerializeField] private float stickDegreesPerSecond = 120f;

        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction interactAction;
        private InputAction pauseAction;
        private InputAction defendAction;
        private bool ownsPlayerActions;

        public Vector2 Move => moveAction != null ? Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f) : Vector2.zero;

        public Vector2 LookDegrees
        {
            get
            {
                if (lookAction == null)
                    return Vector2.zero;

                Vector2 raw = lookAction.ReadValue<Vector2>();
                InputControl control = lookAction.activeControl;
                Vector2 degrees;
                if (control != null && control.device is Pointer)
                    degrees = raw * mouseDegreesPerPixel;
                else
                    // Sticks report deflection, not a per-frame delta.
                    degrees = raw * (stickDegreesPerSecond * Time.unscaledDeltaTime);

                if (InvertY)
                    degrees.y = -degrees.y;
                return degrees;
            }
        }

        public bool InteractPressed => interactAction != null && interactAction.WasPressedThisFrame();

        public bool PausePressed => pauseAction != null && pauseAction.WasPressedThisFrame();

        public bool DefendPressed => defendAction != null && defendAction.WasPressedThisFrame();

        public bool InvertY { get; set; }

        // Degrees per pixel of mouse movement (from GameSettings).
        public float MouseSensitivity
        {
            get => mouseDegreesPerPixel;
            set => mouseDegreesPerPixel = Mathf.Clamp(value, 0.01f, 1f);
        }

        private void Awake()
        {
            InputActionAsset projectActions = InputSystem.actions;
            if (projectActions != null)
            {
                moveAction = projectActions.FindAction("Player/Move");
                lookAction = projectActions.FindAction("Player/Look");
                interactAction = projectActions.FindAction("Player/Interact");
            }

            if (moveAction == null || lookAction == null || interactAction == null)
            {
                Debug.LogWarning("[DesktopInputAdapter] Project-wide Player/Move, Look or Interact not found; using built-in desktop bindings.");
                ownsPlayerActions = true;

                moveAction = new InputAction("Move", InputActionType.Value);
                moveAction.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w")
                    .With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a")
                    .With("Right", "<Keyboard>/d");
                lookAction = new InputAction("Look", InputActionType.Value, "<Pointer>/delta");
                interactAction = new InputAction("Interact", InputActionType.Button, "<Keyboard>/e");
            }

            pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            defendAction = new InputAction("Defend", InputActionType.Button, "<Keyboard>/f");
            defendAction.AddBinding("<Mouse>/rightButton");
        }

        private void OnEnable()
        {
            moveAction?.Enable();
            lookAction?.Enable();
            interactAction?.Enable();
            pauseAction?.Enable();
            defendAction?.Enable();
        }

        private void OnDisable()
        {
            // Project-wide actions are shared, so only disable what this adapter created.
            if (ownsPlayerActions)
            {
                moveAction?.Disable();
                lookAction?.Disable();
                interactAction?.Disable();
            }
            pauseAction?.Disable();
            defendAction?.Disable();
        }

        private void OnDestroy()
        {
            if (ownsPlayerActions)
            {
                moveAction?.Dispose();
                lookAction?.Dispose();
                interactAction?.Dispose();
            }
            pauseAction?.Dispose();
            defendAction?.Dispose();
        }
    }
}
