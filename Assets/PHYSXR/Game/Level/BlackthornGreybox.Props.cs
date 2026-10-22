using PHYSXR.Game.Art;
using UnityEngine;
using S = PHYSXR.Game.Art.HouseMaterials.Surface;

namespace PHYSXR.Game.Level
{
    // Furniture and set dressing. Every collider is kept clear of the route
    // waypoints (BlackthornGreybox.Route*) and of the doorways, so the player,
    // the scripted self-test and the ghost's NavMesh all keep their paths.
    public partial class BlackthornGreybox
    {
        private void BuildProps(Transform parent)
        {
            Transform props = Group(parent, "Props");
            EntryHall(Group(props, "EntryHall"));
            Corridor(Group(props, "Corridor"));
            Kitchen(Group(props, "Kitchen"));
            Utility(Group(props, "Utility"));
            Dining(Group(props, "Dining"));
            BuildStory(Group(props, "Story"));
        }

        // ------------------------------------------------------------- rooms

        private void EntryHall(Transform p)
        {
            Block(p, "Rug", new Vector3(0f, 0.006f, -4.2f), new Vector3(4f, 0.012f, 5f), S.Rug, false, null, 4f, false);

            // Grandfather clock (a landmark; it ticks).
            Block(p, "Clock_Case", new Vector3(-7.62f, 1.05f, -6.0f), new Vector3(0.42f, 2.1f, 0.5f), S.DarkWood);
            Block(p, "Clock_Hood", new Vector3(-7.6f, 2.18f, -6.0f), new Vector3(0.5f, 0.16f, 0.58f), S.DarkWood, false);
            Block(p, "Clock_Face", new Vector3(-7.4f, 1.78f, -6.0f), new Vector3(0.02f, 0.32f, 0.32f), S.Porcelain, false);
            Block(p, "Clock_Hands", new Vector3(-7.385f, 1.8f, -6.0f), new Vector3(0.01f, 0.12f, 0.015f), S.Black, false,
                Quaternion.Euler(33f, 0f, 0f));
            Block(p, "Clock_Window", new Vector3(-7.4f, 1.0f, -6.0f), new Vector3(0.02f, 0.7f, 0.24f), S.Glass, false);
            Block(p, "Clock_Pendulum", new Vector3(-7.45f, 0.95f, -6.0f), new Vector3(0.01f, 0.5f, 0.06f), S.Brass, false);

            // Bench and coat hooks under the west window.
            Block(p, "Bench_Seat", new Vector3(-7.55f, 0.44f, -2.8f), new Vector3(0.45f, 0.06f, 1.6f), S.DarkWood);
            Block(p, "Bench_Base", new Vector3(-7.55f, 0.2f, -2.8f), new Vector3(0.4f, 0.4f, 1.5f), S.DarkWood, false);
            Block(p, "CoatRail", new Vector3(-7.86f, 1.75f, -0.9f), new Vector3(0.05f, 0.08f, 0.9f), S.Trim, false);
            Block(p, "Coat", new Vector3(-7.78f, 1.25f, -1.05f), new Vector3(0.14f, 0.95f, 0.45f), S.Fabric, false, Quaternion.Euler(0f, 0f, 4f));

            // Console table by the front door, with a vase and a photograph.
            Block(p, "Console_Top", new Vector3(-3.0f, 0.82f, -7.72f), new Vector3(1.4f, 0.05f, 0.38f), S.DarkWood);
            Block(p, "Console_Body", new Vector3(-3.0f, 0.55f, -7.74f), new Vector3(1.3f, 0.3f, 0.34f), S.DarkWood, false);
            Legs(p, new Vector3(-3.0f, 0f, -7.74f), new Vector2(1.25f, 0.28f), 0.82f);
            Cylinder(p, "Vase", new Vector3(-3.45f, 1.02f, -7.72f), 0.08f, 0.36f, S.Porcelain, false, null, 0.05f);
            Block(p, "Photo", new Vector3(-2.7f, 0.98f, -7.78f), new Vector3(0.22f, 0.28f, 0.02f), S.Portrait, false, Quaternion.Euler(-12f, 0f, 0f));

            Cylinder(p, "UmbrellaStand", new Vector3(1.25f, 0.3f, -7.6f), 0.14f, 0.6f, S.Brass);
            Block(p, "Umbrella", new Vector3(1.22f, 0.7f, -7.58f), new Vector3(0.05f, 0.9f, 0.05f), S.Black, false, Quaternion.Euler(6f, 0f, -8f));

            // Portraits.
            Painting(p, "Portrait_W", new Vector3(-7.89f, 1.85f, -4.6f), Vector3.right, 0.9f, 1.2f, S.Portrait);
            Painting(p, "Landscape_N", new Vector3(-1.4f, 1.8f, -0.11f), Vector3.back, 1.2f, 0.8f, S.Landscape);
            Painting(p, "Portrait_S", new Vector3(3.2f, 1.8f, -7.89f), Vector3.forward, 0.7f, 0.9f, S.Portrait);

            // A picture that has come off its nail.
            Block(p, "FallenFrame", new Vector3(-2.6f, 0.03f, -1.6f), new Vector3(0.7f, 0.04f, 0.55f), S.Trim, false, Quaternion.Euler(0f, 28f, 0f));
            Block(p, "FallenCanvas", new Vector3(-2.6f, 0.055f, -1.6f), new Vector3(0.6f, 0.01f, 0.45f), S.Landscape, false, Quaternion.Euler(0f, 28f, 0f));

            // Side table by the dining door.
            Block(p, "SideTable", new Vector3(6.9f, 0.4f, 0.0f + -0.6f), new Vector3(0.5f, 0.8f, 0.5f), S.DarkWood);
        }

        private void Corridor(Transform p)
        {
            Block(p, "Runner", new Vector3(-4f, 0.006f, 4f), new Vector3(1.15f, 0.012f, 7.4f), S.Runner, false, null, 1.2f, false);

            // Telephone table on the east wall.
            Block(p, "PhoneTable_Top", new Vector3(-2.98f, 0.75f, 2.2f), new Vector3(0.36f, 0.04f, 0.8f), S.DarkWood);
            Legs(p, new Vector3(-2.98f, 0f, 2.2f), new Vector2(0.3f, 0.7f), 0.75f);
            Block(p, "Phone_Base", new Vector3(-2.98f, 0.82f, 2.1f), new Vector3(0.2f, 0.1f, 0.24f), S.Black, false);
            Block(p, "Phone_Handset", new Vector3(-2.98f, 0.9f, 2.1f), new Vector3(0.06f, 0.05f, 0.26f), S.Black, false);
            Block(p, "Phone_Cord", new Vector3(-3.05f, 0.45f, 2.38f), new Vector3(0.01f, 0.7f, 0.01f), S.Black, false, Quaternion.Euler(14f, 0f, 0f));

            // Crooked frames.
            Painting(p, "Corridor_Portrait1", new Vector3(-5.19f, 1.75f, 2.0f), Vector3.right, 0.5f, 0.65f, S.Portrait, 6f);
            Painting(p, "Corridor_Portrait2", new Vector3(-2.81f, 1.8f, 3.4f), Vector3.left, 0.55f, 0.7f, S.Portrait, -9f);
            Painting(p, "Corridor_Landscape", new Vector3(-5.19f, 1.8f, 6.4f), Vector3.right, 0.7f, 0.45f, S.Landscape, -3f);

            // A small chair someone left in the way, pushed against the wall.
            Chair(p, new Vector3(-5.0f, 0f, 7.2f), 90f, false);
        }

        private void Kitchen(Transform p)
        {
            // North run: base cabinets, worktop, sink below the window, stove.
            Block(p, "Base_N", new Vector3(-4.3f, 0.43f, 15.62f), new Vector3(7.0f, 0.86f, 0.56f), S.PaintedWood, true, null, 1f);
            Block(p, "Worktop_N", new Vector3(-4.3f, 0.885f, 15.58f), new Vector3(7.1f, 0.05f, 0.66f), S.Stone, true);
            for (float x = -7.4f; x < -1f; x += 0.7f)
                Block(p, "Door_N", new Vector3(x + 0.35f, 0.45f, 15.33f), new Vector3(0.62f, 0.7f, 0.02f), S.Trim, false);
            Block(p, "Sink", new Vector3(-4f, 0.86f, 15.55f), new Vector3(0.8f, 0.06f, 0.45f), S.Metal, false);
            Cylinder(p, "Tap", new Vector3(-4f, 1.05f, 15.82f), 0.02f, 0.3f, S.Metal, false);
            Block(p, "Stove", new Vector3(-6.5f, 0.46f, 15.6f), new Vector3(0.75f, 0.92f, 0.62f), S.Black);
            for (int i = 0; i < 4; i++)
                Cylinder(p, "Hob" + i, new Vector3(-6.68f + (i % 2) * 0.36f, 0.93f, 15.48f + (i / 2) * 0.24f), 0.09f, 0.02f, S.Metal, false);
            Block(p, "PotRail", new Vector3(-6.5f, 1.95f, 15.85f), new Vector3(1.0f, 0.03f, 0.03f), S.Brass, false);
            Cylinder(p, "Pot1", new Vector3(-6.75f, 1.7f, 15.8f), 0.12f, 0.16f, S.Metal, false);
            Cylinder(p, "Pot2", new Vector3(-6.3f, 1.66f, 15.8f), 0.1f, 0.22f, S.Metal, false);

            // Upper cabinets, leaving the window clear.
            Block(p, "Upper_NW", new Vector3(-6.1f, 1.95f, 15.8f), new Vector3(2.6f, 0.75f, 0.36f), S.PaintedWood, false, null, 1f);
            Block(p, "Upper_NE", new Vector3(-2.0f, 1.95f, 15.8f), new Vector3(2.4f, 0.75f, 0.36f), S.PaintedWood, false, null, 1f);
            Block(p, "Upper_Open", new Vector3(-1.4f, 1.95f, 15.6f), new Vector3(0.6f, 0.7f, 0.02f), S.Trim, false, Quaternion.Euler(0f, 70f, 0f));

            // West run under the side window.
            Block(p, "Base_W", new Vector3(-7.62f, 0.43f, 10.8f), new Vector3(0.56f, 0.86f, 3.4f), S.PaintedWood, true, null, 1f);
            Block(p, "Worktop_W", new Vector3(-7.58f, 0.885f, 10.8f), new Vector3(0.66f, 0.05f, 3.5f), S.Stone, true);
            Block(p, "Breadbin", new Vector3(-7.62f, 1.0f, 9.5f), new Vector3(0.3f, 0.18f, 0.4f), S.Metal, false);

            // Fridge in the south-east corner.
            Block(p, "Fridge", new Vector3(-0.5f, 0.92f, 8.95f), new Vector3(0.68f, 1.84f, 0.68f), S.Porcelain);
            Block(p, "FridgeHandle", new Vector3(-0.84f, 1.2f, 9.18f), new Vector3(0.03f, 0.4f, 0.04f), S.Metal, false);

            // The table the fuse lies on, with chairs (one knocked over).
            Table(p, "KitchenTable", new Vector3(-6.4f, 0f, 14.4f), new Vector3(1.4f, 0.84f, 0.9f), S.DarkWood);
            Chair(p, new Vector3(-6.75f, 0f, 13.55f), 0f, false);
            Chair(p, new Vector3(-6.05f, 0f, 13.55f), 10f, false);
            Chair(p, new Vector3(-7.45f, 0f, 13.4f), 0f, true);
            Cylinder(p, "Mug", new Vector3(-6.0f, 0.9f, 14.65f), 0.04f, 0.1f, S.Porcelain, false);
            Block(p, "Plate", new Vector3(-6.8f, 0.85f, 14.2f), new Vector3(0.24f, 0.015f, 0.24f), S.Porcelain, false);

            // Shelf with jars on the east wall.
            Block(p, "JarShelf", new Vector3(-0.2f, 1.5f, 14.4f), new Vector3(0.24f, 0.03f, 1.2f), S.DarkWood, false);
            for (int i = 0; i < 4; i++)
                Cylinder(p, "Jar" + i, new Vector3(-0.2f, 1.62f, 13.95f + i * 0.3f), 0.06f, 0.2f + (i % 2) * 0.06f, S.Glass, false);
        }

        private void Utility(Transform p)
        {
            // Workbench along the north wall.
            Block(p, "Bench_Top", new Vector3(2.5f, 0.88f, 15.62f), new Vector3(3.0f, 0.06f, 0.62f), S.DarkWood);
            Legs(p, new Vector3(2.5f, 0f, 15.62f), new Vector2(2.9f, 0.52f), 0.86f);
            Block(p, "Bench_Shelf", new Vector3(2.5f, 0.25f, 15.62f), new Vector3(2.9f, 0.04f, 0.52f), S.DarkWood, false);
            Block(p, "Toolbox", new Vector3(1.6f, 1.0f, 15.6f), new Vector3(0.5f, 0.2f, 0.26f), S.LockRed, false);
            Block(p, "Vise", new Vector3(3.7f, 0.98f, 15.4f), new Vector3(0.18f, 0.14f, 0.2f), S.Metal, false);
            Block(p, "Pegboard", new Vector3(2.5f, 1.65f, 15.88f), new Vector3(2.4f, 0.9f, 0.02f), S.Plaster, false);
            for (int i = 0; i < 5; i++)
                Block(p, "Tool" + i, new Vector3(1.6f + i * 0.45f, 1.65f, 15.86f), new Vector3(0.05f, 0.35f, 0.03f), S.Metal, false,
                    Quaternion.Euler(0f, 0f, (i - 2) * 9f));

            // Shelving with boxes.
            Block(p, "Shelving", new Vector3(5.5f, 1.0f, 15.62f), new Vector3(1.8f, 2.0f, 0.5f), S.Metal, true, null, 1f);
            for (int i = 0; i < 3; i++)
                Block(p, "Box" + i, new Vector3(5.0f + i * 0.5f, 0.55f + i % 2 * 0.7f, 15.45f), new Vector3(0.42f, 0.3f, 0.36f), S.Paper, false);

            // Water heater, washing machine, pipes.
            Cylinder(p, "Heater", new Vector3(7.35f, 0.9f, 15.15f), 0.4f, 1.8f, S.Metal);
            Block(p, "Washer", new Vector3(7.4f, 0.45f, 9.0f), new Vector3(0.62f, 0.9f, 0.62f), S.Porcelain);
            Cylinder(p, "Washer_Door", new Vector3(7.08f, 0.5f, 9.0f), 0.18f, 0.03f, S.Glass, false, Quaternion.Euler(0f, 0f, 90f));
            Cylinder(p, "Pipe1", new Vector3(7.82f, 2.65f, 12f), 0.04f, 7.6f, S.Metal, false, Quaternion.Euler(90f, 0f, 0f));
            Cylinder(p, "Pipe2", new Vector3(7.82f, 2.5f, 12f), 0.03f, 7.6f, S.Metal, false, Quaternion.Euler(90f, 0f, 0f));

            Cylinder(p, "Bucket", new Vector3(1.0f, 0.18f, 9.0f), 0.17f, 0.36f, S.Metal, true, null, 0.2f);
            Block(p, "Mop", new Vector3(0.75f, 0.75f, 8.75f), new Vector3(0.03f, 1.5f, 0.03f), S.DarkWood, false, Quaternion.Euler(10f, 0f, 12f));
        }

        private void Dining(Transform p)
        {
            Block(p, "Rug", new Vector3(6.0f, 0.006f, 4.0f), new Vector3(2.6f, 0.012f, 4.6f), S.Rug, false, null, 3f, false);
            Table(p, "DiningTable", new Vector3(6.0f, 0f, 4.0f), new Vector3(1.2f, 0.78f, 3.2f), S.DarkWood);
            for (int i = 0; i < 3; i++)
            {
                Chair(p, new Vector3(5.05f, 0f, 3.0f + i), 90f, false);
                Chair(p, new Vector3(6.95f, 0f, 3.0f + i), -90f, false);
            }
            Chair(p, new Vector3(6.0f, 0f, 1.95f), 0f, false);
            Chair(p, new Vector3(6.0f, 0f, 6.05f), 180f, false);

            // Places set for a dinner nobody ate.
            for (int i = 0; i < 3; i++)
            {
                Block(p, "PlateW" + i, new Vector3(5.75f, 0.79f, 3.0f + i), new Vector3(0.24f, 0.012f, 0.24f), S.Porcelain, false);
                Block(p, "PlateE" + i, new Vector3(6.25f, 0.79f, 3.0f + i), new Vector3(0.24f, 0.012f, 0.24f), S.Porcelain, false);
            }

            // Sideboard on the west wall, portraits above.
            Block(p, "Sideboard", new Vector3(0.38f, 0.48f, 4.0f), new Vector3(0.52f, 0.96f, 2.4f), S.DarkWood, true, null, 1f);
            Block(p, "Sideboard_Top", new Vector3(0.38f, 0.98f, 4.0f), new Vector3(0.58f, 0.04f, 2.5f), S.Trim, false);
            Painting(p, "Dining_Portrait1", new Vector3(0.11f, 1.9f, 2.6f), Vector3.right, 0.7f, 0.95f, S.Portrait);
            Painting(p, "Dining_Portrait2", new Vector3(0.11f, 1.9f, 5.4f), Vector3.right, 0.7f, 0.95f, S.Portrait, 4f);
            Painting(p, "Dining_Landscape", new Vector3(7.89f, 1.95f, 6.5f), Vector3.left, 1.0f, 0.65f, S.Landscape);
        }

        // ----------------------------------------------------------- helpers

        private void Legs(Transform p, Vector3 centre, Vector2 span, float height)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * span.x / 2f;
                float z = (i < 2 ? -1f : 1f) * span.y / 2f;
                Block(p, "Leg", centre + new Vector3(x, height / 2f, z), new Vector3(0.05f, height, 0.05f), S.DarkWood, false);
            }
        }

        // size: x = width, y = top height, z = depth. One collider for the whole table.
        private void Table(Transform p, string name, Vector3 floorCentre, Vector3 size, S surface)
        {
            Block(p, name + "_Top", floorCentre + Vector3.up * (size.y - 0.03f), new Vector3(size.x, 0.06f, size.z), surface, false, null, 1f);
            Block(p, name + "_Apron", floorCentre + Vector3.up * (size.y - 0.12f), new Vector3(size.x - 0.12f, 0.12f, size.z - 0.12f), surface, false, null, 1f);
            Legs(p, floorCentre, new Vector2(size.x - 0.12f, size.z - 0.12f), size.y - 0.06f);
#if UNITY_EDITOR
            if (drawingGizmos)
                return;
#endif
            var col = new GameObject(name + "_Collider");
            col.transform.SetParent(p, false);
            col.transform.position = floorCentre + Vector3.up * (size.y / 2f);
            col.AddComponent<BoxCollider>().size = new Vector3(size.x, size.y, size.z);
        }

        private void Chair(Transform p, Vector3 floor, float yaw, bool fallen)
        {
            Quaternion rot = fallen ? Quaternion.Euler(-90f, yaw, 0f) : Quaternion.Euler(0f, yaw, 0f);
            Vector3 lift = fallen ? new Vector3(0f, 0.22f, 0f) : Vector3.zero;
            Vector3 Local(Vector3 v) => floor + lift + rot * v;

            Block(p, "Chair_Seat", Local(new Vector3(0f, 0.45f, 0f)), new Vector3(0.44f, 0.05f, 0.44f), S.DarkWood, true, rot);
            Block(p, "Chair_Back", Local(new Vector3(0f, 0.72f, -0.2f)), new Vector3(0.44f, 0.5f, 0.04f), S.DarkWood, false, rot);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -0.18f : 0.18f);
                float z = (i < 2 ? -0.18f : 0.18f);
                Block(p, "Chair_Leg", Local(new Vector3(x, 0.22f, z)), new Vector3(0.04f, 0.44f, 0.04f), S.DarkWood, false, rot);
            }
        }

        // A framed picture on a wall face. wallFace is on the wall surface;
        // inward points into the room; tilt rolls the frame (crooked frames).
        private void Painting(Transform p, string name, Vector3 wallFace, Vector3 inward, float width, float height, S canvas, float tilt = 0f)
        {
            Quaternion rot = Quaternion.LookRotation(inward) * Quaternion.Euler(0f, 0f, tilt);
            Block(p, name + "_Frame", wallFace + inward * 0.02f, new Vector3(width + 0.1f, height + 0.1f, 0.04f), S.Trim, false, rot);
            Block(p, name + "_Canvas", wallFace + inward * 0.045f, new Vector3(width, height, 0.01f), canvas, false, rot);
        }
    }
}
