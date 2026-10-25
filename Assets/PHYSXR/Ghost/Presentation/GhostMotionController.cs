using System;
using System.Globalization;
using PHYSXR.Core.Data;
using PHYSXR.Core.Enums;
using PHYSXR.Telemetry;
using UnityEngine;
using UnityEngine.AI;

namespace PHYSXR.Ghost
{
    // Presentation and execution of The Hollow (Technical Spec section D,
    // "What each action does on screen"). It reads the execution state the
    // decision pipeline already chose (GhostRuntime.CurrentState) and turns it
    // into NavMesh movement, a procedural pose and sound. It never decides
    // WHICH behaviour runs; it only decides HOW the decided one looks.
    //
    // It also owns the two pieces of ghost state that are physical facts
    // rather than decisions, and reports them back through GhostState so the
    // encounter layer can see them:
    //  - the attack phases: WINDUP 1.2 s, STRIKE 0.3 s (hits if within 1.6 m
    //    and 60 degrees of the ghost's facing), RECOVER 2.0 s, then a 6 s
    //    cooldown. The WINDUP keeps its full length (it is the lantern's
    //    window) but is played as two beats: a near-still coil, creeping at
    //    0.8 m/s with one quiet intake of breath, then for its last
    //    0.36-0.54 s (varied per attack) a burst at 6.5 m/s with a hard
    //    acceleration, the lunge pose and the scream. STRIKE keeps the burst
    //    and opens with a whoosh; the impact sound plays only when the strike
    //    actually connects (the StrikeHit event), never on a miss;
    //  - the lantern interruption: TryInterrupt() sets INTERRUPTED at once
    //    (allowed in NORMAL, WINDUP and RECOVER, never during the strike):
    //    3.0 s stagger in place, 2.5 s retreat, then a cooldown of at least 6 s.
    //
    // GameFlowController calls Tick() once per frame after GhostRuntime.Tick(),
    // and only while the run is playing. Halt() freezes it for terminal states.
    //
    // The ghost has a solid body: a non-trigger capsule (radius 0.35 m, 2.0 m
    // tall) on a kinematic Rigidbody, so the player's CharacterController
    // cannot walk through it. The body sits on the Ignore Raycast layer, which
    // keeps it out of the NavMesh bake, the interaction ray, the lantern's
    // line-of-sight test and the first-sighting check (all of which use
    // Physics.DefaultRaycastLayers). It is solid only while the ghost is
    // manifested and visible: dormant, flickering or hidden, it is not there.
    [RequireComponent(typeof(NavMeshAgent))]
    public class GhostMotionController : MonoBehaviour
    {
        public const long WindupMs = 1200, StrikeMs = 300, RecoverMs = 2000, AttackCooldownMs = 6000;
        public const long StaggerMs = 3000, InterruptRetreatMs = 2500, InterruptCooldownMs = 6000;
        public const long InvestigateLingerMs = 2000;
        public const float TrailNear = 6f, TrailFar = 9f, EncounterFollow = 4f, StalkDistance = 4f;
        public const float StrikeReach = 1.6f, StrikeHalfAngle = 60f, PersonalSpace = 1.3f;
        public const float SpeedTrail = 1.4f, SpeedEncounterFollow = 2.0f, SpeedWarn = 1.9f;
        public const float SpeedInvestigate = 2.0f, SpeedRetreat = 3.0f, SpeedLunge = 3.5f;
        // A WARN for something very close to the station (rule X2) presses
        // in: closer and faster than a NEAR warning, still outside the 3.5 m
        // threat zone so it can never bring the in-game attack (E6) in reach.
        public const float StalkDistanceUrgent = 3.75f, SpeedWarnUrgent = 2.6f;
        private const string UrgentWarnRule = "X2";
        private const float EscalationCueMinInterval = 2f;
        // Bounded search parameters derived from scene scale (8 m room grid, 4 m stalk distance):
        // Prevents repetitive shaking from driving the ghost indefinitely away from the player.
        public const float MaxSearchPlayerDistance = 8.5f;
        public const float MaxSearchDisplacement = 6.5f;
        // The two beats of the windup (see above). SpeedLunge is kept for
        // callers that read it; the attack itself uses creep and burst.
        public const float SpeedCreep = 0.8f, SpeedBurst = 6.5f;
        public const float CruiseAcceleration = 12f, LungeAcceleration = 40f;
        public const float BodyRadius = 0.35f, BodyHeight = 2.0f;
        public const int BodyLayer = 2; // Ignore Raycast
        private const float ScreechMinInterval = 6f;
        // Idle turning is deliberately slower than the head (HollowVisual
        // turns the head first); the attack keeps the agent's fast turning.
        private const float IdleTurnRate = 110f;
        private const float EyeHeight = 1.6f;
        // Burst lengths, cycled per attack so no two in a row share a rhythm.
        private static readonly long[] BurstMs = { 460, 360, 540 };

        public enum InterruptResult
        {
            Interrupted,
            DuringStrike,
            AlreadyInterrupted,
            Inactive
        }

        private readonly GhostState report = new GhostState();
        private NavMeshPath path;
        private NavMeshPath searchPath;
        private float searchLastDistance = float.MaxValue;
        private readonly Vector3[] corners = new Vector3[32];

        private NavMeshAgent agent;
        private HollowVisual visual;
        private CapsuleCollider body;
        private GhostRuntime runtime;
        private Transform player;
        private GhostAnchors anchors;
        private IGhostPresentationAssets assets;
        private TelemetryChannel telemetry = TelemetryChannel.Null;
        private Func<bool> encounterCommitted = () => false;
        private Func<bool> powerRestored = () => false;

        private AudioSource breath;
        private AudioSource hum;
        private AudioSource oneShot;
        private AudioSource presence;
        private AudioSource drag;
        private AudioSource attackVoice;
        private AudioSource whisper;
        private float presenceVolume, dragVolume, whisperVolume;
        private bool attackVoiceCut;
        private readonly int[] nextVariant = new int[32];

        private GhostExecutionState? behaviour;
        private long behaviourDecision;
        private float msRemainder;
        private float distanceRefreshAt;
        private float lastScreechTime = -100f;
        private float lastEscalationCueTime = -100f;
        private bool strikeResolved;
        private bool interruptRetreatStarted;
        private Vector3 investigateTarget;
        private Vector3? searchOrigin;
        private bool arrived;
        private long arrivedAtMs;
        private long behaviourElapsedMs;
        private float pendingManifestAt = -1f;
        private Vector3 pendingManifestPosition;
        private long burstAtMs = WindupMs;
        private bool screamed;
        private bool warnUrgent;
        private long investigateEvent;
        private float lastResearchCueTime = -100f;
        private long dormantNotedDecision = -1;

        public GhostPhase Phase { get; private set; } = GhostPhase.NORMAL;
        public long PhaseElapsedMs { get; private set; }
        public long CooldownMs { get; private set; }
        public bool Dormant { get; private set; } = true;
        public bool Halted { get; private set; }
        public float DistanceToPlayer { get; private set; } = 99f;
        public float StraightDistanceToPlayer { get; private set; } = 99f;
        public bool HasNavMesh => agent != null && agent.isOnNavMesh;
        public bool BodySolid => body != null && body.enabled;
        public CapsuleCollider Body => body;
        public HollowVisual Visual => visual;
        public Vector3 ChestPosition => visual != null ? visual.ChestPosition : transform.position + Vector3.up * 1.4f;
        public GhostExecutionState? Behaviour => behaviour;

        // Attacks started so far (WINDUP entries), for presentation checks.
        public int AttackCount { get; private set; }

        // Times a running WARN pressed in because the station reported
        // VERY_NEAR (see TickWarning), for presentation checks.
        public int WarnEscalations { get; private set; }

        // True while the WARN being played is the VERY_NEAR one.
        public bool WarnUrgent => warnUrgent;

        // Times a running search moved on because a further physical event
        // arrived (see UpdateInvestigateEvent), and where it is heading.
        public int Researches { get; private set; }
        public Vector3 InvestigateTarget => investigateTarget;

        // True while the attack scream is sounding.
        public bool AttackVoicePlaying => attackVoice != null && attackVoice.isPlaying;

        // Every ghost sound as it starts (presentation only: the self-test
        // and the docs' state-to-audio table are checked against it).
        public event Action<GhostSound> SoundPlayed;

        // Raised on the frame a strike connects (the game turns it into death).
        public event Action StrikeHit;

        // Raised when a WARN behaviour starts (the game flickers a light).
        public event Action<Vector3> WarnStarted;

        // Raised while PROTECT is active / ends (the game holds lights steady).
        public event Action<bool> ProtectChanged;

        public void Initialize(GhostRuntime runtime, Transform player, GhostAnchors anchors, IGhostPresentationAssets assets,
            TelemetryChannel telemetry, Func<bool> encounterCommitted, Func<bool> powerRestored)
        {
            this.runtime = runtime;
            this.player = player;
            this.anchors = anchors;
            this.assets = assets;
            this.telemetry = telemetry ?? TelemetryChannel.Null;
            this.encounterCommitted = encounterCommitted ?? (() => false);
            this.powerRestored = powerRestored ?? (() => false);

            path = new NavMeshPath();
            agent = GetComponent<NavMeshAgent>();
            agent.speed = SpeedTrail;
            agent.acceleration = CruiseAcceleration;
            agent.angularSpeed = 360f;
            agent.stoppingDistance = PersonalSpace;
            agent.radius = 0.3f;
            agent.height = 2.1f;
            agent.autoBraking = true;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

            var visualGo = new GameObject("Hollow");
            visualGo.transform.SetParent(transform, false);
            visual = visualGo.AddComponent<HollowVisual>();
            visual.Build(assets);
            visual.SetVisible(false, true);
            BuildBody();

            breath = MakeSource("Breath", GhostSound.Breath, true, 1f, 10f);
            hum = MakeSource("Hum", GhostSound.ProtectHum, true, 1.5f, 12f);
            hum.Stop();
            oneShot = MakeSource("Voice", GhostSound.Screech, false, 2f, 30f);
            breath.Stop();
            // A low pressure you feel within a few metres (direction is vague
            // on purpose), and cloth dragging on the boards while it moves.
            presence = MakeSource("Presence", GhostSound.Presence, true, 1f, 9f);
            presence.spread = 120f;
            presenceVolume = presence.volume;
            presence.Stop();
            drag = MakeSource("Drag", GhostSound.Drag, true, 1f, 11f);
            drag.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            dragVolume = drag.volume;
            drag.volume = 0f;
            drag.Stop();
            // The scream has its own voice so it can be cut when the attack is
            // cancelled, and the highest priority so it is never culled.
            attackVoice = MakeSource("AttackVoice", GhostSound.Scream, false, 2.5f, 35f);
            attackVoice.priority = 0;
            oneShot.priority = 16;
            // Murmuring while it searches where it was drawn to.
            whisper = MakeSource("Whisper", GhostSound.Whisper, true, 1f, 8f);
            whisperVolume = whisper.volume;
            whisper.volume = 0f;
            whisper.Stop();
        }

        private void BuildBody()
        {
            Rigidbody rb = gameObject.GetComponent<Rigidbody>();
            if (rb == null)
                rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;

            var bodyGo = new GameObject("Body");
            bodyGo.layer = BodyLayer;
            bodyGo.transform.SetParent(transform, false);
            body = bodyGo.AddComponent<CapsuleCollider>();
            body.isTrigger = false;
            body.direction = 1;
            body.radius = BodyRadius;
            body.height = BodyHeight;
            body.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
            body.enabled = false;
        }

        private void SetSolid(bool solid)
        {
            if (body != null)
                body.enabled = solid;
        }

        private AudioSource MakeSource(string name, GhostSound sound, bool loop, float minDistance, float maxDistance)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            AudioSource s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 1f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDistance;
            s.maxDistance = maxDistance;
            s.dopplerLevel = 0f;
            s.loop = loop;
            s.clip = assets.Clip(sound, out float volume);
            s.volume = volume;
            if (loop)
                s.Play();
            return s;
        }

        private void Play(GhostSound sound, float scale = 1f)
        {
            AudioClip clip = assets.Clip(sound, NextVariant(sound), out float volume);
            if (clip != null)
                oneShot.PlayOneShot(clip, volume * scale);
            SoundPlayed?.Invoke(sound);
        }

        // Cycles through a sound's variations in order: repeats differ, and a
        // replay sounds the same.
        private int NextVariant(GhostSound sound)
        {
            int count = Mathf.Max(1, assets.Variants(sound));
            int i = (int)sound % nextVariant.Length;
            int v = nextVariant[i] % count;
            nextVariant[i] = v + 1;
            return v;
        }

        // The attack scream, on its own voice.
        private void Scream()
        {
            AudioClip clip = assets.Clip(GhostSound.Scream, NextVariant(GhostSound.Scream), out float volume);
            attackVoice.Stop();
            attackVoiceCut = false;
            if (clip != null)
            {
                attackVoice.clip = clip;
                attackVoice.volume = volume;
                attackVoice.pitch = 1f + ((AttackCount * 7) % 5 - 2) * 0.02f;
                attackVoice.Play();
            }
            SoundPlayed?.Invoke(GhostSound.Scream);
        }

        // ------------------------------------------------------------ control

        // Places the ghost and makes it visible (scripted beats, restarts).
        // With flicker = true it blinks out, moves, and blinks back in.
        public void Manifest(Vector3 position, bool flicker, bool sound)
        {
            Dormant = false;
            if (sound)
                Play(GhostSound.Manifest, 0.9f);
            if (!breath.isPlaying)
                breath.Play();
            if (!presence.isPlaying)
                presence.Play();
            if (!drag.isPlaying)
                drag.Play();

            if (flicker && visual.Visibility > 0.5f)
            {
                Play(GhostSound.Vanish, 0.8f);
                visual.SetVisible(false);
                SetSolid(false);
                pendingManifestAt = Time.time + 0.45f;
                pendingManifestPosition = position;
                return;
            }

            Warp(position);
            visual.SetVisible(true);
            SetSolid(true);
        }

        public void SetDormant(Vector3 position)
        {
            Dormant = true;
            pendingManifestAt = -1f;
            SetSolid(false);
            Warp(position);
            visual.SetVisible(false, true);
            breath.Stop();
            hum.Stop();
            presence.Stop();
            drag.Stop();
            attackVoice.Stop();
            whisper.Stop();
            StopAgent();
        }

        public void Halt(bool hide)
        {
            Halted = true;
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
            hum.Stop();
            drag.Stop();
            whisper.Stop();
            if (hide)
            {
                // A scream at the moment of death is left to finish; a ghost
                // that is put away is silenced.
                if (visual.Visibility > 0.5f)
                    Play(GhostSound.Vanish, 0.8f);
                visual.SetVisible(false);
                SetSolid(false);
                breath.Stop();
                presence.Stop();
                attackVoice.Stop();
            }
        }

        private void Warp(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                position = hit.position;
            if (agent.isOnNavMesh || agent.enabled)
                agent.Warp(position);
            else
                transform.position = position;
        }

        // The lantern pulse connected (DefensiveAbility has already checked
        // range, angle and line of sight). The phase changes immediately.
        public InterruptResult TryInterrupt()
        {
            if (Halted || Dormant)
                return InterruptResult.Inactive;
            if (Phase == GhostPhase.STRIKE)
                return InterruptResult.DuringStrike;
            if (Phase == GhostPhase.INTERRUPTED)
                return InterruptResult.AlreadyInterrupted;

            GhostPhase from = Phase;
            SetPhase(GhostPhase.INTERRUPTED);
            interruptRetreatStarted = false;
            visual.Flash();
            visual.SetPose(HollowVisual.Pose.Stagger, 10f);
            // A cancelled attack drops its scream (faded out in Tick); the
            // lantern hurts, so it cries out instead.
            if (attackVoice.isPlaying)
                attackVoiceCut = true;
            Play(GhostSound.LanternRecoil, 1f);
            StopAgent();
            telemetry.Emit("BEHAVIOUR", "event=INTERRUPT from_phase=" + from, decisionId: behaviourDecision,
                executionState: behaviour);
            return InterruptResult.Interrupted;
        }

        // ------------------------------------------------------------ tick

        public GhostState Report(GhostAction lastAction)
        {
            report.currentState = lastAction;
            report.phase = Phase;
            report.phaseElapsedMs = PhaseElapsedMs;
            report.attackCooldownMs = CooldownMs;
            return report;
        }

        public void Tick(float deltaTime)
        {
            if (Halted || runtime == null)
                return;

            float ms = deltaTime * 1000f + msRemainder;
            long step = (long)ms;
            msRemainder = ms - step;

            PhaseElapsedMs += step;
            behaviourElapsedMs += step;
            if (CooldownMs > 0)
                CooldownMs = Math.Max(0L, CooldownMs - step);

            RefreshDistance();

            if (pendingManifestAt > 0f && Time.time >= pendingManifestAt)
            {
                pendingManifestAt = -1f;
                Warp(pendingManifestPosition);
                visual.SetVisible(true);
                SetSolid(true);
            }

            if (Dormant)
            {
                NoteDormantDecision();
                return;
            }

            UpdatePresenceAudio();
            UpdateAttackVoice();
            // By default the head holds on the player; behaviours that search
            // or stagger clear it below.
            visual.SetLookTarget(player.position + Vector3.up * EyeHeight);

            switch (Phase)
            {
                case GhostPhase.INTERRUPTED: TickInterrupted(); return;
                case GhostPhase.WINDUP: TickWindup(); return;
                case GhostPhase.STRIKE: TickStrike(); return;
                case GhostPhase.RECOVER: TickRecover(); return;
            }

            GhostExecutionState state = runtime.CurrentState;
            if (state != behaviour)
                EnterBehaviour(state);
            else if (state == GhostExecutionState.Warning)
                UpdateWarnUrgency();
            else if (state == GhostExecutionState.Investigating)
                UpdateInvestigateEvent();

            switch (state)
            {
                case GhostExecutionState.Attacking: StartAttack(); break;
                case GhostExecutionState.Warning: TickWarning(); break;
                case GhostExecutionState.Investigating: TickInvestigating(); break;
                case GhostExecutionState.Protecting: TickProtecting(); break;
                case GhostExecutionState.Retreating: TickRetreating(); break;
                default: TickFollowing(); break;
            }

            visual.SetSpeed(agent.isOnNavMesh ? agent.velocity.magnitude : 0f);
        }

        private void EnterBehaviour(GhostExecutionState state)
        {
            if (behaviour.HasValue)
                telemetry.Emit("BEHAVIOUR", "event=PREEMPTED state=" + behaviour.Value, decisionId: behaviourDecision,
                    executionState: behaviour.Value);

            bool wasProtecting = behaviour == GhostExecutionState.Protecting;
            if (state == GhostExecutionState.Investigating)
            {
                if (!behaviour.HasValue || behaviour.Value != GhostExecutionState.Investigating)
                    searchOrigin = transform.position;
            }
            else
            {
                searchOrigin = null;
            }
            behaviour = state;
            behaviourDecision = runtime.CurrentDecisionId;
            behaviourElapsedMs = 0;
            arrived = false;
            // A WARN that starts as the VERY_NEAR one already telegraphs
            // below; only a later rise within the same WARN escalates.
            warnUrgent = state == GhostExecutionState.Warning && runtime.LastRuleId == UrgentWarnRule;
            telemetry.Emit("BEHAVIOUR", "event=START state=" + state, decisionId: behaviourDecision, executionState: state,
                action: runtime.LastAction, ruleId: runtime.LastRuleId);

            switch (state)
            {
                case GhostExecutionState.Warning:
                    if (Time.time - lastScreechTime > ScreechMinInterval && StraightDistanceToPlayer < 14f)
                    {
                        Play(GhostSound.Screech, encounterCommitted() ? 0.7f : 1f);
                        lastScreechTime = Time.time;
                    }
                    WarnStarted?.Invoke(transform.position);
                    break;
                case GhostExecutionState.Investigating:
                    investigateTarget = SearchSpot(null);
                    investigateEvent = runtime.LastCausedEventId;
                    searchLastDistance = float.MaxValue;
                    lastResearchCueTime = Time.time;
                    Play(GhostSound.Thud, 0.9f);
                    break;
                case GhostExecutionState.Retreating:
                    Play(GhostSound.Hiss, 0.8f);
                    break;
            }

            bool protecting = state == GhostExecutionState.Protecting;
            if (protecting && !hum.isPlaying)
                hum.Play();
            else if (!protecting && hum.isPlaying)
                hum.Stop();
            if (protecting != wasProtecting)
                ProtectChanged?.Invoke(protecting);
        }

        // ------------------------------------------------------------ behaviours

        private void TickFollowing()
        {
            if (encounterCommitted())
            {
                MoveToward(player.position, SpeedEncounterFollow, EncounterFollow);
                visual.SetPose(AgentMoving ? HollowVisual.Pose.Drift : HollowVisual.Pose.Watch);
                return;
            }

            // Trail 6-9 m behind: close in beyond 9 m, hold inside 6 m.
            if (DistanceToPlayer > TrailFar || (AgentMoving && DistanceToPlayer > (TrailNear + TrailFar) * 0.5f))
                MoveToward(player.position, SpeedTrail, TrailNear);
            else
                StopAndFace(IdleTurnRate);
            visual.SetPose(AgentMoving ? HollowVisual.Pose.Drift : HollowVisual.Pose.Watch);
        }

        private void TickWarning()
        {
            // Before the encounter a WARN (a physical presence or disturbance,
            // a beat, rising threat, or the player inside the threat zone)
            // makes it advance and stalk at StalkDistance instead of standing
            // where it was, so the reaction is visible. StalkDistance is
            // outside the 3.5 m threat zone: a WARN alone never brings it
            // close enough for the in-game proximity attack (E6).
            float stalk = warnUrgent ? StalkDistanceUrgent : StalkDistance;
            if (encounterCommitted())
                MoveToward(player.position, SpeedWarn, PersonalSpace);
            else if (StraightDistanceToPlayer > stalk + 0.25f)
                MoveToward(player.position, warnUrgent ? SpeedWarnUrgent : SpeedWarn, stalk);
            else
                StopAndFace();
            visual.SetPose(HollowVisual.Pose.Warn, warnUrgent ? 12f : 6f);
        }

        // NEAR -> VERY_NEAR keeps the decision WARN (R4, then X2), so without
        // this the hand moving closer changed nothing on screen. The rise is
        // telegraphed once (screech, light flicker) and the stalk tightens; a
        // fall back to NEAR only relaxes the stalk. Presentation only: the
        // rule ID is read, never decided here.
        private void UpdateWarnUrgency()
        {
            bool urgent = runtime.LastRuleId == UrgentWarnRule;
            if (urgent == warnUrgent)
                return;

            warnUrgent = urgent;
            if (!urgent)
                return;

            WarnEscalations++;
            // A hand hovering at the 30/35 cm boundary cannot make it chatter:
            // the cue plays at most once per EscalationCueMinInterval.
            if (Time.time - lastEscalationCueTime > EscalationCueMinInterval)
            {
                lastEscalationCueTime = Time.time;
                if (StraightDistanceToPlayer < 14f)
                {
                    Play(GhostSound.Screech, 0.8f);
                    lastScreechTime = Time.time;
                }
                WarnStarted?.Invoke(transform.position);
            }
            telemetry.Emit("BEHAVIOUR", "event=ESCALATE state=Warning rule=" + UrgentWarnRule, decisionId: runtime.CurrentDecisionId,
                executionState: behaviour, action: runtime.LastAction, ruleId: runtime.LastRuleId);
        }

        // A dormant ghost is not in the house, so a decision made now (a
        // sensor WARN before the first sighting, say) has no visible
        // response. Recorded once per decision so a log never implies one.
        private void NoteDormantDecision()
        {
            long decision = runtime.CurrentDecisionId;
            if (decision == dormantNotedDecision)
                return;
            dormantNotedDecision = decision;
            GhostExecutionState state = runtime.CurrentState;
            if (state == GhostExecutionState.Following)
                return;
            telemetry.Emit("BEHAVIOUR", "event=NOT_SHOWN reason=DORMANT state=" + state, decisionId: decision,
                executionState: state, action: runtime.LastAction, ruleId: runtime.LastRuleId);
        }

        private void TickInvestigating()
        {
            if (!arrived)
            {
                // Before the encounter a search never brings it into the threat
                // zone (and so never within reach of E6): it stops at
                // StalkDistance even if the anchor lies past the player. Only
                // closing in stops it, so a search that starts inside that
                // radius (after the VERY_NEAR stalk at StalkDistanceUrgent)
                // can still walk away.
                float d = StraightDistanceToPlayer;
                bool closingIn = searchLastDistance < float.MaxValue && d < searchLastDistance - 0.005f;
                searchLastDistance = d;
                if (!encounterCommitted() && d <= StalkDistance && closingIn)
                {
                    arrived = true;
                    arrivedAtMs = behaviourElapsedMs;
                }
                else
                {
                    MoveToward(investigateTarget, SpeedInvestigate, 0.4f);
                    if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= 0.6f)
                    {
                        arrived = true;
                        arrivedAtMs = behaviourElapsedMs;
                    }
                }
                // Searching: it looks where it is going, not at the player.
                visual.SetLookTarget(null);
                visual.SetPose(HollowVisual.Pose.Drift);
                return;
            }

            StopAgent();
            if (behaviourElapsedMs - arrivedAtMs < InvestigateLingerMs)
            {
                visual.SetLookTarget(null);
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.Euler(0f, transform.eulerAngles.y + Mathf.Sin(Time.time * 1.5f) * 40f, 0f), 60f * Time.deltaTime);
            }
            else
            {
                FacePlayer(IdleTurnRate);
            }
            visual.SetPose(HollowVisual.Pose.Investigate);
        }

        private void TickProtecting()
        {
            Vector3 guard = powerRestored() ? anchors.GuardAfterPower : anchors.GuardBeforePower;
            if (!arrived)
            {
                MoveToward(guard, SpeedInvestigate, 0.4f);
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= 0.6f)
                    arrived = true;
                visual.SetPose(HollowVisual.Pose.Drift);
                return;
            }

            StopAndFace(IdleTurnRate);
            visual.SetPose(HollowVisual.Pose.Protect, 3f);
        }

        private void TickRetreating()
        {
            if (!arrived)
            {
                if (behaviourElapsedMs < 50)
                    investigateTarget = Farthest(anchors.Retreat, player.position);
                MoveToward(investigateTarget, SpeedRetreat, 0.4f);
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= 0.6f)
                    arrived = true;
                visual.SetPose(HollowVisual.Pose.Recoil, 6f);
                return;
            }

            StopAndFace(IdleTurnRate);
            visual.SetPose(HollowVisual.Pose.Watch);
        }

        // ------------------------------------------------------------ attack

        private void StartAttack()
        {
            SetPhase(GhostPhase.WINDUP);
            strikeResolved = false;
            screamed = false;
            AttackCount++;
            burstAtMs = WindupMs - BurstMs[AttackCount % BurstMs.Length];
            visual.SetAttackMirror(AttackCount % 2 == 0 ? 1f : -1f);
            visual.SetPose(HollowVisual.Pose.Coil, 6f);
            Play(GhostSound.Inhale, 1f);
            telemetry.Emit("ATTACK", "event=WINDUP distance=" + F(DistanceToPlayer), decisionId: runtime.CurrentDecisionId);
        }

        // Coil, then burst: the scream starts on the first frame of the burst,
        // once per attack.
        private void TickWindup()
        {
            if (PhaseElapsedMs < burstAtMs)
            {
                MoveToward(player.position, SpeedCreep, 0.9f);
                visual.SetPose(HollowVisual.Pose.Coil, 6f);
                visual.SetSpeed(SpeedCreep);
            }
            else
            {
                if (!screamed)
                {
                    screamed = true;
                    agent.acceleration = LungeAcceleration;
                    Scream();
                }
                MoveToward(player.position, SpeedBurst, 0.9f);
                visual.SetPose(HollowVisual.Pose.Lunge, 24f);
                visual.SetSpeed(SpeedBurst);
            }

            if (PhaseElapsedMs >= WindupMs)
            {
                SetPhase(GhostPhase.STRIKE);
                visual.SetPose(HollowVisual.Pose.Strike, 30f);
                Play(GhostSound.StrikeWhiff, 1f);
            }
        }

        private void TickStrike()
        {
            MoveToward(player.position, SpeedBurst, 0.9f);
            if (!strikeResolved && InStrikeCone(out float distance, out float angle))
            {
                strikeResolved = true;
                StopAgent();
                Play(GhostSound.StrikeImpact, 1f);
                telemetry.Emit("ATTACK", "event=STRIKE_HIT distance=" + F(distance) + " angle=" + F(angle), decisionId: runtime.CurrentDecisionId);
                StrikeHit?.Invoke();
                return;
            }

            if (PhaseElapsedMs >= StrikeMs)
            {
                // A miss has its whoosh (played on entering STRIKE) and no
                // impact.
                if (!strikeResolved)
                    telemetry.Emit("ATTACK", "event=STRIKE_MISS distance=" + F(StraightDistanceToPlayer), decisionId: runtime.CurrentDecisionId);
                strikeResolved = true;
                SetPhase(GhostPhase.RECOVER);
                StopAgent();
                visual.SetPose(HollowVisual.Pose.Recover, 5f);
            }
        }

        private void TickRecover()
        {
            StopAgent();
            visual.SetPose(HollowVisual.Pose.Recover, 5f);
            if (PhaseElapsedMs >= RecoverMs)
            {
                SetPhase(GhostPhase.NORMAL);
                CooldownMs = AttackCooldownMs;
                telemetry.Emit("ATTACK", "event=RECOVER_END cooldown_ms=" + CooldownMs, decisionId: runtime.CurrentDecisionId);
            }
        }

        private void TickInterrupted()
        {
            if (PhaseElapsedMs < StaggerMs)
            {
                visual.SetLookTarget(null);
                StopAgent();
                visual.SetPose(HollowVisual.Pose.Stagger, 10f);
                return;
            }

            if (PhaseElapsedMs < StaggerMs + InterruptRetreatMs)
            {
                if (!interruptRetreatStarted)
                {
                    interruptRetreatStarted = true;
                    investigateTarget = Farthest(anchors.Retreat, player.position);
                }
                MoveToward(investigateTarget, SpeedRetreat, 0.4f);
                visual.SetPose(HollowVisual.Pose.Recoil, 6f);
                visual.SetSpeed(SpeedRetreat);
                return;
            }

            SetPhase(GhostPhase.NORMAL);
            CooldownMs = Math.Max(CooldownMs, InterruptCooldownMs);
            behaviour = null;
            telemetry.Emit("BEHAVIOUR", "event=RECOVER cooldown_ms=" + CooldownMs, decisionId: behaviourDecision);
        }

        // Spec: the strike hits if the player is within 1.6 m and within 60
        // degrees of the ghost's facing.
        public bool InStrikeCone(out float distance, out float angle)
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            distance = to.magnitude;
            angle = distance > 0.01f ? Vector3.Angle(transform.forward, to) : 0f;
            return distance <= StrikeReach && angle <= StrikeHalfAngle;
        }

        // ------------------------------------------------------------ helpers

        private void SetPhase(GhostPhase phase)
        {
            Phase = phase;
            PhaseElapsedMs = 0;
            // The lunge's hard acceleration ends with the strike.
            if (phase != GhostPhase.WINDUP && phase != GhostPhase.STRIKE && agent != null)
                agent.acceleration = CruiseAcceleration;
        }

        private bool AgentMoving => agent.isOnNavMesh && !agent.isStopped && agent.velocity.sqrMagnitude > 0.04f;

        private void MoveToward(Vector3 target, float speed, float stoppingDistance)
        {
            if (!agent.isOnNavMesh)
            {
                // No NavMesh (should not happen): glide directly, never through
                // the player's personal space.
                Vector3 to = target - transform.position;
                to.y = 0f;
                if (to.magnitude > stoppingDistance)
                    transform.position += to.normalized * speed * Time.deltaTime;
                if (to.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 360f * Time.deltaTime);
                return;
            }

            agent.isStopped = false;
            agent.updateRotation = true;
            agent.speed = speed;
            agent.stoppingDistance = stoppingDistance;
            if ((agent.destination - target).sqrMagnitude > 0.09f)
                agent.SetDestination(target);
            if (agent.remainingDistance <= stoppingDistance + 0.1f && !agent.pathPending)
                FacePlayer();
        }

        private void StopAgent()
        {
            if (agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
            }
        }

        private void StopAndFace(float degreesPerSecond = 240f)
        {
            StopAgent();
            FacePlayer(degreesPerSecond);
        }

        private void FacePlayer(float degreesPerSecond = 240f)
        {
            agent.updateRotation = false;
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), degreesPerSecond * Time.deltaTime);
        }

        // Presence follows visibility; the drag follows ground speed.
        private void UpdatePresenceAudio()
        {
            float seen = visual.Visibility;
            float moving = agent.isOnNavMesh ? Mathf.Clamp01(agent.velocity.magnitude / 2.2f) : 0f;
            presence.volume = presenceVolume * seen;
            drag.volume = Mathf.MoveTowards(drag.volume, dragVolume * moving * seen, Time.deltaTime * 1.5f);

            // The whisper only while lingering where it was drawn to search.
            bool searching = behaviour == GhostExecutionState.Investigating && arrived && Phase == GhostPhase.NORMAL;
            float w = Mathf.MoveTowards(whisper.volume, searching ? whisperVolume * seen : 0f, Time.deltaTime * (searching ? 0.6f : 1.5f));
            whisper.volume = w;
            if (w > 0.001f && !whisper.isPlaying)
                whisper.Play();
            else if (w <= 0.001f && whisper.isPlaying)
                whisper.Stop();
        }

        // A cancelled attack's scream fades out in about a tenth of a second.
        private void UpdateAttackVoice()
        {
            if (!attackVoiceCut || !attackVoice.isPlaying)
                return;
            attackVoice.volume = Mathf.MoveTowards(attackVoice.volume, 0f, Time.deltaTime * 8f);
            if (attackVoice.volume <= 0.001f)
            {
                attackVoice.Stop();
                attackVoiceCut = false;
            }
        }

        private void RefreshDistance()
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            StraightDistanceToPlayer = to.magnitude;

            if (Dormant)
            {
                // A dormant ghost is not in the house as far as the rules know.
                DistanceToPlayer = 99f;
                return;
            }

            // Path distance is refreshed every 0.15 s; at close range the
            // straight distance is used every frame (the attack checks need it).
            if (Time.time < distanceRefreshAt)
            {
                if (StraightDistanceToPlayer < 3f)
                    DistanceToPlayer = StraightDistanceToPlayer;
                return;
            }
            distanceRefreshAt = Time.time + 0.15f;

            if (path != null && agent.isOnNavMesh && NavMesh.CalculatePath(transform.position, player.position, NavMesh.AllAreas, path)
                && path.status == NavMeshPathStatus.PathComplete)
            {
                int count = path.GetCornersNonAlloc(corners);
                float length = 0f;
                for (int i = 1; i < count; i++)
                    length += Vector3.Distance(corners[i - 1], corners[i]);
                DistanceToPlayer = Mathf.Max(length, StraightDistanceToPlayer);
            }
            else
            {
                DistanceToPlayer = StraightDistanceToPlayer;
            }
        }

        // A further qualified event while the search is running (live COM6
        // run 23 Oct 20:57: shakes 111-117 re-armed the search but it had
        // already arrived and stood still, so nothing showed). The decision
        // stays INVESTIGATE; the search visibly moves on to another spot,
        // with the thud, at most once per ResearchCueMinInterval.
        private const float ResearchCueMinInterval = 1f;

        private void UpdateInvestigateEvent()
        {
            long evt = runtime.LastCausedEventId;
            if (evt == 0L || evt == investigateEvent)
                return;

            investigateEvent = evt;
            Researches++;
            investigateTarget = SearchSpot(investigateTarget);
            arrived = false;
            searchLastDistance = float.MaxValue;
            if (Time.time - lastResearchCueTime > ResearchCueMinInterval)
            {
                lastResearchCueTime = Time.time;
                Play(GhostSound.Thud, 0.9f);
            }
            telemetry.Emit("BEHAVIOUR", "event=RESEARCH state=Investigating evt=" + evt + " target=" + F(investigateTarget.x) + "," + F(investigateTarget.z),
                decisionId: runtime.CurrentDecisionId, executionState: behaviour, action: runtime.LastAction, ruleId: runtime.LastRuleId);
        }

        // Where a search goes: the investigate anchor nearest the player.
        // Before the encounter the search never comes within StalkDistance
        // of the player (TickInvestigating stops it there), so the nearest
        // anchor was often inside that radius, or past the player, and the
        // ghost "arrived" where it stood (responsiveness self-test Q05: moved
        // 0.29 m). Before the encounter the preferred spots are therefore at
        // least StalkDistance + SearchMargin from the player and reachable by
        // a NavMesh path that stays StalkDistance away from them, so the
        // search is a visible walk that never has to pass the player (a
        // straight-line "same side" test was not enough: the path to (2, 4)
        // ran down the corridor past the player, Q23); then any spot far
        // enough away; then the plain nearest. 'exclude' (the current spot)
        // is skipped so a re-armed search goes somewhere new.
        private const float SearchMargin = 1f;

        private Vector3 SearchSpot(Vector3? exclude)
        {
            Vector3 p = player.position;
            Vector3 ghostPos = transform.position;
            bool committed = encounterCommitted();
            float min = StalkDistance + SearchMargin;
            Vector3 origin = searchOrigin ?? ghostPos;

            // Pass 0: Preferred spots before the encounter:
            //   - Not excluded (not the spot we are currently at or heading to)
            //   - At least StalkDistance + SearchMargin from player (never closing into 4 m boundary)
            //   - Within MaxSearchPlayerDistance of player (bounded to local area, <= 8.5 m)
            //   - Within MaxSearchDisplacement of searchOrigin (bounds cumulative drift from repeated shakes)
            //   - NavMesh path stays at least StalkDistance away from the player
            //   - Prefer closest valid spot to the ghost (nearby anchors over increasingly distant ones)
            // Pass 1: Relax displacement budget if needed, keeping local distance to player and path clearance
            // Pass 2: Relax path clearance if needed
            // Pass 3: Fallback (committed encounter, or nearest anchor)
            for (int pass = committed ? 3 : 0; pass < 4; pass++)
            {
                Vector3 best = p;
                float bestDist = float.MaxValue;
                foreach (Vector3 a in anchors.Investigate)
                {
                    if (exclude.HasValue && Flat(a - exclude.Value) < 0.5f)
                        continue;
                    float d = Flat(a - p);
                    if (pass < 3 && d < min)
                        continue;
                    if (pass < 1 && d > MaxSearchPlayerDistance)
                        continue;
                    if (pass == 0 && Flat(a - origin) > MaxSearchDisplacement)
                        continue;
                    if (pass < 2 && !PathKeepsAway(a, p))
                        continue;

                    // Prefer valid nearby search anchors over increasingly distant anchors
                    float dist = Flat(a - ghostPos);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = a;
                    }
                }
                if (bestDist < float.MaxValue)
                    return best;
            }
            return Nearest(anchors.Investigate, p);
        }

        // True if the ghost's NavMesh path to 'to' never comes within
        // StalkDistance of 'avoid' (or, starting closer than that, never
        // closer than it starts). Called only when a search starts or moves
        // on, at most once per investigate anchor.
        private bool PathKeepsAway(Vector3 to, Vector3 avoid)
        {
            float clearance = Mathf.Min(StalkDistance, Flat(transform.position - avoid) - 0.05f);
            if (!agent.isOnNavMesh)
                return Flat(to - transform.position) <= Flat(to - avoid);
            if (searchPath == null)
                searchPath = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, to, NavMesh.AllAreas, searchPath)
                || searchPath.status != NavMeshPathStatus.PathComplete)
                return false;
            int count = searchPath.GetCornersNonAlloc(corners);
            for (int i = 1; i < count; i++)
                if (SegmentDistance(corners[i - 1], corners[i], avoid) < clearance)
                    return false;
            return true;
        }

        private static float SegmentDistance(Vector3 a, Vector3 b, Vector3 point)
        {
            a.y = b.y = point.y = 0f;
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(a + ab * t, point);
        }

        private static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }

        private static Vector3 Nearest(Vector3[] points, Vector3 to)
        {
            Vector3 best = to;
            float bestD = float.MaxValue;
            foreach (Vector3 p in points)
            {
                float d = (p - to).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private static Vector3 Farthest(Vector3[] points, Vector3 from)
        {
            Vector3 best = from;
            float bestD = -1f;
            foreach (Vector3 p in points)
            {
                float d = (p - from).sqrMagnitude;
                if (d > bestD) { bestD = d; best = p; }
            }
            return best;
        }

        private static string F(float v) => v.ToString("F2", CultureInfo.InvariantCulture);
    }
}
