using PHYSXR.Game.Audio;
using PHYSXR.Ghost;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // The game's answer to the ghost's presentation needs: procedural sounds
    // (or local overrides) and material instances built from the shared
    // templates. Materials are new instances each call; the ghost owns and
    // destroys them.
    public sealed class HollowAssets : IGhostPresentationAssets
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        public AudioClip Clip(GhostSound sound, out float volume)
        {
            return GameAudio.Clip(Map(sound), 0, out volume);
        }

        private static AudioCue Map(GhostSound sound)
        {
            switch (sound)
            {
                case GhostSound.Breath: return AudioCue.GhostBreath;
                case GhostSound.Whisper: return AudioCue.GhostWhisper;
                case GhostSound.Screech: return AudioCue.GhostScreech;
                case GhostSound.Thud: return AudioCue.GhostThud;
                case GhostSound.Hiss: return AudioCue.GhostHiss;
                case GhostSound.Manifest: return AudioCue.GhostManifest;
                case GhostSound.AttackRise: return AudioCue.AttackRise;
                case GhostSound.StrikeImpact: return AudioCue.StrikeImpact;
                case GhostSound.StrikeWhiff: return AudioCue.StrikeWhiff;
                case GhostSound.ProtectHum: return AudioCue.ProtectHum;
                default: return AudioCue.GhostHiss;
            }
        }

        public Material CreateShroudMaterial()
        {
            Material m = HouseMaterials.NewLit(new Color(0.025f, 0.025f, 0.03f), 0.05f);
            m.SetTexture("_BaseMap", ProceduralTextures.Get(ProceduralTextures.Id.Fabric));
            m.SetFloat(CullId, 0f);
            return m;
        }

        public Material CreateSkinMaterial()
        {
            Material m = HouseMaterials.NewLit(new Color(0.11f, 0.12f, 0.115f), 0.25f);
            return m;
        }

        public Material CreateEyeMaterial()
        {
            Material m = new Material(HouseMaterials.Get(HouseMaterials.Surface.BulbOn));
            m.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f));
            m.SetColor(EmissionColorId, new Color(2.2f, 2.6f, 3.2f));
            return m;
        }

        public Material CreateWispMaterial()
        {
            return HouseMaterials.NewParticle(ProceduralTextures.Get(ProceduralTextures.Id.SoftBlob), Color.white, false);
        }
    }
}
