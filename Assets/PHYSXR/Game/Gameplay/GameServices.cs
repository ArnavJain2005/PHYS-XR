using System;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Settings;
using PHYSXR.Telemetry;

namespace PHYSXR.Game.Gameplay
{
    // Everything the game scene receives from the persistent app layer when it
    // loads. AppBootstrap builds one and hands it to GameFlowController; when
    // the game scene is played on its own, Local() stands in (no sensors, no
    // telemetry file, a fresh session), so the scene is always playable.
    public sealed class GameServices
    {
        public GameSession Session;
        public IPhysicalInputSource Sensors;
        public TelemetryChannel Telemetry = TelemetryChannel.Null;
        public GameSettings Settings;
        public Action ReturnToMenu;
        public Action Quit;

        // Dev/self-test only: the encounter is never committed, so a scripted
        // route can walk the whole house without being attacked.
        public bool EncounterDisabled;

        // How the decision layer turns the physical station's semantic state
        // into visible ghost behaviour (X1-X3). The app sets the default
        // profile; null (-physxr-standard-rules) keeps the specified rules.
        public PHYSXR.Decision.SensorResponseProfile SensorResponse;

        public static GameServices Local()
        {
            return new GameServices
            {
                Session = new GameSession(),
                Sensors = new NoPhysicalInput(),
                Telemetry = TelemetryChannel.Null,
                Settings = GameSettings.Load()
            };
        }
    }
}
