using System.Collections.Generic;
using PHYSXR.Game.Gameplay;
using PHYSXR.Game.Input;
using PHYSXR.Game.Player;
using PHYSXR.Game.UI;
using UnityEngine;

namespace PHYSXR.Game.Level
{
    // Builds the M2 greybox of Blackthorn House from primitives when the game
    // scene loads, then wires the player, interactables, power system, flow
    // controller and HUD together explicitly (no Find calls, no singletons).
    //
    // Building at load time keeps the scene file trivial and makes a restart
    // (scene reload) rebuild everything from the same code, so no run state
    // can survive. The art pass (M10) replaces this with authored rooms.
    //
    // Layout (spec section C, "five rooms in a loop"), metres, top view:
    //
    //   z=16 +-----------+-----------+
    //        |  Kitchen  |  Utility  |   fuse on the kitchen table,
    //        |   (fuse)  =  (fuse    |   fuse box on the utility east wall
    //        |           |   box)    |
    //   z=8  +----= -----+-----= ----+
    //        |  Corridor |  Dining   |   corridor/dining wall is solid,
    //        |           |           |   which forces the loop
    //   z=0  +----= -----+-----= ----+
    //        |        Entry Hall     |   start; front door (exit) south
    //   z=-8 +----------[D]----------+
    //       x=-8        x=0         x=8
    //
    // Walk-only critical path is about 60 m (about 30 s at 2.2 m/s).
    public class BlackthornGreybox : MonoBehaviour
    {
        public const float WallHeight = 3f;
        public const float WallThickness = 0.2f;
        public const float DoorWidth = 1.4f;

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

        private readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        private readonly List<Light> mainLights = new List<Light>();
        private readonly List<Light> emergencyLights = new List<Light>();
        private readonly List<Renderer> bulbs = new List<Renderer>();
        private Material baseMaterial;

        public PlayerController Player { get; private set; }
        public GameFlowController FlowController { get; private set; }
        public PowerSystem Power { get; private set; }
        public FusePickup Fuse { get; private set; }
        public FuseBox FuseBox { get; private set; }
        public ExitDoor Exit { get; private set; }

        private static readonly Color WallColor = new Color(0.42f, 0.4f, 0.37f);
        private static readonly Color FloorColor = new Color(0.25f, 0.2f, 0.16f);
        private static readonly Color CeilingColor = new Color(0.3f, 0.3f, 0.3f);
        private static readonly Color FurnitureColor = new Color(0.33f, 0.22f, 0.14f);
        private static readonly Color MetalColor = new Color(0.35f, 0.37f, 0.4f);
        private static readonly Color FuseColor = new Color(1f, 0.55f, 0.1f);
        private static readonly Color DoorColor = new Color(0.22f, 0.12f, 0.07f);

        private void Awake()
        {
            Transform environment = new GameObject("Environment").transform;
            environment.SetParent(transform, false);

            BuildRooms(environment);
            BuildFurniture(environment);
            BuildLights(environment);

            GameObject systems = new GameObject("GameFlow");
            systems.transform.SetParent(transform, false);
            Power = systems.AddComponent<PowerSystem>();
            FlowController = systems.AddComponent<GameFlowController>();

            Player = BuildPlayer();
            Fuse = BuildFuse(environment);
            FuseBox = BuildFuseBox(environment);
            Exit = BuildExitDoor(environment);

            Power.Register(mainLights, emergencyLights, bulbs);
            FlowController.Initialize(Power);
            Player.Initialize(Player.transform.Find("Head"), Player.GetComponent<DesktopInputAdapter>(), FlowController);
            Player.Respawn(StartPosition, StartYaw);
            Fuse.Initialize(FlowController);
            Exit.Initialize(FlowController, Exit.transform.Find("Hinge"), Exit.GetComponentInChildren<Light>(),
                Exit.transform.Find("Hinge/LockIndicator").GetComponent<Renderer>());

            GameUI ui = systems.AddComponent<GameUI>();
            ui.Initialize(FlowController, Player.Interactor);
        }

        // ---------------------------------------------------------------- rooms

        private void BuildRooms(Transform parent)
        {
            Transform rooms = Group(parent, "Rooms");
            Room(rooms, "EntryHall", -8f, 8f, -8f, 0f);
            Room(rooms, "Corridor", -8f, 0f, 0f, 8f);
            Room(rooms, "Dining", 0f, 8f, 0f, 8f);
            Room(rooms, "Kitchen", -8f, 0f, 8f, 16f);
            Room(rooms, "Utility", 0f, 8f, 8f, 16f);

            Transform walls = Group(parent, "Walls");
            // Outer shell. The south wall's gap holds the front door.
            WallX(walls, "South", -8f, 8f, -8f, 0f, 1.2f);
            WallX(walls, "North", -8f, 8f, 16f);
            WallZ(walls, "West", -8f, 16f, -8f);
            WallZ(walls, "East", -8f, 16f, 8f);
            // Interior.
            WallX(walls, "Entry_Corridor", -8f, 0f, 0f, -4f, DoorWidth);
            WallX(walls, "Entry_Dining", 0f, 8f, 0f, 4f, DoorWidth);
            WallX(walls, "Corridor_Kitchen", -8f, 0f, 8f, -4f, DoorWidth);
            WallX(walls, "Dining_Utility", 0f, 8f, 8f, 4f, DoorWidth);
            WallZ(walls, "Corridor_Dining", 0f, 8f, 0f);
            WallZ(walls, "Kitchen_Utility", 8f, 16f, 0f, 12f, DoorWidth);
        }

        private void Room(Transform parent, string name, float x0, float x1, float z0, float z1)
        {
            Transform room = Group(parent, name);
            Vector3 centre = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            Vector3 size = new Vector3(x1 - x0, 0.1f, z1 - z0);
            Block(room, "Floor", centre + Vector3.down * 0.05f, size, FloorColor);
            Block(room, "Ceiling", centre + Vector3.up * (WallHeight + 0.05f), size, CeilingColor);
        }

        // Wall along X at constant z, optionally with one gap centred at gapX.
        private void WallX(Transform parent, string name, float x0, float x1, float z, float gapX = 0f, float gapWidth = 0f)
        {
            if (gapWidth <= 0f)
            {
                Segment(parent, name, new Vector3((x0 + x1) / 2f, 0f, z), new Vector3(x1 - x0 + WallThickness, 0f, WallThickness));
                return;
            }
            float a = gapX - gapWidth / 2f;
            float b = gapX + gapWidth / 2f;
            Segment(parent, name + "_A", new Vector3((x0 + a) / 2f, 0f, z), new Vector3(a - x0 + WallThickness / 2f, 0f, WallThickness));
            Segment(parent, name + "_B", new Vector3((b + x1) / 2f, 0f, z), new Vector3(x1 - b + WallThickness / 2f, 0f, WallThickness));
            Lintel(parent, name + "_Lintel", new Vector3(gapX, 0f, z), new Vector3(gapWidth, 0f, WallThickness));
        }

        // Wall along Z at constant x, optionally with one gap centred at gapZ.
        private void WallZ(Transform parent, string name, float z0, float z1, float x, float gapZ = 0f, float gapWidth = 0f)
        {
            if (gapWidth <= 0f)
            {
                Segment(parent, name, new Vector3(x, 0f, (z0 + z1) / 2f), new Vector3(WallThickness, 0f, z1 - z0 + WallThickness));
                return;
            }
            float a = gapZ - gapWidth / 2f;
            float b = gapZ + gapWidth / 2f;
            Segment(parent, name + "_A", new Vector3(x, 0f, (z0 + a) / 2f), new Vector3(WallThickness, 0f, a - z0 + WallThickness / 2f));
            Segment(parent, name + "_B", new Vector3(x, 0f, (b + z1) / 2f), new Vector3(WallThickness, 0f, z1 - b + WallThickness / 2f));
            Lintel(parent, name + "_Lintel", new Vector3(x, 0f, gapZ), new Vector3(WallThickness, 0f, gapWidth));
        }

        private void Segment(Transform parent, string name, Vector3 baseCentre, Vector3 footprint)
        {
            Block(parent, name, baseCentre + Vector3.up * (WallHeight / 2f),
                new Vector3(footprint.x, WallHeight, footprint.z), WallColor);
        }

        private void Lintel(Transform parent, string name, Vector3 baseCentre, Vector3 footprint)
        {
            const float doorHeight = 2.3f;
            float h = WallHeight - doorHeight;
            Block(parent, name, baseCentre + Vector3.up * (doorHeight + h / 2f), new Vector3(footprint.x, h, footprint.z), WallColor);
        }

        // ------------------------------------------------------------ furniture

        private void BuildFurniture(Transform parent)
        {
            Transform props = Group(parent, "Props");
            // Kitchen: worktop along the north wall and the table the fuse lies on.
            Block(props, "Kitchen_Worktop", new Vector3(-4f, 0.45f, 15.5f), new Vector3(5f, 0.9f, 0.7f), FurnitureColor);
            Block(props, "Kitchen_Table", new Vector3(-6.4f, 0.42f, 14.4f), new Vector3(1.2f, 0.84f, 0.9f), FurnitureColor);
            // Dining: long table in the middle of the loop's return leg.
            Block(props, "Dining_Table", new Vector3(5.6f, 0.4f, 4f), new Vector3(1.4f, 0.8f, 3.4f), FurnitureColor);
            // Entry hall: a bench and a cabinet as orientation landmarks.
            Block(props, "Entry_Bench", new Vector3(-6.5f, 0.25f, -4f), new Vector3(0.6f, 0.5f, 2f), FurnitureColor);
            Block(props, "Entry_Cabinet", new Vector3(6.8f, 0.9f, -5f), new Vector3(0.8f, 1.8f, 1.6f), FurnitureColor);
            // Utility: shelving opposite the fuse box.
            Block(props, "Utility_Shelf", new Vector3(1f, 1f, 15.4f), new Vector3(1.6f, 2f, 0.5f), MetalColor);
            // Corridor: a narrow sideboard to break up the empty room.
            Block(props, "Corridor_Sideboard", new Vector3(-7.5f, 0.45f, 4f), new Vector3(0.6f, 0.9f, 2.4f), FurnitureColor);
        }

        // --------------------------------------------------------------- lights

        private void BuildLights(Transform parent)
        {
            Transform lights = Group(parent, "Lights");
            Vector3[] ceiling =
            {
                new Vector3(0f, 2.8f, -4f), new Vector3(-4f, 2.8f, 4f), new Vector3(4f, 2.8f, 4f),
                new Vector3(-4f, 2.8f, 12f), new Vector3(4f, 2.8f, 12f)
            };
            for (int i = 0; i < ceiling.Length; i++)
            {
                mainLights.Add(PointLight(lights, "CeilingLight_" + i, ceiling[i], new Color(1f, 0.85f, 0.6f), 1.6f, 9f));
                GameObject bulb = Block(lights, "Bulb_" + i, ceiling[i] + Vector3.up * 0.1f, new Vector3(0.25f, 0.12f, 0.25f), Color.gray);
                Object.Destroy(bulb.GetComponent<Collider>());
                bulbs.Add(bulb.GetComponent<Renderer>());
            }

            // Dim red emergency lights: just enough to navigate before power.
            emergencyLights.Add(PointLight(lights, "Emergency_Entry", new Vector3(0f, 2.6f, -6.5f), new Color(0.8f, 0.15f, 0.1f), 0.6f, 7f));
            emergencyLights.Add(PointLight(lights, "Emergency_Corridor", new Vector3(-4f, 2.6f, 4f), new Color(0.8f, 0.15f, 0.1f), 0.45f, 7f));
            emergencyLights.Add(PointLight(lights, "Emergency_Utility", new Vector3(6f, 2.6f, 12f), new Color(0.8f, 0.15f, 0.1f), 0.45f, 6f));
            emergencyLights.Add(PointLight(lights, "Emergency_Dining", new Vector3(4f, 2.6f, 3f), new Color(0.8f, 0.15f, 0.1f), 0.4f, 6f));
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
            camera.farClipPlane = 60f;
            camera.fieldOfView = 70f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            head.AddComponent<AudioListener>();

            player.AddComponent<DesktopInputAdapter>();
            player.AddComponent<DesktopMotor>();
            player.AddComponent<PlayerInteractor>();
            return player.AddComponent<PlayerController>();
        }

        // --------------------------------------------------------- interactables

        private FusePickup BuildFuse(Transform parent)
        {
            GameObject fuse = new GameObject("Fuse");
            fuse.transform.SetParent(parent, false);
            fuse.transform.position = FusePosition;

            GameObject body = Block(fuse.transform, "FuseBody", FusePosition, new Vector3(0.1f, 0.1f, 0.26f), FuseColor);
            Object.Destroy(body.GetComponent<Collider>());
            // A generous collider makes the small fuse easy to target.
            BoxCollider target = fuse.AddComponent<BoxCollider>();
            target.size = new Vector3(0.45f, 0.35f, 0.5f);
            // A faint glow so the fuse is discoverable in the dark kitchen.
            PointLight(fuse.transform, "FuseGlow", FusePosition + Vector3.up * 0.3f, FuseColor, 0.7f, 2.5f);

            return fuse.AddComponent<FusePickup>();
        }

        private FuseBox BuildFuseBox(Transform parent)
        {
            GameObject box = Block(parent, "FuseBox", FuseBoxPosition, new Vector3(0.25f, 0.6f, 0.45f), MetalColor);
            // Sibling, not child: Block() sizes by localScale, and the box is scaled.
            GameObject slot = Block(parent, "FuseBox_InstalledFuse", FuseBoxPosition + new Vector3(-0.14f, 0f, 0f),
                new Vector3(0.06f, 0.1f, 0.26f), FuseColor);
            Object.Destroy(slot.GetComponent<Collider>());

            FuseBox fuseBox = box.AddComponent<FuseBox>();
            fuseBox.Initialize(FlowController, slot);
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

            GameObject panel = Block(hinge.transform, "Panel", door.transform.position + new Vector3(0f, 1.15f, 0f),
                new Vector3(1.2f, 2.3f, 0.1f), DoorColor);
            panel.name = "Panel";
            GameObject indicator = Block(hinge.transform, "LockIndicator", door.transform.position + new Vector3(0.42f, 1.1f, 0.08f),
                new Vector3(0.08f, 0.12f, 0.04f), Color.red);
            Object.Destroy(indicator.GetComponent<Collider>());
            PointLight(hinge.transform, "LockLight", door.transform.position + new Vector3(0.42f, 1.1f, 0.3f), Color.red, 0.5f, 1.8f);

            return door.AddComponent<ExitDoor>();
        }

        // -------------------------------------------------------------- helpers

        private static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private GameObject Block(Transform parent, string name, Vector3 worldCentre, Vector3 size, Color color)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = worldCentre;
            block.transform.localScale = size;
            Renderer renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialFor(renderer, color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return block;
        }

        private Material MaterialFor(Renderer renderer, Color color)
        {
            if (baseMaterial == null)
                baseMaterial = renderer.sharedMaterial;
            if (!materials.TryGetValue(color, out Material material))
            {
                material = new Material(baseMaterial) { color = color };
                materials.Add(color, material);
            }
            return material;
        }

        private static Light PointLight(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            return light;
        }

        private void OnDestroy()
        {
            foreach (Material material in materials.Values)
                if (material != null)
                    Destroy(material);
        }
    }
}
