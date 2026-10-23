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
    //  - the attack phases: WINDUP 1.2 s (lunge at 3.5 m/s), STRIKE 0.3 s
    //    (hits if within 1.6 m and 60 degrees of the ghost's facing), RECOVER
    //    2.0 s, then a 6 s cooldown;
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
        public const float TrailNear = 6f, TrailFar = 9f, EncounterFollow = 4f;
        public const float StrikeReach = 1.6f, StrikeHalfAngle = 60f, PersonalSpace = 1.3f;
        public const float SpeedTrail = 1.4f, SpeedEncounterFollow = 2.0f, SpeedWarn = 1.9f;
        public const float SpeedInvestigate = 2.0f, SpeedRetreat = 3.0f, SpeedLunge = 3.5f;
        public const float BodyRadius = 0.35f, BodyHeight = 2.0f;
        public const int BodyLayer = 2; // Ignore Raycast
        private const float ScreechMinInterval = 6f;
        // Idle turning is deliberately slower than the head (HollowVisual
        // turns the head first); the attack keeps the agent's fast turning.
        private const float IdleTurnRate = 110f;
        private const float EyeHeight = 1.6f;

        public enum InterruptResult
        {
            Interrupted,
            DuringStrike,
            AlreadyInterrupted,
            Inactive
        }

        private readonly GhostState report = new GhostState();
        private NavMeshPath path;
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
        private float presenceVolume, dragVolume;

        private GhostExecutionState? behaviour;
        private long behaviourDecision;
        private float msRemainder;
        private float distanceRefreshAt;
        private float lastScreechTime = -100f;
        private bool strikeResolved;
        private bool interruptRetreatStarted;
        private Vector3 investigateTarget;
        private bool arrived;
        private long arrivedAtMs;
        private long behaviourElapsedMs;
        private float pendingManifestAt = -1f;
        private Vector3 pendingManifestPosition;

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
            agent.acceleration = 12f;
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
            AudioClip clip = assets.Clip(sound, out float volume);
            if (clip != null)
                oneShot.PlayOneShot(clip, volume * scale);
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
            if (hide)
            {
                visual.SetVisible(false);
                SetSolid(false);
                breath.Stop();
                presence.Stop();
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
            Play(GhostSound.Hiss, 1.1f);
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
                return;

            UpdatePresenceAudio();
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
            behaviour = state;
            behaviourDecision = runtime.CurrentDecisionId;
            behaviourElapsedMs = 0;
            arrived = false;
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
                    investigateTarget = Nearest(anchors.Investigate, player.position);
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
            if (encounterCommitted())
                MoveToward(player.position, SpeedWarn, PersonalSpace);
            else
                StopAndFace();
            visual.SetPose(HollowVisual.Pose.Warn, 6f);
        }

        private void TickInvestigating()
        {
            if (!arrived)
            {
                MoveToward(investigateTarget, SpeedInvestigate, 0.4f);
                if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance <= 0.6f)
                {
                    arrived = true;
                    arrivedAtMs = behaviourElapsedMs;
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
            Play(GhostSound.AttackRise, 1f);
            visual.SetPose(HollowVisual.Pose.Windup, 8f);
            telemetry.Emit("ATTACK", "event=WINDUP distance=" + F(DistanceToPlayer), decisionId: runtime.CurrentDecisionId);
        }

        private void TickWindup()
        {
            MoveToward(player.position, SpeedLunge, 0.9f);
            visual.SetPose(HollowVisual.Pose.Windup, 8f);
            visual.SetSpeed(SpeedLunge);
            if (PhaseElapsedMs >= WindupMs)
            {
                SetPhase(GhostPhase.STRIKE);
                visual.SetPose(HollowVisual.Pose.Strike, 18f);
            }
        }

        private void TickStrike()
        {
            MoveToward(player.position, SpeedLunge, 0.9f);
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
                if (!strikeResolved)
                {
                    Play(GhostSound.StrikeWhiff, 1f);
                    telemetry.Emit("ATTACK", "event=STRIKE_MISS distance=" + F(StraightDistanceToPlayer), decisionId: runtime.CurrentDecisionId);
                }
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
