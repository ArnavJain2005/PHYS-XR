using System;
using System.Globalization;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Flow;
using PHYSXR.Game.Player;
using PHYSXR.Ghost;
using PHYSXR.Telemetry;
using PHYSXR.WorldStateSystem;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PHYSXR.Game.Gameplay
{
    // Unity-side owner of one attempt in Game_BlackthornHouse. It wraps the
    // plain C# GameFlow (objective and run states) and composes the ghost
    // pipeline exactly as the architecture describes:
    //
    //   each frame, while Playing:
    //     sensing (SensorService) -> WorldStateManager.UpdateSourceStatus /
    //                                NotifyAcceptedEvent      (P2 gating)
    //     EncounterDirector       -> PlayerState, VirtualState
    //     GhostMotionController   -> GhostState (phase, cooldown)
    //     GhostRuntime.Tick()     -> EncounterDecisionLayer(R1-R8) -> GhostAction
    //                                -> GhostExecutionState -> controller
    //     GhostMotionController.Tick() turns that state into movement, pose, sound
    //
    // It also applies the Unity consequences of flow changes: power, time
    // scale and audio pause, cursor lock, checkpoint save/restore, death and
    // the ending, scene reloads for restarts, and the OBJECTIVE / FLOW /
    // RUN_END telemetry. Interactables and menus call the public methods;
    // nothing here reads a device.
    public class GameFlowController : MonoBehaviour
    {
        public const string GameSceneName = "Game_BlackthornHouse";

        // Checkpoint: the start of the main encounter, in the utility room by
        // the fuse box, facing the dining-room door (spec section C).
        public static readonly Vector3 CheckpointPosition = new Vector3(5.6f, 0.05f, 11.0f);
        public const float CheckpointYaw = 205f;
        public const long EventBlackoutMs = 1000L;

        // First sighting: The Hollow counts as seen when its chest is inside
        // the player's view, within this distance, with no wall in between.
        public const float SightingMaxDistance = 12f;
        public const float SightingViewportMargin = 0.05f;

        private PowerSystem powerSystem;
        private GameServices services;
        private bool restarting;
        private bool firstSightingDone;
        private bool sightingSeen;
        private Camera playerView;
        private ReadableNote openNote;
        private bool encounterHintShown;
        private bool runEndLogged;
        private float clockRemainder;
        private float lastPlayerSpeed;
        private Vector3 lastPlayerPosition;

        private readonly PlayerState playerState = new PlayerState();
        private readonly VirtualState virtualState = new VirtualState();
        private readonly EncounterDirector director = new EncounterDirector();

        private PlayerController player;
        private GhostRuntime ghostRuntime;
        private GhostMotionController ghost;
        private DefensiveAbility lantern;
        private GameAudio audioPlayer;
        private FuseBox fuseBox;
        private FusePickup fuse;
        private GhostAnchors anchors;
        private Vector3 startPosition;
        private float startYaw;

        public GameFlow Flow { get; private set; } = new GameFlow();

        public GameSession Session => services?.Session;

        public GameServices Services => services;

        public WorldStateManager World { get; private set; }

        public GhostRuntime GhostRuntime => ghostRuntime;

        public GhostMotionController Ghost => ghost;

        public DefensiveAbility Lantern => lantern;

        public EncounterDirector Director => director;

        public PlayerState LastPlayerState => playerState;

        public VirtualState LastVirtualState => virtualState;

        // Milliseconds of unpaused play this attempt: the decision clock (P1).
        public long GameClockMs { get; private set; }

        // True once Start() has begun this scene's attempt on the session.
        public bool RunStarted { get; private set; }

        public bool StartedFromCheckpoint { get; private set; }

        // Seconds of unpaused play since the attempt started; frozen at the end.
        public float RunSeconds { get; private set; }

        public float TotalRunSeconds => (services?.Session?.SecondsBeforeAttempt ?? 0f) + RunSeconds;

        public string LastMessage { get; private set; } = string.Empty;

        public float LastMessageTime { get; private set; } = -100f;

        // The readable (note or radio) on screen, if any. While one is open
        // the player is in a modal: look, move, sprint, jump, the lantern
        // and world interaction are blocked, and E closes it.
        public ReadableNote OpenNote => openNote;

        public bool ReadableOpen => openNote != null;

        // True once the player has actually seen The Hollow this attempt.
        public bool SightingSeen => sightingSeen;

        public event Action<string> MessageRaised;
        public event Action<string> HintRaised;
        public event Action<ReadableNote> NoteOpened;
        public event Action<ReadableNote> NoteClosed;
        public event Action StrikeFeinted;
        public event Action DeathStarted;
        public event Action EndingStarted;
        public event Action<ObjectiveStage> ObjectiveChanged;

        public void Initialize(PowerSystem powerSystem)
        {
            this.powerSystem = powerSystem;
            powerSystem.PowerRestored += OnPowerRestored;
            powerSystem.SurgeStarted += OnSurgeStarted;
            powerSystem.ApplyUnpowered();
        }

        // The rest of the scene's parts, wired by BlackthornGreybox.
        public void Compose(PlayerController player, GhostRuntime ghostRuntime, GhostMotionController ghost,
            DefensiveAbility lantern, GameAudio audioPlayer, FusePickup fuse, FuseBox fuseBox, GhostAnchors anchors,
            Vector3 startPosition, float startYaw)
        {
            this.player = player;
            this.ghostRuntime = ghostRuntime;
            this.ghost = ghost;
            this.lantern = lantern;
            this.audioPlayer = audioPlayer;
            this.fuse = fuse;
            this.fuseBox = fuseBox;
            this.anchors = anchors;
            this.startPosition = startPosition;
            this.startYaw = startYaw;
            playerView = player != null && player.Head != null ? player.Head.GetComponent<Camera>() : null;
        }

        // Called by AppBootstrap when the scene loads (before Start). If the
        // game scene is played directly, Start() uses GameServices.Local().
        public void AttachServices(GameServices gameServices)
        {
            services = gameServices;
        }

        // Kept for callers that only have a session.
        public void AttachSession(GameSession gameSession)
        {
            services = GameServices.Local();
            services.Session = gameSession;
        }

        private TelemetryChannel Telemetry => services?.Telemetry ?? TelemetryChannel.Null;

        private void Awake()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        private void Start()
        {
            if (services == null)
            {
                Debug.LogWarning("[GameFlowController] No Bootstrap services attached (scene played directly); using local services with no sensors.");
                services = GameServices.Local();
            }

            GameSession session = services.Session;
            CheckpointData checkpoint = default;
            if (session.NextStart == GameSession.StartKind.Checkpoint && session.HasCheckpoint)
            {
                checkpoint = session.BeginCheckpointAttempt();
                StartedFromCheckpoint = true;
                Flow = new GameFlow(checkpoint.stage);
            }
            else
            {
                session.BeginNewRun();
                Flow = new GameFlow();
            }

            Flow.RunStateChanged += OnRunStateChanged;
            Flow.StageChanged += stage => ObjectiveChanged?.Invoke(stage);

            World = services.Sensors.CreateWorldStateManager(() => GameClockMs);
            World.PhysicalInputMasked = services.Sensors.PhysicalInputMasked;
            if (ghostRuntime != null)
            {
                ghostRuntime.Initialize(World, () => GameClockMs, Telemetry.Sink);
                ghost.Initialize(ghostRuntime, player.transform, anchors, new HollowAssets(), Telemetry,
                    () => Flow.EncounterCommitted, () => Flow.PowerRestored);
                ghost.StrikeHit += OnStrikeHit;
                ghost.WarnStarted += position => powerSystem.PulseNearest(position);
                ghost.ProtectChanged += steady => powerSystem.SetSteady(steady);
            }

            if (StartedFromCheckpoint)
                RestoreCheckpoint(checkpoint);
            else
                StartFresh();

            RunStarted = true;
            ApplyCursor(Flow.RunState);
            lastPlayerPosition = player != null ? player.transform.position : Vector3.zero;
            Debug.Log("[GameFlow] RUN_START run=" + session.RunNumber + " attempt=" + session.Attempt
                + " condition=" + services.Sensors.Condition + (StartedFromCheckpoint ? " from=checkpoint" : string.Empty));
            Telemetry.Emit("FLOW", "event=" + (StartedFromCheckpoint ? "CHECKPOINT_RESTORE" : "RUN_START")
                + " run=" + session.RunNumber + " attempt=" + session.Attempt + " condition=" + services.Sensors.Condition);
        }

        private void StartFresh()
        {
            powerSystem.ApplyUnpowered();
            player?.Respawn(startPosition, startYaw);
            if (ghost != null)
                ghost.SetDormant(anchors.Spawn);
        }

        private void RestoreCheckpoint(CheckpointData checkpoint)
        {
            powerSystem.ApplyPowered();
            fuseBox?.ShowInstalled();
            if (fuse != null)
                fuse.gameObject.SetActive(false);
            player?.Respawn(new Vector3(checkpoint.x, checkpoint.y, checkpoint.z), checkpoint.yaw);
            firstSightingDone = true;
            // The player met The Hollow before the checkpoint; the restart
            // goes straight to the encounter without a second sighting lunge.
            sightingSeen = true;
            if (ghost != null)
                ghost.Manifest(anchors.CheckpointSpawn, false, false);
            director.BeginRestartGrace(GameClockMs);
            services.Sensors.BeginEventBlackout(EventBlackoutMs);
            Raise("You wake by the fuse box. The house is lit. The door is still ahead.");
        }

        private void Update()
        {
            BridgeSensing();

            if (Flow.RunState != RunState.Playing)
                return;

            float dt = Time.deltaTime;
            RunSeconds += dt;
            float ms = dt * 1000f + clockRemainder;
            long step = (long)ms;
            clockRemainder = ms - step;
            GameClockMs += step;

            if (ghostRuntime == null || !ghostRuntime.IsInitialized)
                return;

            Vector3 p = player.transform.position;
            Vector3 delta = p - lastPlayerPosition;
            delta.y = 0f;
            lastPlayerSpeed = dt > 0f ? delta.magnitude / dt : 0f;
            lastPlayerPosition = p;

            director.Update(GameClockMs, ghost.DistanceToPlayer, lastPlayerSpeed, Flow.Stage >= ObjectiveStage.InstallFuse,
                Flow.EncounterCommitted, playerState, virtualState);
            World.UpdatePlayerState(playerState);
            World.UpdateVirtualState(virtualState);
            World.UpdateGhostState(ghost.Report(ghostRuntime.LastAction ?? GhostAction.FOLLOW));

            ghostRuntime.Tick();
            ghost.Tick(dt);
            UpdateFirstSighting();
            // Never leave the player stuck reading while an attack winds up.
            if (openNote != null && ghost.Phase == GhostPhase.WINDUP)
                CloseNote();

            if (Flow.EncounterCommitted && !encounterHintShown && ghost.StraightDistanceToPlayer < 7f)
            {
                encounterHintShown = true;
                Hint("Press F to raise the lantern when it comes close.");
            }
        }

        // The first time the player actually sees The Hollow it lunges. This
        // only raises VirtualState.firstSightingAttack (through the director);
        // the decision layer turns it into ATTACK via rule E5, under the same
        // grace, cooldown and terminal checks as every other attack, and the
        // ghost's normal WINDUP/STRIKE/RECOVER phases (and the lantern
        // interrupt) play it out. Sensors are not involved. Once per attempt.
        private void UpdateFirstSighting()
        {
            if (sightingSeen)
            {
                // The request is spent once the attack phases start.
                if (director.FirstSightingAttackPending(GameClockMs) && ghost.Phase != GhostPhase.NORMAL)
                    director.ConsumeFirstSightingAttack();
                return;
            }

            if (!firstSightingDone || ghost.Dormant || Flow.EncounterCommitted || !PlayerCanSeeGhost())
                return;

            sightingSeen = true;
            if (!director.BeginFirstSightingAttack(GameClockMs))
                return;
            Telemetry.Emit("FLOW", string.Format(CultureInfo.InvariantCulture,
                "event=SIGHTING distance={0:F2}", ghost.StraightDistanceToPlayer));
            Hint("Press F to raise the lantern when it comes close.");
            encounterHintShown = true;
        }

        public bool PlayerCanSeeGhost()
        {
            if (ghost == null || ghost.Dormant || ghost.Visual == null || ghost.Visual.Visibility < 0.5f || player == null)
                return false;

            Transform eye = playerView != null ? playerView.transform : player.Head;
            if (eye == null)
                return false;

            Vector3 chest = ghost.ChestPosition;
            Vector3 toChest = chest - eye.position;
            if (toChest.magnitude > SightingMaxDistance)
                return false;

            if (playerView != null)
            {
                Vector3 vp = playerView.WorldToViewportPoint(chest);
                float m = SightingViewportMargin;
                if (vp.z <= 0f || vp.x < m || vp.x > 1f - m || vp.y < m || vp.y > 1f - m)
                    return false;
            }
            else if (Vector3.Angle(eye.forward, toChest) > 35f)
            {
                return false;
            }

            // Walls block the view. The ghost's own body is on Ignore Raycast,
            // which DefaultRaycastLayers already leaves out.
            if (Physics.Linecast(eye.position, chest, out RaycastHit block, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !block.collider.transform.IsChildOf(ghost.transform) && !block.collider.transform.IsChildOf(player.transform))
                return false;

            return true;
        }

        // Sensing reaches the WorldStateManager every frame, paused or not, so
        // freshness is never stale when play resumes. Events are different: the
        // pulse runs on the game clock, which stops while paused, so an event
        // that arrives then is dropped (and logged) rather than played on resume.
        private void BridgeSensing()
        {
            if (World == null || services == null)
                return;

            IPhysicalInputSource sensors = services.Sensors;
            World.UpdateSourceStatus(sensors.Status);
            World.PhysicalInputMasked = sensors.PhysicalInputMasked;
            World.EventsSuspended = Flow.RunState != RunState.Playing;
            var events = sensors.FrameAcceptedEvents;
            for (int i = 0; i < events.Count; i++)
            {
                if (!World.NotifyAcceptedEvent(events[i]) && World.EventsSuspended)
                    Telemetry.Emit("PHYS_EVENT_DROPPED", string.Format(CultureInfo.InvariantCulture,
                        "evt={0} boot={1} reason=NOT_PLAYING run_state={2}", events[i].eventId, events[i].boot, Flow.RunState),
                        relatedId: events[i].eventId);
            }
        }

        private void OnDestroy()
        {
            if (powerSystem != null)
            {
                powerSystem.PowerRestored -= OnPowerRestored;
                powerSystem.SurgeStarted -= OnSurgeStarted;
            }
            if (ghost != null)
                ghost.StrikeHit -= OnStrikeHit;
            AudioListener.pause = false;
        }

        // ------------------------------------------------------------ objective

        public InteractionResult CollectFuse()
        {
            InteractionResult result = Report(Flow.TryCollectFuse(), "FUSE_PICKUP");
            if (result.Accepted)
            {
                director.BeginBeat(GameClockMs, "FUSE_PICKUP");
                EnsureFirstSighting(anchors != null ? anchors.FirstSighting : Vector3.zero, false);
            }
            return result;
        }

        public InteractionResult InstallFuse()
        {
            InteractionResult result = Report(Flow.TryInstallFuse(), "FUSE_INSTALLED");
            if (result.Accepted)
                powerSystem.RestorePower();
            return result;
        }

        public InteractionResult UseExit()
        {
            return Report(Flow.TryUseExit(), "EXIT_USED");
        }

        // Scripted beat: the first time the player is shown The Hollow.
        public void TriggerFirstSighting(Vector3 anchor)
        {
            EnsureFirstSighting(anchor, true);
        }

        private void EnsureFirstSighting(Vector3 anchor, bool fromTrigger)
        {
            if (firstSightingDone || ghost == null || Flow.RunState != RunState.Playing)
                return;
            // The scripted M2 route runs with the encounter off; the ghost
            // stays dormant there so it can neither block nor lunge.
            if (services != null && services.EncounterDisabled)
                return;

            firstSightingDone = true;
            ghost.Manifest(anchor, false, true);
            director.BeginBeat(GameClockMs, "FIRST_SIGHTING");
            audioPlayer?.PlayAt(AudioCue.GhostThud, anchor + Vector3.up, 0.8f);
            Telemetry.Emit("FLOW", "event=BEAT id=FIRST_SIGHTING source=" + (fromTrigger ? "trigger" : "fuse"));
        }

        // The encounter trigger was crossed. Commits only after power.
        public void TriggerEncounter(string triggerId)
        {
            if (services != null && services.EncounterDisabled)
                return;
            if (!Flow.CommitEncounter())
                return;

            Report(InteractionResult.Accept(string.Empty), "ENCOUNTER_COMMITTED");
            Telemetry.Emit("OBJECTIVE", "event=ENCOUNTER_COMMITTED trigger=" + triggerId);
            ObjectiveChanged?.Invoke(Flow.Stage);
            if (ghost != null && anchors != null)
                ghost.Manifest(anchors.EncounterManifest, true, true);
            powerSystem.PulseNearest(anchors != null ? anchors.EncounterManifest : Vector3.zero);
            audioPlayer?.Play2D(AudioCue.GhostManifest, 0.6f);
        }

        public void ShowNote(ReadableNote note, bool firstTime)
        {
            if (note == null || Flow.RunState != RunState.Playing)
                return;
            if (openNote == note)
            {
                CloseNote();
                return;
            }
            if (openNote != null)
                CloseNote();

            openNote = note;
            audioPlayer?.Play2D(AudioCue.NoteRustle);
            if (firstTime)
                Telemetry.Emit("CLUE", "id=" + note.name);
            NoteOpened?.Invoke(note);
        }

        // Closes the open readable (E, Esc, pause, death, the ending, or an
        // attack wind-up). Safe to call when nothing is open.
        public void CloseNote()
        {
            if (openNote == null)
                return;
            ReadableNote closed = openNote;
            openNote = null;
            NoteClosed?.Invoke(closed);
        }

        // Shows a refusal or remark from scenery (locked doors).
        public InteractionResult Message(InteractionResult result)
        {
            return Report(result, null);
        }

        public void Hint(string text)
        {
            HintRaised?.Invoke(text);
        }

        // ------------------------------------------------------------ run flow

        public bool Pause() => Flow.Pause();

        public bool Resume() => Flow.Resume();

        public bool TogglePause() => Flow.TogglePause();

        // Full restart from the beginning: reload the game scene, which
        // rebuilds the house, player, ghost, objective and lighting.
        public void RestartRun()
        {
            if (restarting)
                return;

            restarting = true;
            Debug.Log("[GameFlow] RUN_RESTART from stage=" + Flow.Stage + " state=" + Flow.RunState);
            Telemetry.Emit("FLOW", "event=RUN_RESTART stage=" + Flow.Stage + " state=" + Flow.RunState);
            LogRunEnd("restart");
            services.Session.RequestNewRun();
            Reload();
        }

        // Reload at the checkpoint (after death, or from the pause menu once
        // a checkpoint exists). Falls back to a full restart without one.
        public void RestartFromCheckpoint()
        {
            if (restarting)
                return;

            if (!services.Session.RequestCheckpointRestart(RunSeconds))
            {
                RestartRun();
                return;
            }

            restarting = true;
            Debug.Log("[GameFlow] CHECKPOINT_RESTART attempt=" + (services.Session.Attempt + 1));
            Reload();
        }

        public void ReturnToMenu()
        {
            if (restarting)
                return;
            restarting = true;
            LogRunEnd("abandon");
            Time.timeScale = 1f;
            AudioListener.pause = false;
            if (services.ReturnToMenu != null)
                services.ReturnToMenu();
            else
                SceneManager.LoadScene(GameSceneName);
        }

        public void QuitGame()
        {
            Debug.Log("[GameFlow] QUIT");
            LogRunEnd(Flow.IsSucceeded ? "success" : "abandon");
            if (services?.Quit != null)
            {
                services.Quit();
                return;
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Reload()
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            SceneManager.LoadScene(GameSceneName);
        }

        // ------------------------------------------------------------ events

        private void OnSurgeStarted()
        {
            director.BeginSurge(GameClockMs);
        }

        private void OnPowerRestored()
        {
            InteractionResult result = Report(Flow.ConfirmPowerRestored(), "POWER_RESTORED");
            if (!result.Accepted)
                return;

            services.Session.SaveCheckpoint(new CheckpointData
            {
                stage = ObjectiveStage.ReachExit,
                x = CheckpointPosition.x, y = CheckpointPosition.y, z = CheckpointPosition.z, yaw = CheckpointYaw
            });
            Telemetry.Emit("FLOW", "event=CHECKPOINT_SAVE stage=ReachExit");
            audioPlayer?.Play2D(AudioCue.ObjectiveUpdate);
        }

        private void OnStrikeHit()
        {
            // Before the encounter is committed (the first-sighting lunge) a
            // strike that lands is a feint: it knocks the breath out of the
            // player but does not kill. The GDD's early ghost is an ambiguous
            // threat; death belongs to the committed encounter (Technical Spec
            // section D, E3).
            if (!Flow.EncounterCommitted)
            {
                if (Flow.RunState != RunState.Playing)
                    return;
                Telemetry.Emit("ATTACK", string.Format(CultureInfo.InvariantCulture,
                    "event=STRIKE_FEINT distance={0:F2}", ghost.StraightDistanceToPlayer));
                audioPlayer?.Play2D(AudioCue.DeathSting, 0.5f);
                StrikeFeinted?.Invoke();
                return;
            }

            InteractionResult death = Flow.ReportDeath();
            if (!death.Accepted)
                return;

            services.Session.RecordDeath();
            ghostRuntime.Session.EnterTerminal(TerminalReason.PlayerDied);
            ghost.Halt(false);
            Telemetry.Emit("FLOW", string.Format(CultureInfo.InvariantCulture,
                "event=DEATH attempt={0} deaths={1} t={2:F1}", services.Session.Attempt, services.Session.Deaths, RunSeconds));
            Debug.Log("[GameFlow] DEATH attempt=" + services.Session.Attempt);
            audioPlayer?.Play2D(AudioCue.DeathSting);
            Raise(death.Message);
            DeathStarted?.Invoke();
        }

        private void OnRunStateChanged(RunState state)
        {
            Time.timeScale = state == RunState.Paused ? 0f : 1f;
            AudioListener.pause = state == RunState.Paused;
            ApplyCursor(state);
            if (state != RunState.Playing)
                CloseNote();

            switch (state)
            {
                case RunState.Succeeded:
                    if (ghostRuntime != null && ghostRuntime.IsInitialized)
                        ghostRuntime.Session.EnterTerminal(TerminalReason.EndingPlaying);
                    ghost?.Halt(true);
                    Debug.Log("[GameFlow] RUN_END outcome=success duration_s=" + TotalRunSeconds.ToString("F1"));
                    LogRunEnd("success");
                    EndingStarted?.Invoke();
                    break;
                case RunState.Dead:
                    break;
                default:
                    string word = state == RunState.Paused ? "PAUSE" : "RESUME";
                    Debug.Log("[GameFlow] " + word);
                    Telemetry.Emit("FLOW", "event=" + word);
                    break;
            }
        }

        private void LogRunEnd(string outcome)
        {
            if (runEndLogged || services == null)
                return;
            runEndLogged = true;
            Telemetry.Emit("RUN_END", string.Format(CultureInfo.InvariantCulture,
                "outcome={0} duration_s={1:F1} deaths={2} attempt={3} lantern_uses={4} lantern_hits={5}",
                outcome, TotalRunSeconds, services.Session.Deaths, services.Session.Attempt,
                lantern != null ? lantern.Uses : 0, lantern != null ? lantern.Hits : 0));
        }

        private InteractionResult Report(InteractionResult result, string objectiveRecord)
        {
            if (result.Accepted && objectiveRecord != null)
            {
                Debug.Log("[GameFlow] OBJECTIVE " + objectiveRecord + " stage=" + Flow.Stage + " t=" + RunSeconds.ToString("F1"));
                Telemetry.Emit("OBJECTIVE", "event=" + objectiveRecord + " stage=" + Flow.Stage + " t=" + RunSeconds.ToString("F1", CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(result.Message))
                Raise(result.Message);
            return result;
        }

        private void Raise(string message)
        {
            LastMessage = message;
            LastMessageTime = Time.unscaledTime;
            MessageRaised?.Invoke(message);
        }

        private static void ApplyCursor(RunState state)
        {
            bool playing = state == RunState.Playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
        }
    }
}
