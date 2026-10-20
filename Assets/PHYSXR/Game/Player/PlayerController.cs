using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Input;
using UnityEngine;

namespace PHYSXR.Game.Player
{
    // The player body. Each frame it polls the active IPlayerInput and routes
    // intents: pause to GameFlowController, look/move to DesktopMotor and
    // interact to PlayerInteractor. Movement and interaction only run while
    // the flow accepts gameplay input (not paused, not succeeded).
    //
    // The input source is swappable (SetInput), which is how the scripted
    // route self-test drives the real motor and interactor, and how a Quest
    // XRInputAdapter will plug in later.
    [RequireComponent(typeof(DesktopMotor), typeof(PlayerInteractor))]
    public class PlayerController : MonoBehaviour
    {
        private DesktopMotor motor;
        private PlayerInteractor interactor;
        private GameFlowController flowController;
        private IPlayerInput input;

        public Transform Head { get; private set; }

        public DesktopMotor Motor => motor;

        public PlayerInteractor Interactor => interactor;

        public IPlayerInput Input => input;

        public void Initialize(Transform head, IPlayerInput input, GameFlowController flowController)
        {
            Head = head;
            this.input = input;
            this.flowController = flowController;

            motor = GetComponent<DesktopMotor>();
            interactor = GetComponent<PlayerInteractor>();
            motor.Initialize(head);
            interactor.Initialize(head, flowController);
        }

        public void SetInput(IPlayerInput newInput)
        {
            input = newInput;
        }

        public void Respawn(Vector3 position, float yawDegrees)
        {
            motor.Teleport(position, yawDegrees);
        }

        private void Update()
        {
            if (input == null || flowController == null)
                return;

            if (input.PausePressed)
                flowController.TogglePause();

            if (!flowController.Flow.AcceptsGameplayInput)
                return;

            motor.Look(input.LookDegrees);
            motor.Move(input.Move, Time.deltaTime);

            if (input.InteractPressed)
                interactor.TryInteract();
        }
    }
}
