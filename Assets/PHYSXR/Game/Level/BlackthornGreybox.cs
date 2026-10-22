using System.Collections.Generic;
using PHYSXR.Game.Art;
using PHYSXR.Game.Audio;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Input;
using PHYSXR.Game.Player;
using PHYSXR.Game.Settings;
using PHYSXR.Game.UI;
using PHYSXR.Ghost;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace PHYSXR.Game.Level
{
    // Builds Blackthorn House when the game scene loads, then wires the
    // player, the ghost, interactables, power, audio, UI and flow controller
    // together explicitly (no Find calls, no singletons).
    //
    // The class keeps its M2 name (BlackthornGreybox) because the game scene
    // references this script; it now builds the finished house rather than a
    // greybox. Building at load time keeps the scene file trivial and makes
    // every restart (a scene reload) rebuild the whole run from the same code,
    // so no run state can survive. All art is generated in code
    // (ProceduralTextures, HouseMaterials, MeshKit, ProceduralAudio).
    //
    // The file is split by concern:
    //   BlackthornGreybox.cs              constants, composition, helpers, gizmo preview
    //   BlackthornGreybox.Architecture.cs floors, walls, linings, doorways, windows, stairs
    //   BlackthornGreybox.Props.cs        furniture and set dressing, room by room
    //   BlackthornGreybox.Atmosphere.cs   lights, sound beds, fog, post-processing,
    //                                     storm, the ending's exterior set
    //
    // Layout (spec section C, "five rooms in a loop"), metres, top view:
    //
    //   z=16 +-----------+-----------+
    //        |  Kitchen  |  Utility  |   fuse on the kitchen table,
    //        |   (fuse)  =  (fuse    |   fuse box on the utility east wall,
    //        |           |   box)    |   checkpoint by the fuse box
    //   z=8  +----= -----+-----= ----+
    //        | |Corr-| . |  Dining   |   a 2.6 m corridor; the dining room
    //        | |idor | . | (encounter|   doorway from the utility room is the
    //        | |     | . |  trigger) |   encounter trigger
    //   z=0  +----= -----+-----= ----+
    //        |        Entry Hall     |   start; front door (exit) south
    //   z=-8 +----------[D]----------+
    //       x=-8        x=0         x=8
    //
    // Walk-only critical path is about 60 m (about 35 s at 1.8 m/s).
    public partial class BlackthornGreybox : MonoBehaviour
    {
        public const float WallHeight = 3f;
        public const float WallThickness = 0.2f;
        public const float DoorWidth = 1.4f;
        public const float DoorHeight = 2.3f;

        public static readonly Vector3 StartPosition = new Vector3(0f, 0.05f, -5.5f);
        public const float StartYaw = 0f;

        public static readonly Vector3 FusePosition = new Vector3(-6.4f, 0.92f, 14.4f);
        public static readonly Vector3 FuseBoxPosition = new Vector3(7.8f, 1.4f, 12f);
        public static readonly Vector3 ExitDoorPosition = new Vector3(0f, 1.1f, -8f);

        // Doorway centres along the critical path, in walking order.
        public static readonly Vector3[] RouteToFuse =
        {
            new Vector3(-4f, 0f, -1.2f), new Vector3(-4f, 0f, 1.2f),
            new Vector3(-4f, 0f, 6.8f), new Vector3(-4f, 0f, 9.2f),
            new Vector3(-5.3f, 0f, 13.3f)
        };
        public static readonly Vector3[] RouteToFuseBox =
        {
            new Vector3(-1.2f, 0f, 12f), new Vector3(1.2f, 0f, 12f), new Vector3(6.7f, 0f, 12f)
        };
        public static readonly Vector3[] RouteToExit =
        {
            new Vector3(4f, 0f, 9.2f), new Vector3(4f, 0f, 6.8f),
            new Vector3(4f, 0f, 1.2f), new Vector3(4f, 0f, -1.2f),
            new Vector3(0f, 0f, -6.5f)
        };

        // Where the player stands to test the locked door at the start.
        public static readonly Vector3 ExitStandPosition = new Vector3(0f, 0f, -6.5f);

        // Where the player stands for the ending (the front yard).
        public static readonly Vector3 EndingPosition = new Vector3(0f, 0.05f, -17f);

        private NavMeshDataInstance navMeshInstance;

        private readonly List<Light> mainLights = new List<Light>();
        private readonly List<Light> emergencyLights = new List<Light>();
        private readonly List<Renderer> bulbs = new List<Renderer>();
        private readonly List<Renderer> emergencyBulbs = new List<Renderer>();
        private readonly List<ReadableNote> notes = new List<ReadableNote>();
        private Transform interior;
        private Transform exterior;

        public PlayerController Player { get; private set; }
        public GameFlowController FlowController { get; private set; }
        public PowerSystem Power { get; private set; }
        public FusePickup Fuse { get; private set; }
        public FuseBox FuseBox { get; private set; }
        public ExitDoor Exit { get; private set; }
        public GhostRuntime GhostRuntime { get; private set; }
        public GhostMotionController Ghost { get; private set; }
        public DefensiveAbility Lantern { get; private set; }
        public GameAudio Audio { get; private set; }
        public GameUI UI { get; private set; }
        public GhostAnchors Anchors { get; private set; }
        public StormController Storm { get; private set; }
        public EndingSequence Ending { get; private set; }
        // True once the ghost's NavMesh was baked and registered.
        public bool HasNavMesh => navMeshInstance.valid;
        public IReadOnlyList<ReadableNote> Notes => notes;

        private void Awake()
        {
            interior = new GameObject("Interior").transform;
            interior.SetParent(transform, false);
            exterior = new GameObject("Exterior").transform;
            exterior.SetParent(transform, false);

            GameObject systems = new GameObject("GameFlow");
            systems.transform.SetParent(transform, false);
            Audio = systems.AddComponent<GameAudio>();
            Power = systems.AddComponent<PowerSystem>();
            FlowController = systems.AddComponent<GameFlowController>();

            BuildArchitecture(interior);
            BuildProps(interior);
            BuildLights(interior);
            BuildExterior(exterior);
            BakeNavMesh();

            Player = BuildPlayer();
            Fuse = BuildFuse(interior);
            FuseBox = BuildFuseBox(interior);
            Exit = BuildExitDoor(interior);
            Anchors = BuildAnchors();
            BuildGhost();

            Power.Register(mainLights, emergencyLights, bulbs);
            Power.RegisterEmergencyBulbs(emergencyBulbs);
            Power.SetAudio(Audio, FuseBoxPosition);
            FlowController.Initialize(Power);
            Player.Initialize(Player.transform.Find("Head"), Player.GetComponent<DesktopInputAdapter>(), FlowController);
            Player.Respawn(StartPosition, StartYaw);

            Lantern = Player.gameObject.AddComponent<DefensiveAbility>();
            Lantern.Initialize(Player.Head, Ghost, Audio, null);
            Player.SetAbilities(Lantern, Audio);

            Fuse.Initialize(FlowController, Audio);
            Exit.Initialize(FlowController, Exit.transform.Find("Hinge"), Exit.GetComponentInChildren<Light>(),
                Exit.transform.Find("Hinge/LockIndicator").GetComponent<Renderer>(), Audio);

            BuildInteractables(interior);
            BuildTriggers();
            BuildAtmosphere(systems.transform);

            FlowController.Compose(Player, GhostRuntime, Ghost, Lantern, Audio, Fuse, FuseBox, Anchors, StartPosition, StartYaw);

            Ending = systems.AddComponent<EndingSequence>();
            Ending.Initialize(FlowController, Player, Lantern, Storm, exterior, Audio, EndingPosition);

            UI = systems.AddComponent<GameUI>();
            UI.Initialize(FlowController, Player.Interactor, Lantern, Ending, Audio);
            UI.SettingsChanged += ApplySettings;
        }

        private void Start()
        {
            // Services (settings, telemetry) are attached after Awake; apply the
            // per-player settings and the telemetry channel now.
            GameServices services = FlowController.Services;
            if (services == null)
                return;

            Lantern.SetTelemetry(services.Telemetry);
            ApplySettings();
        }

        // Player settings: mouse, head bob, volume, brightness.
        private void ApplySettings()
        {
            GameSettings settings = FlowController.Services?.Settings;
            if (settings == null)
                return;

            var input = Player.GetComponent<DesktopInputAdapter>();
            input.MouseSensitivity = settings.MouseSensitivity;
            input.InvertY = settings.InvertY;
            Player.HeadBob = settings.HeadBob;
            settings.ApplyAudio();
            PostFx?.SetBrightness(settings.Brightness);
        }

#if UNITY_EDITOR
        // Edit-mode layout preview. The house is built only at runtime (Awake),
        // so outside Play mode the scene holds just this component. This replays
        // the same BuildArchitecture/BuildProps layout code with gizmo drawing
        // instead of object creation: nothing is added to or saved in the scene,
        // nothing runs in Play mode, and none of it exists in player builds.
        // Only BuildArchitecture/BuildProps may run here: their helpers never
        // use what Group()/Block() return, which are null while drawingGizmos is set.
        private bool drawingGizmos;

        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
                return;

            drawingGizmos = true;
            try
            {
                BuildArchitecture(null);
                BuildProps(null);
            }
            finally
            {
                drawingGizmos = false;
                Gizmos.matrix = Matrix4x4.identity;
            }

            Marker(StartPosition + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f), Color.cyan);
            Marker(FusePosition, new Vector3(0.3f, 0.3f, 0.3f), new Color(1f, 0.55f, 0.1f));
            Marker(FuseBoxPosition, new Vector3(0.25f, 0.6f, 0.45f), new Color(0.35f, 0.37f, 0.4f));
            Marker(ExitDoorPosition, new Vector3(1.2f, 2.3f, 0.1f), new Color(0.22f, 0.12f, 0.07f));
            Marker(GameFlowController.CheckpointPosition + Vector3.up * 0.9f, new Vector3(0.5f, 1.8f, 0.5f), Color.green);
        }

        private static void Marker(Vector3 centre, Vector3 size, Color color)
        {
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = color;
            Gizmos.DrawCube(centre, size);
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(centre, size);
        }

        private static Color GizmoColor(HouseMaterials.Surface surface)
        {
            switch (surface)
            {
                case HouseMaterials.Surface.FloorBoards: return new Color(0.3f, 0.2f, 0.12f);
                case HouseMaterials.Surface.Tiles: return new Color(0.45f, 0.45f, 0.42f);
                case HouseMaterials.Surface.Wallpaper: return new Color(0.35f, 0.38f, 0.3f);
                case HouseMaterials.Surface.Plaster: return new Color(0.4f, 0.4f, 0.38f);
                case HouseMaterials.Surface.Metal: return new Color(0.35f, 0.37f, 0.4f);
                case HouseMaterials.Surface.Rug:
                case HouseMaterials.Surface.Runner: return new Color(0.4f, 0.1f, 0.08f);
                default: return new Color(0.28f, 0.18f, 0.11f);
            }
        }
#endif

        // ---------------------------------------------------------------- nav

        // Bakes the ghost's NavMesh from the interior colliders, at runtime,
        // with explicit build settings: a 0.3 m radius so it fits the doorways
        // and a 0.2 m climb so it cannot walk over furniture. The settings are
        // a modified copy of the default agent type's (0), and the data is
        // registered under that type, so the agent and the data always match.
        // Nothing else in the project bakes for agent type 0. The data is
        // removed again when the scene unloads.
        private void BakeNavMesh()
        {
            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(GhostAgentType);
            settings.agentRadius = 0.3f;
            settings.agentHeight = 2.0f;
            settings.agentClimb = 0.2f;
            settings.agentSlope = 30f;
            settings.overrideVoxelSize = true;
            settings.voxelSize = 0.06f;

            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(interior, Physics.DefaultRaycastLayers, NavMeshCollectGeometry.PhysicsColliders, 0,
                new List<NavMeshBuildMarkup>(), sources);
            var bounds = new Bounds(new Vector3(0f, 1.5f, 4f), new Vector3(18f, 4f, 26f));
            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                Debug.LogError("[BlackthornGreybox] NavMesh bake failed; the ghost will glide without pathfinding.");
                return;
            }
            data.name = "PHYSXR_GhostNavMesh";
            navMeshInstance = NavMesh.AddNavMeshData(data);
        }

        private const int GhostAgentType = 0;

        private void OnDestroy()
        {
            if (navMeshInstance.valid)
                navMeshInstance.Remove();
            if (dustMaterial != null)
                Destroy(dustMaterial);
        }

        // --------------------------------------------------------------- player

        private PlayerController BuildPlayer()
        {
            GameObject player = new GameObject("Player");
            player.transform.SetParent(transform, false);

            CharacterController body = player.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.3f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.stepOffset = 0.3f;
            body.slopeLimit = 45f;

            GameObject head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            Camera camera = head.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 80f;
            camera.fieldOfView = 70f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.01f, 0.012f, 0.016f);
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.stopNaN = true;
            head.AddComponent<AudioListener>();

            player.AddComponent<DesktopInputAdapter>();
            player.AddComponent<DesktopMotor>();
            player.AddComponent<PlayerInteractor>();
            return player.AddComponent<PlayerController>();
        }

        // ---------------------------------------------------------------- ghost

        private GhostAnchors BuildAnchors()
        {
            return new GhostAnchors
            {
                Spawn = new Vector3(-4f, 0f, 12f),
                CheckpointSpawn = new Vector3(-3.5f, 0f, 10.5f),
                FirstSighting = new Vector3(-4f, 0f, 7.2f),
                EncounterManifest = new Vector3(-0.5f, 0f, -3.2f),
                GuardBeforePower = new Vector3(-3f, 0f, 13f),
                GuardAfterPower = new Vector3(1.5f, 0f, -6f),
                Investigate = new[]
                {
                    new Vector3(-5f, 0f, -3.5f), new Vector3(3f, 0f, -3f), new Vector3(-4f, 0f, 3f),
                    new Vector3(-3f, 0f, 11.5f), new Vector3(3f, 0f, 11.5f), new Vector3(2f, 0f, 4f)
                },
                Retreat = new[]
                {
                    new Vector3(-6.9f, 0f, 12.8f), new Vector3(7.2f, 0f, 13.6f), new Vector3(7.2f, 0f, 0.8f),
                    new Vector3(-5.5f, 0f, -7f), new Vector3(-4f, 0f, 7.4f)
                }
            };
        }

        // Secondary first-sighting anchor for players who take the loop the
        // other way round (entry hall -> dining room).
        public static readonly Vector3 FirstSightingAlt = new Vector3(4f, 0f, 6.8f);

        private void BuildGhost()
        {
            // Built inactive so the agent is created with the ghost's agent
            // type already set (it binds to the NavMesh on activation).
            var go = new GameObject("TheHollow");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            go.transform.position = Anchors.Spawn;
            NavMeshAgent agent = go.AddComponent<NavMeshAgent>();
            agent.agentTypeID = GhostAgentType;
            agent.radius = 0.3f;
            agent.height = 2.1f;
            GhostRuntime = go.AddComponent<GhostRuntime>();
            Ghost = go.AddComponent<GhostMotionController>();
            go.SetActive(true);
        }

        // --------------------------------------------------------- interactables

        private FusePickup BuildFuse(Transform parent)
        {
            GameObject fuse = new GameObject("Fuse");
            fuse.transform.SetParent(parent, false);
            fuse.transform.position = FusePosition;

            Block(fuse.transform, "FuseBody", FusePosition, new Vector3(0.1f, 0.1f, 0.26f), HouseMaterials.Surface.Fuse, false);
            Block(fuse.transform, "FuseCapA", FusePosition + new Vector3(0f, 0f, 0.14f), new Vector3(0.11f, 0.11f, 0.03f), HouseMaterials.Surface.Brass, false);
            Block(fuse.transform, "FuseCapB", FusePosition + new Vector3(0f, 0f, -0.14f), new Vector3(0.11f, 0.11f, 0.03f), HouseMaterials.Surface.Brass, false);
            // A generous collider makes the small fuse easy to target.
            BoxCollider target = fuse.AddComponent<BoxCollider>();
            target.size = new Vector3(0.45f, 0.35f, 0.5f);
            // A faint glow so the fuse is discoverable in the dark kitchen.
            PointLight(fuse.transform, "FuseGlow", FusePosition + Vector3.up * 0.3f, new Color(1f, 0.55f, 0.1f), 1.6f, 2.8f, false);

            return fuse.AddComponent<FusePickup>();
        }

        private FuseBox BuildFuseBox(Transform parent)
        {
            GameObject box = Block(parent, "FuseBox", FuseBoxPosition, new Vector3(0.25f, 0.6f, 0.45f), HouseMaterials.Surface.Metal);
            Block(parent, "FuseBox_Door", FuseBoxPosition + new Vector3(-0.2f, 0f, 0.36f), new Vector3(0.02f, 0.58f, 0.42f),
                HouseMaterials.Surface.Metal, false, Quaternion.Euler(0f, -60f, 0f));
            Block(parent, "FuseBox_Conduit", FuseBoxPosition + new Vector3(0.02f, 0.95f, 0f), new Vector3(0.06f, 1.3f, 0.06f), HouseMaterials.Surface.Metal, false);
            Block(parent, "FuseBox_Warning", FuseBoxPosition + new Vector3(-0.13f, 0.22f, 0f), new Vector3(0.005f, 0.08f, 0.2f), HouseMaterials.Surface.Paper, false);
            // Sibling, not child, so the box's collider stays the only target.
            GameObject slot = Block(parent, "FuseBox_InstalledFuse", FuseBoxPosition + new Vector3(-0.14f, -0.05f, 0f),
                new Vector3(0.06f, 0.1f, 0.26f), HouseMaterials.Surface.FuseInstalled, false);

            var leverPivot = new GameObject("FuseBox_Lever").transform;
            leverPivot.SetParent(parent, false);
            leverPivot.position = FuseBoxPosition + new Vector3(-0.15f, 0.12f, 0.27f);
            leverPivot.rotation = Quaternion.Euler(0f, 0f, 0f);
            Block(leverPivot, "Handle", leverPivot.position + new Vector3(0f, -0.08f, 0f), new Vector3(0.03f, 0.16f, 0.03f), HouseMaterials.Surface.Black, false);

            FuseBox fuseBox = box.AddComponent<FuseBox>();
            fuseBox.Initialize(FlowController, slot, Audio, leverPivot);
            return fuseBox;
        }

        private ExitDoor BuildExitDoor(Transform parent)
        {
            GameObject door = new GameObject("FrontDoor");
            door.transform.SetParent(parent, false);
            door.transform.position = new Vector3(0f, 0f, -8f);

            // Hinge at the door's left edge, so opening swings the panel.
            GameObject hinge = new GameObject("Hinge");
            hinge.transform.SetParent(door.transform, false);
            hinge.transform.localPosition = new Vector3(-0.6f, 0f, 0f);

            Vector3 d = door.transform.position;
            Block(hinge.transform, "Panel", d + new Vector3(0f, 1.15f, 0f), new Vector3(1.18f, 2.28f, 0.08f), HouseMaterials.Surface.DarkWood);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -0.26f : 0.26f;
                float y = i < 2 ? 0.65f : 1.6f;
                Block(hinge.transform, "Inset" + i, d + new Vector3(x, y, 0.05f), new Vector3(0.38f, i < 2 ? 0.75f : 0.85f, 0.02f), HouseMaterials.Surface.Trim, false);
            }
            Block(hinge.transform, "Knob", d + new Vector3(0.45f, 1.0f, 0.09f), new Vector3(0.06f, 0.06f, 0.06f), HouseMaterials.Surface.Brass, false);
            GameObject indicator = Block(hinge.transform, "LockIndicator", d + new Vector3(0.42f, 1.18f, 0.08f),
                new Vector3(0.08f, 0.12f, 0.04f), HouseMaterials.Surface.LockRed, false);
            indicator.name = "LockIndicator";
            PointLight(hinge.transform, "LockLight", d + new Vector3(0.42f, 1.18f, 0.3f), Color.red, 0.5f, 1.8f, false);

            return door.AddComponent<ExitDoor>();
        }

        // -------------------------------------------------------------- helpers

        private Transform Group(Transform parent, string name)
        {
#if UNITY_EDITOR
            if (drawingGizmos)
                return null;
#endif
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        // A box with metre-scaled UVs. The transform is never scaled, so
        // children of a block keep their own sizes.
        private GameObject Block(Transform parent, string name, Vector3 worldCentre, Vector3 size, HouseMaterials.Surface surface,
            bool collider = true, Quaternion? rotation = null, float uvMetres = 1.5f, bool castShadows = true)
        {
#if UNITY_EDITOR
            if (drawingGizmos)
            {
                Gizmos.matrix = Matrix4x4.TRS(worldCentre, rotation ?? Quaternion.identity, Vector3.one);
                Color c = GizmoColor(surface);
                Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
                Gizmos.DrawCube(Vector3.zero, size);
                Gizmos.color = c;
                Gizmos.DrawWireCube(Vector3.zero, size);
                return null;
            }
#endif
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(worldCentre, rotation ?? Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(size, uvMetres);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = HouseMaterials.Get(surface);
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider)
                go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private GameObject Cylinder(Transform parent, string name, Vector3 worldCentre, float radius, float height, HouseMaterials.Surface surface,
            bool collider = true, Quaternion? rotation = null, float topRadius = -1f)
        {
#if UNITY_EDITOR
            if (drawingGizmos)
            {
                Gizmos.matrix = Matrix4x4.TRS(worldCentre, rotation ?? Quaternion.identity, Vector3.one);
                Gizmos.color = GizmoColor(surface);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(radius * 2f, height, radius * 2f));
                return null;
            }
#endif
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(worldCentre, rotation ?? Quaternion.identity);
            go.AddComponent<MeshFilter>().sharedMesh = MeshKit.Cylinder(radius, height, 16, topRadius);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = HouseMaterials.Get(surface);
            if (collider)
                go.AddComponent<BoxCollider>().size = new Vector3(radius * 2f, height, radius * 2f);
            return go;
        }

        private GameObject Sphere(Transform parent, string name, Vector3 worldCentre, Vector3 size, HouseMaterials.Surface surface)
        {
#if UNITY_EDITOR
            if (drawingGizmos)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.color = GizmoColor(surface);
                Gizmos.DrawWireSphere(worldCentre, Mathf.Max(size.x, size.y, size.z) * 0.5f);
                return null;
            }
#endif
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = worldCentre;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = HouseMaterials.Get(surface);
            return go;
        }

        private static Light PointLight(Transform parent, string name, Vector3 position, Color color, float intensity, float range, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowNearPlane = 0.2f;
            return light;
        }
    }
}
