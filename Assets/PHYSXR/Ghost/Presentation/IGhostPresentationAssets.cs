using UnityEngine;

namespace PHYSXR.Ghost
{
    public enum GhostSound
    {
        Breath,
        Whisper,
        Screech,
        Thud,
        Hiss,
        Manifest,
        AttackRise,
        StrikeImpact,
        StrikeWhiff,
        ProtectHum,
        LanternRecoil,
        Presence,
        Drag,
        Scream,
        Inhale,
        Vanish
    }

    // The modelled body parts of the figure. Each mesh is authored in the
    // local space of the HollowVisual transform it hangs from (see
    // Tools/blender/build_hollow.py).
    public enum GhostBodyPart
    {
        Head,
        Hood,
        Neck,
        Torso,
        Robe,
        UpperArm,
        Forearm
    }

    // What the ghost's presentation needs from the game: sounds, materials
    // and meshes. The game implements it (HollowAssets); the Ghost layer never
    // references game code, keeping the dependency direction Game -> Ghost.
    public interface IGhostPresentationAssets
    {
        AudioClip Clip(GhostSound sound, out float volume);

        // Variation `variant` (0 .. Variants - 1) of a sound. Variations are
        // the same voice, so repeats differ without sounding like another
        // creature.
        AudioClip Clip(GhostSound sound, int variant, out float volume);
        int Variants(GhostSound sound);

        // Shared asset, not owned by the caller. Null when the part is not
        // available; the figure then falls back to primitive shapes.
        Mesh BodyMesh(GhostBodyPart part);

        // Owned by the caller: the ghost destroys these when it is destroyed.
        Material CreateShroudMaterial();
        Material CreateSkinMaterial();
        Material CreateEyeMaterial();
        Material CreateWispMaterial();
    }

    // Where the ghost goes for each behaviour (spec section D, "anchors").
    public class GhostAnchors
    {
        public Vector3[] Investigate = new Vector3[0];
        public Vector3[] Retreat = new Vector3[0];

        // Guard positions by objective phase: before power (fuse room), then
        // the exit hall.
        public Vector3 GuardBeforePower;
        public Vector3 GuardAfterPower;

        public Vector3 Spawn;
        public Vector3 CheckpointSpawn;
        public Vector3 FirstSighting;
        public Vector3 EncounterManifest;
    }
}
