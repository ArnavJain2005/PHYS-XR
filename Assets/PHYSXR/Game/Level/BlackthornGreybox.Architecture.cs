using System.Collections.Generic;
using PHYSXR.Game.Art;
using UnityEngine;
using S = PHYSXR.Game.Art.HouseMaterials.Surface;

namespace PHYSXR.Game.Level
{
    public partial class BlackthornGreybox
    {
        // A room's footprint and the doorway gaps along each of its edges.
        private struct RoomSpec
        {
            public string name;
            public float x0, x1, z0, z1;
            public bool tiled;
            public float[] southGaps, northGaps, westGaps, eastGaps;
        }

        private static readonly RoomSpec[] Rooms =
        {
            new RoomSpec { name = "EntryHall", x0 = -8f, x1 = 8f, z0 = -8f, z1 = 0f, southGaps = new[] { 0f }, northGaps = new[] { -4f, 4f } },
            new RoomSpec { name = "Corridor", x0 = -5.3f, x1 = -2.7f, z0 = 0f, z1 = 8f, southGaps = new[] { -4f }, northGaps = new[] { -4f } },
            new RoomSpec { name = "Dining", x0 = 0f, x1 = 8f, z0 = 0f, z1 = 8f, southGaps = new[] { 4f }, northGaps = new[] { 4f } },
            new RoomSpec { name = "Kitchen", x0 = -8f, x1 = 0f, z0 = 8f, z1 = 16f, tiled = true, southGaps = new[] { -4f }, eastGaps = new[] { 12f } },
            new RoomSpec { name = "Utility", x0 = 0f, x1 = 8f, z0 = 8f, z1 = 16f, tiled = true, southGaps = new[] { 4f }, westGaps = new[] { 12f } },
        };

        private void BuildArchitecture(Transform parent)
        {
            Transform rooms = Group(parent, "Rooms");
            foreach (RoomSpec room in Rooms)
                Room(rooms, room);

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
            WallZ(walls, "Corridor_East", 0f, 8f, -2.7f);
            WallZ(walls, "Corridor_West", 0f, 8f, -5.3f);
            WallZ(walls, "Block_Dining", 0f, 8f, 0f);
            WallZ(walls, "Kitchen_Utility", 8f, 16f, 0f, 12f, DoorWidth);

            Transform trim = Group(parent, "Trim");
            foreach (RoomSpec room in Rooms)
                Linings(trim, room);
            DoorFrameX(trim, "FrontDoorFrame", 0f, -8f, 1.2f);
            DoorFrameX(trim, "Frame_EntryCorridor", -4f, 0f, DoorWidth);
            DoorFrameX(trim, "Frame_EntryDining", 4f, 0f, DoorWidth);
            DoorFrameX(trim, "Frame_CorridorKitchen", -4f, 8f, DoorWidth);
            DoorFrameX(trim, "Frame_DiningUtility", 4f, 8f, DoorWidth);
            DoorFrameZ(trim, "Frame_KitchenUtility", 0f, 12f, DoorWidth);

            Transform windows = Group(parent, "Windows");
            Window(windows, "Entry_SW", new Vector3(-5.5f, 0f, -8f), Vector3.forward);
            Window(windows, "Entry_SE", new Vector3(5.5f, 0f, -8f), Vector3.forward);
            Window(windows, "Entry_W", new Vector3(-8f, 0f, -3.2f), Vector3.right);
            Window(windows, "Kitchen_N", new Vector3(-4f, 0f, 16f), Vector3.back, 1.1f, 1.0f, 1.25f);
            Window(windows, "Kitchen_W", new Vector3(-8f, 0f, 11f), Vector3.right, 0.9f, 1.0f, 1.25f);
            Window(windows, "Utility_N", new Vector3(5.6f, 0f, 16f), Vector3.back, 0.8f, 0.7f, 1.7f);
            Window(windows, "Dining_E", new Vector3(8f, 0f, 4f), Vector3.left, 1.1f, 1.5f);

            Stairs(Group(parent, "Stairs"));
        }

        private void Room(Transform parent, RoomSpec r)
        {
            Transform room = Group(parent, r.name);
            Vector3 centre = new Vector3((r.x0 + r.x1) / 2f, 0f, (r.z0 + r.z1) / 2f);
            Vector3 size = new Vector3(r.x1 - r.x0, 0.1f, r.z1 - r.z0);
            Block(room, r.tiled ? "Floor_Tile" : "Floor", centre + Vector3.down * 0.05f, size,
                r.tiled ? S.Tiles : S.FloorBoards, true, null, r.tiled ? 2f : 2.4f, false);
            Block(room, "Ceiling", centre + Vector3.up * (WallHeight + 0.05f), size, S.Plaster, true, null, 2.5f);

            if (r.name == "EntryHall" || r.name == "Dining")
            {
                // Exposed beams across the short span.
                for (float x = r.x0 + 2f; x < r.x1 - 0.5f; x += 2.6f)
                    Block(room, "Beam", new Vector3(x, WallHeight - 0.12f, centre.z), new Vector3(0.18f, 0.24f, r.z1 - r.z0 - 0.2f), S.DarkWood, false);
            }
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
                new Vector3(footprint.x, WallHeight, footprint.z), S.Wallpaper, true, null, 1.2f);
        }

        private void Lintel(Transform parent, string name, Vector3 baseCentre, Vector3 footprint)
        {
            float h = WallHeight - DoorHeight;
            Block(parent, name, baseCentre + Vector3.up * (DoorHeight + h / 2f), new Vector3(footprint.x, h, footprint.z), S.Wallpaper, true, null, 1.2f);
        }

        // Wainscot, chair rail, baseboard and crown along each inner edge of a
        // room, broken at its doorways. Purely visual: no colliders.
        private void Linings(Transform parent, RoomSpec r)
        {
            Transform group = Group(parent, r.name + "_Lining");
            const float inset = WallThickness / 2f;
            EdgeX(group, r, r.z0 + inset, 1f, r.southGaps);
            EdgeX(group, r, r.z1 - inset, -1f, r.northGaps);
            EdgeZ(group, r, r.x0 + inset, 1f, r.westGaps);
            EdgeZ(group, r, r.x1 - inset, -1f, r.eastGaps);
        }

        private void EdgeX(Transform parent, RoomSpec r, float faceZ, float inward, float[] gaps)
        {
            foreach (Vector2 span in Spans(r.x0 + 0.1f, r.x1 - 0.1f, gaps, r.name == "EntryHall" && inward > 0f ? 1.2f : DoorWidth))
            {
                float length = span.y - span.x;
                if (length < 0.05f) continue;
                float cx = (span.x + span.y) / 2f;
                LiningPieces(parent, new Vector3(cx, 0f, faceZ), new Vector3(0f, 0f, inward), new Vector3(length, 0f, 0f), r.tiled);
            }
        }

        private void EdgeZ(Transform parent, RoomSpec r, float faceX, float inward, float[] gaps)
        {
            foreach (Vector2 span in Spans(r.z0 + 0.1f, r.z1 - 0.1f, gaps, DoorWidth))
            {
                float length = span.y - span.x;
                if (length < 0.05f) continue;
                float cz = (span.x + span.y) / 2f;
                LiningPieces(parent, new Vector3(faceX, 0f, cz), new Vector3(inward, 0f, 0f), new Vector3(0f, 0f, length), r.tiled);
            }
        }

        private void LiningPieces(Transform parent, Vector3 face, Vector3 inward, Vector3 along, bool tiled)
        {
            Vector3 thin(float depth) => new Vector3(Mathf.Max(along.x, Mathf.Abs(inward.x) * depth), 0f, Mathf.Max(along.z, Mathf.Abs(inward.z) * depth));
            Vector3 size(float depth, float height) { Vector3 s = thin(depth); s.y = height; return s; }

            float wainscot = tiled ? 1.2f : 1.0f;
            Block(parent, "Wainscot", face + inward * 0.012f + Vector3.up * (wainscot / 2f), size(0.024f, wainscot),
                tiled ? S.Tiles : S.Wainscot, false, null, tiled ? 1.2f : 1f, false);
            Block(parent, "ChairRail", face + inward * 0.035f + Vector3.up * wainscot, size(0.05f, 0.06f), S.Trim, false, null, 1f, false);
            Block(parent, "Baseboard", face + inward * 0.03f + Vector3.up * 0.07f, size(0.06f, 0.14f), S.Trim, false, null, 1f, false);
            Block(parent, "Crown", face + inward * 0.04f + Vector3.up * (WallHeight - 0.06f), size(0.08f, 0.12f), S.Trim, false, null, 1f, false);
        }

        // [from, to] minus the gaps (each gap centred on a coordinate).
        private static List<Vector2> Spans(float from, float to, float[] gaps, float gapWidth)
        {
            var spans = new List<Vector2>();
            float start = from;
            if (gaps != null)
            {
                var sorted = new List<float>(gaps);
                sorted.Sort();
                foreach (float g in sorted)
                {
                    float a = g - gapWidth / 2f - 0.06f;
                    float b = g + gapWidth / 2f + 0.06f;
                    if (a > start)
                        spans.Add(new Vector2(start, a));
                    start = Mathf.Max(start, b);
                }
            }
            if (to > start)
                spans.Add(new Vector2(start, to));
            return spans;
        }

        // Jambs and header around a doorway in a wall along X (no colliders, so
        // the walkable width and the NavMesh stay the full gap).
        private void DoorFrameX(Transform parent, string name, float x, float z, float width)
        {
            float depth = WallThickness + 0.08f;
            Block(parent, name + "_L", new Vector3(x - width / 2f - 0.02f, DoorHeight / 2f, z), new Vector3(0.1f, DoorHeight, depth), S.Trim, false);
            Block(parent, name + "_R", new Vector3(x + width / 2f + 0.02f, DoorHeight / 2f, z), new Vector3(0.1f, DoorHeight, depth), S.Trim, false);
            Block(parent, name + "_Head", new Vector3(x, DoorHeight + 0.06f, z), new Vector3(width + 0.3f, 0.14f, depth), S.Trim, false);
        }

        private void DoorFrameZ(Transform parent, string name, float x, float z, float width)
        {
            float depth = WallThickness + 0.08f;
            Block(parent, name + "_L", new Vector3(x, DoorHeight / 2f, z - width / 2f - 0.02f), new Vector3(depth, DoorHeight, 0.1f), S.Trim, false);
            Block(parent, name + "_R", new Vector3(x, DoorHeight / 2f, z + width / 2f + 0.02f), new Vector3(depth, DoorHeight, 0.1f), S.Trim, false);
            Block(parent, name + "_Head", new Vector3(x, DoorHeight + 0.06f, z), new Vector3(depth, 0.14f, width + 0.3f), S.Trim, false);
        }

        // A window on the inner face of an outer wall: a dark storm pane (its
        // material flashes with lightning), frame, mullions, sill and curtains.
        // wallCentre is on the wall's centre line; inward points into the room.
        private void Window(Transform parent, string name, Vector3 wallCentre, Vector3 inward,
            float width = 1.0f, float height = 1.4f, float sill = 0.9f)
        {
            Quaternion rot = Quaternion.LookRotation(inward);
            Vector3 right = rot * Vector3.right;
            Vector3 face = wallCentre + inward * (WallThickness / 2f);
            Vector3 mid = face + Vector3.up * (sill + height / 2f);
            Transform w = Group(parent, name);

            Block(w, "Pane", mid + inward * 0.012f, new Vector3(width, height, 0.02f), S.WindowStorm, false, rot);
            Block(w, "FrameTop", mid + inward * 0.04f + Vector3.up * (height / 2f + 0.04f), new Vector3(width + 0.16f, 0.08f, 0.06f), S.Trim, false, rot);
            Block(w, "FrameBottom", mid + inward * 0.04f - Vector3.up * (height / 2f + 0.04f), new Vector3(width + 0.16f, 0.08f, 0.06f), S.Trim, false, rot);
            Block(w, "FrameL", mid + inward * 0.04f - right * (width / 2f + 0.04f), new Vector3(0.08f, height, 0.06f), S.Trim, false, rot);
            Block(w, "FrameR", mid + inward * 0.04f + right * (width / 2f + 0.04f), new Vector3(0.08f, height, 0.06f), S.Trim, false, rot);
            Block(w, "MullionV", mid + inward * 0.03f, new Vector3(0.035f, height, 0.03f), S.Trim, false, rot);
            Block(w, "MullionH", mid + inward * 0.03f + Vector3.up * (height * 0.1f), new Vector3(width, 0.035f, 0.03f), S.Trim, false, rot);
            Block(w, "Sill", face + inward * 0.08f + Vector3.up * (sill - 0.06f), new Vector3(width + 0.24f, 0.05f, 0.16f), S.Trim, false, rot);

            float curtainTop = Mathf.Min(WallHeight - 0.25f, sill + height + 0.35f);
            float curtainH = curtainTop - 0.25f;
            Block(w, "CurtainL", face + inward * 0.1f - right * (width / 2f + 0.12f) + Vector3.up * (0.25f + curtainH / 2f),
                new Vector3(0.34f, curtainH, 0.05f), S.Fabric, false, rot, 0.8f);
            Block(w, "CurtainR", face + inward * 0.1f + right * (width / 2f + 0.12f) + Vector3.up * (0.25f + curtainH / 2f),
                new Vector3(0.34f, curtainH, 0.05f), S.Fabric, false, rot, 0.8f);
            Block(w, "Rod", face + inward * 0.12f + Vector3.up * (curtainTop + 0.03f), new Vector3(width + 0.8f, 0.03f, 0.03f), S.Brass, false, rot);

#if UNITY_EDITOR
            if (drawingGizmos)
                return;
#endif
            // Moonlight and lightning through the glass: a cold spot light,
            // a dim spill between strikes (StormController), flaring with the
            // storm. The larger windows in the main sightlines cast shadows,
            // so a strike throws the frame and mullions across the floor.
            var flash = new GameObject("StormFlash");
            flash.transform.SetParent(w, false);
            flash.transform.SetPositionAndRotation(face + Vector3.up * (sill + height * 0.8f) - inward * 0.05f,
                Quaternion.LookRotation(inward + Vector3.down * 0.45f));
            Light light = flash.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 105f;
            light.range = 8f;
            light.intensity = 0f;
            light.color = new Color(0.72f, 0.8f, 1f);
            bool shadowed = !PresentationProfile.Lightweight && (name == "Entry_SW" || name == "Kitchen_N" || name == "Dining_E");
            light.shadows = shadowed ? LightShadows.Soft : LightShadows.None;
            light.shadowNearPlane = 0.1f;
            light.enabled = false;
            stormLights.Add(light);
        }

        // The staircase to the upper floor, boarded up: visible, not usable.
        private void Stairs(Transform parent)
        {
            const int steps = 10;
            const float rise = 0.26f, run = 0.45f, x = 7.05f, width = 1.5f, z0 = -6.6f;
            for (int i = 0; i < steps; i++)
            {
                float h = rise * (i + 1);
                float z = z0 + run * i + run / 2f;
                Block(parent, "Step" + i, new Vector3(x, h / 2f, z), new Vector3(width, h, run), S.DarkWood, false, null, 1f);
                Block(parent, "Tread" + i, new Vector3(x, h + 0.01f, z - 0.02f), new Vector3(width + 0.04f, 0.03f, run + 0.04f), S.Trim, false);
            }

            for (int i = 0; i < steps; i += 2)
            {
                float h = rise * (i + 1);
                float z = z0 + run * i + run / 2f;
                Block(parent, "Baluster" + i, new Vector3(x - width / 2f - 0.04f, h + 0.45f, z), new Vector3(0.05f, 0.9f, 0.05f), S.Trim, false);
            }

            float railLength = Mathf.Sqrt((run * steps) * (run * steps) + (rise * steps) * (rise * steps));
            float slope = Mathf.Atan2(rise * steps, run * steps) * Mathf.Rad2Deg;
            Block(parent, "Rail", new Vector3(x - width / 2f - 0.04f, rise * steps / 2f + 0.95f, z0 + run * steps / 2f),
                new Vector3(0.07f, 0.07f, railLength), S.Trim, false, Quaternion.Euler(-slope, 0f, 0f));
            Block(parent, "Newel", new Vector3(x - width / 2f - 0.04f, 0.6f, z0 - 0.05f), new Vector3(0.12f, 1.2f, 0.12f), S.DarkWood, false);

            // The dark stairwell opening above.
            Block(parent, "Stairwell", new Vector3(x, WallHeight - 0.005f, -3.0f), new Vector3(width + 0.1f, 0.01f, 2.4f), S.Black, false);

            // Boards nailed across the bottom of the flight.
            for (int i = 0; i < 3; i++)
            {
                float y = 0.5f + i * 0.45f;
                float roll = (i % 2 == 0 ? 1f : -1f) * (12f + i * 4f);
                Block(parent, "Board" + i, new Vector3(x, y, z0 - 0.12f), new Vector3(width + 0.3f, 0.14f, 0.03f), S.DarkWood, false,
                    Quaternion.Euler(0f, 0f, roll));
            }

            // One invisible blocker keeps the player and the ghost off the flight.
#if UNITY_EDITOR
            if (drawingGizmos)
                return;
#endif
            var blocker = new GameObject("StairBlocker");
            blocker.transform.SetParent(parent, false);
            blocker.transform.position = new Vector3(x, WallHeight / 2f, z0 + run * steps / 2f - 0.1f);
            blocker.AddComponent<BoxCollider>().size = new Vector3(width + 0.3f, WallHeight, run * steps + 0.4f);
        }
    }
}
