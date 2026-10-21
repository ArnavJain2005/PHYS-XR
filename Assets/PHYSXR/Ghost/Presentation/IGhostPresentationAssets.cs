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
        LanternRecoil
    }

    // What the ghost's presentation needs from the game: sounds and
    // materials. The game implements it (HollowAssets); the Ghost layer never
    // references game code, keeping the dependency direction Game -> Ghost.
    public interface IGhostPresentationAssets
    {
        AudioClip Clip(GhostSound sound, out float volume);

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
