using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PHYSXR.Game.Input
{
    // Windows keyboard/mouse adapter (GDD 7.1): WASD move, mouse look,
    // E interact, F lantern pulse (the designated defensive key), Left Shift
    // sprint (hold), Space jump, Esc pause/resume. Mouse sensitivity and
    // invert-Y come from GameSettings.
    //
    // Move, Look, Interact, Sprint and Jump come from the project-wide actions
    // asset (Assets/InputSystem_Actions.inputactions, "Player" map: Sprint is
    // Left Shift / left-stick press, Jump is Space / gamepad south), which is
    // left unmodified. That asset has no pause action, so Pause is a local
    // action bound to Escape. If the project-wide asset or one of its actions
    // is missing, equivalent local bindings are created so the game is still
    // playable.
    //
    // Meta Quest Touch controllers (OpenXR Oculus Touch profile, e.g. over
    // Quest Link) drive the same intents alongside the keyboard: left stick
    // move, left stick click sprint (hold), right stick smooth turn (yaw only;
    // the headset owns pitch), right trigger or A interact, left trigger
    // lantern pulse, B jump, left menu button pause/resume. The asset's
    // generic XR-group bindings (either stick moves, either trigger sprints)
    // would clash with that, so they are disabled with runtime binding
    // overrides, which are never written to the asset and are removed on
    // destroy.
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
        private InputAction sprintAction;
        private InputAction jumpAction;
        private bool ownsPlayerActions;
        private bool ownsSprintJump;

        // Quest Touch actions, always owned by this adapter.
        private InputAction xrMoveAction;
        private InputAction xrTurnAction;
        private InputAction xrInteractAction;
        private InputAction xrSprintAction;
        private InputAction xrJumpAction;
        private readonly List<(InputAction action, int bindingIndex)> disabledXRBindings = new List<(InputAction, int)>();

        public Vector2 Move
        {
            get
            {
                Vector2 move = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
                return Vector2.ClampMagnitude(move + xrMoveAction.ReadValue<Vector2>(), 1f);
            }
        }

        public Vector2 LookDegrees
        {
            get
            {
                Vector2 degrees = Vector2.zero;
                if (lookAction != null)
                {
                    Vector2 raw = lookAction.ReadValue<Vector2>();
                    InputControl control = lookAction.activeControl;
                    if (control != null && control.device is Pointer)
                        degrees = raw * mouseDegreesPerPixel;
                    else
                        // Sticks report deflection, not a per-frame delta.
                        degrees = raw * (stickDegreesPerSecond * Time.unscaledDeltaTime);

                    if (InvertY)
                        degrees.y = -degrees.y;
                }

                // Right Touch stick turns the body only; its y is ignored.
                degrees.x += xrTurnAction.ReadValue<Vector2>().x * (stickDegreesPerSecond * Time.unscaledDeltaTime);
                return degrees;
            }
        }

        public bool InteractPressed =>
            (interactAction != null && interactAction.WasPressedThisFrame()) || xrInteractAction.WasPressedThisFrame();

        public bool PausePressed => pauseAction != null && pauseAction.WasPressedThisFrame();

        public bool DefendPressed => defendAction != null && defendAction.WasPressedThisFrame();

        public bool SprintHeld => (sprintAction != null && sprintAction.IsPressed()) || xrSprintAction.IsPressed();

        public bool JumpPressed =>
            (jumpAction != null && jumpAction.WasPressedThisFrame()) || xrJumpAction.WasPressedThisFrame();

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
                sprintAction = projectActions.FindAction("Player/Sprint");
                jumpAction = projectActions.FindAction("Player/Jump");
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

            if (sprintAction == null || jumpAction == null)
            {
                Debug.LogWarning("[DesktopInputAdapter] Project-wide Player/Sprint or Jump not found; using built-in Left Shift / Space.");
                ownsSprintJump = true;
                sprintAction = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
                jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            }

            pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pauseAction.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            defendAction = new InputAction("Defend", InputActionType.Button, "<Keyboard>/f");
            defendAction.AddBinding("<Mouse>/rightButton");
            defendAction.AddBinding("<XRController>{LeftHand}/{TriggerButton}");

            xrMoveAction = new InputAction("XRMove", InputActionType.Value, "<XRController>{LeftHand}/{Primary2DAxis}");
            xrTurnAction = new InputAction("XRTurn", InputActionType.Value, "<XRController>{RightHand}/{Primary2DAxis}");
            xrInteractAction = new InputAction("XRInteract", InputActionType.Button, "<XRController>{RightHand}/{TriggerButton}");
            xrInteractAction.AddBinding("<XRController>{RightHand}/{PrimaryButton}");
            xrSprintAction = new InputAction("XRSprint", InputActionType.Button, "<XRController>{LeftHand}/{Primary2DAxisClick}");
            xrJumpAction = new InputAction("XRJump", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}");

            if (!ownsPlayerActions)
            {
                DisableXRBindings(moveAction);
                DisableXRBindings(lookAction);
                DisableXRBindings(interactAction);
            }
            if (!ownsSprintJump)
            {
                DisableXRBindings(sprintAction);
                DisableXRBindings(jumpAction);
            }
        }

        // An empty override path disables a binding for this session only.
        private void DisableXRBindings(InputAction action)
        {
            for (int i = 0; i < action.bindings.Count; i++)
            {
                if (!action.bindings[i].path.StartsWith("<XRController>"))
                    continue;
                action.ApplyBindingOverride(i, string.Empty);
                disabledXRBindings.Add((action, i));
            }
        }

        private void OnEnable()
        {
            moveAction?.Enable();
            lookAction?.Enable();
            interactAction?.Enable();
            sprintAction?.Enable();
            jumpAction?.Enable();
            pauseAction?.Enable();
            defendAction?.Enable();
            xrMoveAction.Enable();
            xrTurnAction.Enable();
            xrInteractAction.Enable();
            xrSprintAction.Enable();
            xrJumpAction.Enable();
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
            if (ownsSprintJump)
            {
                sprintAction?.Disable();
                jumpAction?.Disable();
            }
            pauseAction?.Disable();
            defendAction?.Disable();
            xrMoveAction.Disable();
            xrTurnAction.Disable();
            xrInteractAction.Disable();
            xrSprintAction.Disable();
            xrJumpAction.Disable();
        }

        private void OnDestroy()
        {
            foreach ((InputAction action, int bindingIndex) in disabledXRBindings)
                action.RemoveBindingOverride(bindingIndex);
            disabledXRBindings.Clear();

            if (ownsPlayerActions)
            {
                moveAction?.Dispose();
                lookAction?.Dispose();
                interactAction?.Dispose();
            }
            if (ownsSprintJump)
            {
                sprintAction?.Dispose();
                jumpAction?.Dispose();
            }
            pauseAction?.Dispose();
            defendAction?.Dispose();
            xrMoveAction.Dispose();
            xrTurnAction.Dispose();
            xrInteractAction.Dispose();
            xrSprintAction.Dispose();
            xrJumpAction.Dispose();
        }
    }
}
