using UnityEngine;

namespace PHYSXR.Game.Input
{
    // Device-independent player intents, polled once per frame by
    // PlayerController. Desktop (DesktopInputAdapter), a future Quest
    // XRInputAdapter and the scripted route self-test all implement this, so
    // gameplay code never reads a keyboard, mouse or controller directly.
    public interface IPlayerInput
    {
        // Planar movement intent: x = strafe right, y = forward. Magnitude <= 1.
        Vector2 Move { get; }

        // Look change for this frame in degrees: x = yaw (right), y = pitch (up).
        Vector2 LookDegrees { get; }

        // True only on the frame the interact intent starts.
        bool InteractPressed { get; }

        // True only on the frame the pause/resume intent starts.
        bool PausePressed { get; }
    }
}
