using UnityEngine;
using S = PHYSXR.Game.Art.HouseMaterials.Surface;

namespace PHYSXR.Game.Level
{
    // Environmental storytelling: small details that imply what happened
    // here without a word of text, for players who look.
    //
    //  - The caretaker's muddy footprints come in at the front door and stop
    //    in the middle of the hall. They do not go anywhere else.
    //  - Wet grave soil is smeared along the corridor from the locked cellar
    //    door toward the kitchen, fading as it goes: something comes up from
    //    below, and it drags.
    //  - The cellar door is gouged from this side at hand height.
    //  - Crockery lies smashed by the kitchen table, beside the fallen chair.
    //
    // Everything here is decoration: no colliders, no shadows, nothing on the
    // route waypoints, so the NavMesh, the player and the self-tests are
    // unaffected. Positions come from a seeded generator, so the house is the
    // same on every run.
    public partial class BlackthornGreybox
    {
        private uint storySeed;

        private void BuildStory(Transform parent)
        {
            storySeed = 0x5EEDu;
            Footprints(parent);
            DragMarks(parent);
            CellarGouges(parent);
            Shards(parent);
        }

        private float StoryRand()
        {
            storySeed ^= storySeed << 13;
            storySeed ^= storySeed >> 17;
            storySeed ^= storySeed << 5;
            return (storySeed & 0xFFFFFF) / 16777215f;
        }

        private void Footprints(Transform p)
        {
            // From the doormat north, drying out as they go, ending at z -4.4.
            Vector3 at = new Vector3(0.25f, 0f, -7.55f);
            for (int i = 0; i < 9; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float yaw = 4f + (StoryRand() - 0.5f) * 10f;
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 foot = at + rot * new Vector3(0.11f * side, 0f, 0f);
                // On the rug from z -6.7; lift above it there.
                foot.y = foot.z > -6.7f ? 0.0145f : 0.0025f;
                float wet = 1f - i / 10f;
                Block(p, "Footprint", foot, new Vector3(0.1f, 0.002f, 0.26f * (0.85f + 0.15f * wet)), S.WetSoil, false, rot, 0.3f, false);
                Block(p, "Heel", foot + rot * new Vector3(0f, 0f, -0.16f), new Vector3(0.085f, 0.002f, 0.08f), S.WetSoil, false, rot, 0.3f, false);
                at += rot * new Vector3(0f, 0f, 0.38f);
            }
        }

        private void DragMarks(Transform p)
        {
            // Cellar door (east wall of the corridor, z 5.0) -> kitchen doorway.
            Vector3[] path =
            {
                new Vector3(-3.05f, 0f, 5.0f), new Vector3(-3.6f, 0f, 5.6f), new Vector3(-3.95f, 0f, 6.6f),
                new Vector3(-4.05f, 0f, 7.6f), new Vector3(-4.0f, 0f, 8.6f), new Vector3(-4.1f, 0f, 9.6f)
            };
            int count = 22;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1) * (path.Length - 1);
                int seg = Mathf.Min(path.Length - 2, Mathf.FloorToInt(t));
                Vector3 a = path[seg], b = path[seg + 1];
                Vector3 pos = Vector3.Lerp(a, b, t - seg);
                Vector3 dir = (b - a).normalized;
                float fade = 1f - (float)i / count;
                if (StoryRand() > 0.35f + 0.6f * fade)
                    continue; // gaps grow as the soil runs out
                pos += new Vector3(dir.z, 0f, -dir.x) * (StoryRand() - 0.5f) * 0.25f;
                // The runner covers the corridor's middle; sit on top of it.
                bool onRunner = pos.x > -4.6f && pos.x < -3.4f && pos.z > 0.3f && pos.z < 7.7f;
                pos.y = onRunner ? 0.0145f : 0.0025f;
                float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + (StoryRand() - 0.5f) * 25f;
                Vector3 size = new Vector3(0.12f + StoryRand() * 0.22f * fade, 0.002f, 0.35f + StoryRand() * 0.5f * fade);
                Block(p, "DragMark", pos, size, S.WetSoil, false, Quaternion.Euler(0f, yaw, 0f), 0.4f, false);
            }
        }

        private void CellarGouges(Transform p)
        {
            // Four parallel scores, twice, on the corridor face of the door.
            for (int set = 0; set < 2; set++)
            {
                float z0 = set == 0 ? 4.82f : 5.12f;
                float y0 = set == 0 ? 1.28f : 0.98f;
                float tilt = set == 0 ? 14f : -9f;
                for (int k = 0; k < 4; k++)
                {
                    float len = 0.34f + StoryRand() * 0.14f;
                    Vector3 c = new Vector3(-2.866f, y0 + (StoryRand() - 0.5f) * 0.05f, z0 + k * 0.035f);
                    Block(p, "Gouge", c, new Vector3(0.004f, len, 0.009f), S.Gouge, false, Quaternion.Euler(tilt, 0f, 0f), 0.3f, false);
                }
            }
        }

        private void Shards(Transform p)
        {
            Vector3 centre = new Vector3(-7.0f, 0.004f, 13.1f);
            for (int i = 0; i < 9; i++)
            {
                Vector3 pos = centre + new Vector3((StoryRand() - 0.5f) * 0.9f, StoryRand() * 0.004f, (StoryRand() - 0.5f) * 0.7f);
                float s = 0.03f + StoryRand() * 0.06f;
                Block(p, "Shard", pos, new Vector3(s, 0.006f, s * (0.5f + StoryRand())), S.Porcelain, false,
                    Quaternion.Euler(0f, StoryRand() * 360f, 0f), 0.2f, false);
            }
        }
    }
}
