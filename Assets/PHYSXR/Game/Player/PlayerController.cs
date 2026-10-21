using PHYSXR.Game.Audio;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Input;
using UnityEngine;

namespace PHYSXR.Game.Player
{
    // The player body. Each frame it polls the active IPlayerInput and routes
    // intents: pause to GameFlowController, look/move to DesktopMotor,
    // interact to PlayerInteractor and defend to the lantern. Movement and
    // interaction only run while the flow accepts gameplay input (not
    // paused, dead or finished). It also plays footsteps (wood or tile, from
    // the floor under the player) and applies a small, optional head bob.
    //
    // The input source is swappable (SetInput), which is how the scripted
    // self-tests drive the real motor and interactor, and how a Quest
    // XRInputAdapter will plug in later.
    [RequireComponent(typeof(DesktopMotor), typeof(PlayerInteractor))]
    public class PlayerController : MonoBehaviour
    {
        private const float StrideMetres = 0.72f;
        private const float EyeHeight = 1.65f;

        private DesktopMotor motor;
        private PlayerInteractor interactor;
        private GameFlowController flowController;
        private IPlayerInput input;
        private DefensiveAbility lantern;
        private GameAudio audioPlayer;
        private Vector3 lastPosition;
        private float strideAccumulator;
        private float bobPhase;
        private float bobWeight;
        private bool leftFoot;
        private readonly RaycastHit[] floorHits = new RaycastHit[4];

        public Transform Head { get; private set; }

        public DesktopMotor Motor => motor;

        public PlayerInteractor Interactor => interactor;

        public IPlayerInput Input => input;

        public bool HeadBob { get; set; } = true;

        public void Initialize(Transform head, IPlayerInput input, GameFlowController flowController)
        {
            Head = head;
            this.input = input;
            this.flowController = flowController;

            motor = GetComponent<DesktopMotor>();
            interactor = GetComponent<PlayerInteractor>();
            motor.Initialize(head);
            interactor.Initialize(head, flowController);
            lastPosition = transform.position;
        }

        public void SetAbilities(DefensiveAbility lantern, GameAudio audioPlayer)
        {
            this.lantern = lantern;
            this.audioPlayer = audioPlayer;
        }

        public void SetInput(IPlayerInput newInput)
        {
            input = newInput;
        }

        public void Respawn(Vector3 position, float yawDegrees)
        {
            motor.Teleport(position, yawDegrees);
            lastPosition = transform.position;
            strideAccumulator = 0f;
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

            if (input.DefendPressed && lantern != null)
                lantern.TryPulse();

            UpdateFootsteps();
        }

        private void UpdateFootsteps()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - lastPosition;
            delta.y = 0f;
            lastPosition = position;
            float dt = Time.deltaTime;
            float speed = dt > 0f ? delta.magnitude / dt : 0f;

            bobWeight = Mathf.MoveTowards(bobWeight, speed > 0.3f ? 1f : 0f, dt * 4f);
            bobPhase += delta.magnitude / StrideMetres * Mathf.PI;
            if (Head != null)
            {
                float amount = HeadBob ? 1f : 0f;
                float y = EyeHeight + Mathf.Abs(Mathf.Sin(bobPhase)) * 0.028f * bobWeight * amount - 0.014f * bobWeight * amount;
                Vector3 local = Head.localPosition;
                Head.localPosition = new Vector3(local.x, y, local.z);
            }

            if (speed < 0.3f)
                return;

            strideAccumulator += delta.magnitude;
            if (strideAccumulator < StrideMetres)
                return;

            strideAccumulator = 0f;
            leftFoot = !leftFoot;
            if (audioPlayer == null)
                return;

            AudioCue cue = FloorIsTile(position) ? AudioCue.FootstepTile : AudioCue.FootstepWood;
            Vector3 foot = position + transform.right * (leftFoot ? -0.12f : 0.12f) + Vector3.up * 0.05f;
            audioPlayer.PlayRandomAt(cue, foot, 1f, 0.06f);
        }

        private bool FloorIsTile(Vector3 position)
        {
            int count = Physics.RaycastNonAlloc(position + Vector3.up * 0.3f, Vector3.down, floorHits, 0.8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (floorHits[i].collider.name.EndsWith("_Tile"))
                    return true;
            return false;
        }
    }
}
