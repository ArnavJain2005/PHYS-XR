using System.Collections.Generic;
using PHYSXR.Game.Audio;
using PHYSXR.Ghost;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // The game's answer to the ghost's presentation needs: procedural sounds
    // (or local overrides), the modelled body parts and material instances
    // built from the shared templates. Materials are new instances each call;
    // the ghost owns and destroys them. Meshes are shared imported assets.
    public sealed class HollowAssets : IGhostPresentationAssets
    {
        // Resources path of the FBX made by Tools/blender/build_hollow.py.
        public const string MeshResource = "PHYSXR/Hollow/Hollow";

        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static Dictionary<string, Mesh> meshes;

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
                case GhostSound.Presence: return AudioCue.GhostPresence;
                case GhostSound.Drag: return AudioCue.GhostDrag;
                default: return AudioCue.GhostHiss;
            }
        }

        public Mesh BodyMesh(GhostBodyPart part)
        {
            if (meshes == null)
            {
                meshes = new Dictionary<string, Mesh>();
                foreach (Mesh m in Resources.LoadAll<Mesh>(MeshResource))
                    meshes[m.name] = m;
                if (meshes.Count == 0)
                    Debug.LogWarning("[HollowAssets] No meshes at Resources/" + MeshResource + "; using the primitive figure.");
            }
            return meshes.TryGetValue("Hollow_" + part, out Mesh mesh) ? mesh : null;
        }

        // Grave linen: dark, damp, double-sided (the robe is torn open).
        public Material CreateShroudMaterial()
        {
            Material m = HouseMaterials.NewLit(new Color(1.15f, 1.12f, 1.1f), 0.14f);
            m.SetTexture("_BaseMap", ProceduralTextures.Get(ProceduralTextures.Id.GraveLinen));
            m.SetTexture("_BumpMap", ProceduralTextures.Get(ProceduralTextures.Id.GraveLinenNormal));
            m.SetFloat("_BumpScale", 0.3f);
            m.SetFloat(CullId, 0f);
            return m;
        }

        // Bloodless skin with a clammy sheen and a faint cold self-light, so
        // the face, ribs and hands keep a readable pallor in near darkness
        // without the figure glowing.
        public Material CreateSkinMaterial()
        {
            return HouseMaterials.NewEmissiveLit(ProceduralTextures.Get(ProceduralTextures.Id.GhostSkin),
                new Color(0.5f, 0.53f, 0.52f), 0.3f, new Color(0.014f, 0.018f, 0.025f));
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
