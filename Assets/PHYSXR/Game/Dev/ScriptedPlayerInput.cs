using PHYSXR.Game.Input;
using UnityEngine;

namespace PHYSXR.Game.Dev
{
    // IPlayerInput driven by code instead of a device. Used by the route
    // self-test to walk the real CharacterController and press the real
    // interact/pause path. Presses are scheduled for the NEXT frame because
    // coroutines run after Update, where PlayerController has already polled.
    public class ScriptedPlayerInput : IPlayerInput
    {
        private int interactFrame = -1;
        private int pauseFrame = -1;
        private int defendFrame = -1;
        private int jumpFrame = -1;

        public Vector2 Move { get; set; }

        public Vector2 LookDegrees { get; set; }

        public bool InteractPressed => Time.frameCount == interactFrame;

        public bool PausePressed => Time.frameCount == pauseFrame;

        public bool DefendPressed => Time.frameCount == defendFrame;

        public bool SprintHeld { get; set; }

        public bool JumpPressed => Time.frameCount == jumpFrame;

        public void PressInteractNextFrame()
        {
            interactFrame = Time.frameCount + 1;
        }

        public void PressPauseNextFrame()
        {
            pauseFrame = Time.frameCount + 1;
        }

        public void PressDefendNextFrame()
        {
            defendFrame = Time.frameCount + 1;
        }

        public void PressJumpNextFrame()
        {
            jumpFrame = Time.frameCount + 1;
        }

        public void Stop()
        {
            Move = Vector2.zero;
            LookDegrees = Vector2.zero;
            SprintHeld = false;
        }
    }
}
