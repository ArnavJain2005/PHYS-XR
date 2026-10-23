using System.Collections.Generic;
using UnityEngine;

namespace PHYSXR.Game.Art
{
    // Shared URP materials for the house, built from the procedural textures.
    //
    // Materials are cloned from a few template assets in
    // Resources/PHYSXR/ (made by PHYS-XR > Art > Create Material Templates).
    // The templates exist so the shader variants the game switches on at
    // runtime (_NORMALMAP, _EMISSION, transparency, particles) are referenced
    // by a shipped asset and therefore kept in player builds. If a template is
    // missing, the URP shader is looked up by name instead.
    //
    // Every material here is shared and cached for the process lifetime
    // (bounded by the Surface enum), so scene reloads never leak or duplicate
    // them. Code that needs a per-object variation must create and destroy
    // its own instance.
    public static class HouseMaterials
    {
        public enum Surface
        {
            FloorBoards,
            Wallpaper,
            Wainscot,
            Plaster,
            Tiles,
            Rug,
            Runner,
            DarkWood,
            PaintedWood,
            Trim,
            Metal,
            Brass,
            Porcelain,
            Paper,
            Fabric,
            Mattress,
            Portrait,
            Landscape,
            Siding,
            Roof,
            Ground,
            Black,
            Glass,
            WindowStorm,
            BulbOff,
            BulbOn,
            EmergencyOn,
            CandleFlame,
            Fuse,
            FuseInstalled,
            LockRed,
            LockGreen,
            Stone,
            Bark,
            LitWindow,
            WetSoil,
            Gouge
        }

        public const string TemplateLit = "PHYSXR/Mat_LitNormal";
        public const string TemplateEmissive = "PHYSXR/Mat_LitEmissive";
        public const string TemplateTransparent = "PHYSXR/Mat_LitTransparent";
        public const string TemplateParticleAlpha = "PHYSXR/Mat_ParticleAlpha";
        public const string TemplateParticleAdditive = "PHYSXR/Mat_ParticleAdditive";

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int CullId = Shader.PropertyToID("_Cull");

        private static readonly Dictionary<Surface, Material> Cache = new Dictionary<Surface, Material>();
        private static readonly Dictionary<string, Material> Templates = new Dictionary<string, Material>();

        public static Material Get(Surface surface)
        {
            if (Cache.TryGetValue(surface, out Material cached) && cached != null)
                return cached;

            Material material = Build(surface);
            material.name = "PHYSXR_" + surface;
            material.hideFlags = HideFlags.DontSave;
            Cache[surface] = material;
            return material;
        }

        // A new opaque lit material (caller owns and destroys it).
        public static Material NewLit(Color color, float smoothness = 0.2f)
        {
            Material m = FromTemplate(TemplateLit, "Universal Render Pipeline/Lit");
            m.SetColor(BaseColorId, color);
            m.SetFloat(SmoothnessId, smoothness);
            return m;
        }

        // A new opaque, textured, faintly self-lit material (caller owns it).
        // Uses the emissive template, so it carries no normal map.
        public static Material NewEmissiveLit(Texture albedo, Color tint, float smoothness, Color emission)
        {
            Material m = Emissive(tint, emission);
            m.SetTexture(BaseMapId, albedo);
            m.SetFloat(SmoothnessId, smoothness);
            m.SetFloat(MetallicId, 0f);
            return m;
        }

        // A new transparent, optionally emissive material (caller owns it).
        public static Material NewTransparent(Color color, Color emission, bool doubleSided)
        {
            Material m = FromTemplate(TemplateTransparent, "Universal Render Pipeline/Lit");
            m.SetColor(BaseColorId, color);
            m.SetFloat(SmoothnessId, 0.1f);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            if (doubleSided)
                m.SetFloat(CullId, 0f);
            return m;
        }

        // A new particle material (caller owns it).
        public static Material NewParticle(Texture texture, Color tint, bool additive)
        {
            Material m = FromTemplate(additive ? TemplateParticleAdditive : TemplateParticleAlpha,
                "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture(BaseMapId, texture);
            m.SetColor(BaseColorId, tint);
            return m;
        }

        public static Color EmissionOf(Surface surface)
        {
            Material m = Get(surface);
            return m.HasProperty(EmissionColorId) ? m.GetColor(EmissionColorId) : Color.black;
        }

        private static Material Build(Surface s)
        {
            switch (s)
            {
                case Surface.FloorBoards: return Textured(ProceduralTextures.Id.FloorBoards, ProceduralTextures.Id.FloorBoardsNormal, Color.white, 0.32f, 1f);
                case Surface.Wallpaper: return Textured(ProceduralTextures.Id.Wallpaper, ProceduralTextures.Id.WallpaperNormal, Color.white, 0.12f, 0.6f);
                case Surface.Wainscot: return Textured(ProceduralTextures.Id.Wainscot, ProceduralTextures.Id.WainscotNormal, Color.white, 0.35f, 1f);
                case Surface.Plaster: return Textured(ProceduralTextures.Id.Plaster, null, Color.white, 0.05f, 0f);
                case Surface.Tiles: return Textured(ProceduralTextures.Id.Tiles, ProceduralTextures.Id.TilesNormal, Color.white, 0.55f, 1f);
                case Surface.Rug: return Textured(ProceduralTextures.Id.Rug, null, Color.white, 0.02f, 0f);
                case Surface.Runner: return Textured(ProceduralTextures.Id.Runner, null, Color.white, 0.02f, 0f);
                case Surface.DarkWood: return Textured(ProceduralTextures.Id.DarkWood, null, Color.white, 0.38f, 0f);
                case Surface.PaintedWood: return Textured(ProceduralTextures.Id.DarkWood, null, new Color(1.6f, 1.7f, 1.6f), 0.25f, 0f);
                case Surface.Trim: return Textured(ProceduralTextures.Id.DarkWood, null, new Color(0.8f, 0.75f, 0.7f), 0.45f, 0f);
                case Surface.Metal: return Metal(ProceduralTextures.Id.Metal, Color.white, 0.45f, 0.6f);
                case Surface.Brass: return Metal(ProceduralTextures.Id.Metal, new Color(0.9f, 0.7f, 0.35f), 0.6f, 0.9f);
                case Surface.Porcelain: return Plain(new Color(0.75f, 0.74f, 0.7f), 0.8f);
                case Surface.Paper: return Textured(ProceduralTextures.Id.Paper, null, Color.white, 0.05f, 0f);
                case Surface.Fabric: return Textured(ProceduralTextures.Id.Fabric, null, new Color(0.9f, 0.85f, 0.8f), 0.05f, 0f);
                case Surface.Mattress: return Textured(ProceduralTextures.Id.Fabric, null, new Color(1.4f, 1.3f, 1.1f), 0.05f, 0f);
                case Surface.Portrait: return Textured(ProceduralTextures.Id.Portrait, null, Color.white, 0.5f, 0f);
                case Surface.Landscape: return Textured(ProceduralTextures.Id.Landscape, null, Color.white, 0.5f, 0f);
                case Surface.Siding: return Textured(ProceduralTextures.Id.Siding, null, Color.white, 0.2f, 0f);
                case Surface.Roof: return Textured(ProceduralTextures.Id.Roof, null, Color.white, 0.5f, 0f);
                case Surface.Ground: return Textured(ProceduralTextures.Id.Ground, null, Color.white, 0.6f, 0f);
                case Surface.Black: return Plain(new Color(0.02f, 0.02f, 0.02f), 0.1f);
                case Surface.Stone: return Textured(ProceduralTextures.Id.Plaster, null, new Color(0.6f, 0.6f, 0.62f), 0.15f, 0f);
                case Surface.Bark: return Textured(ProceduralTextures.Id.DarkWood, null, new Color(0.5f, 0.45f, 0.4f), 0.05f, 0f);
                case Surface.Glass: return NewTransparent(new Color(0.12f, 0.14f, 0.16f, 0.35f), Color.black, false);
                case Surface.WindowStorm: return Emissive(new Color(0.03f, 0.04f, 0.06f), new Color(0.02f, 0.03f, 0.05f));
                case Surface.BulbOff: return Emissive(new Color(0.15f, 0.14f, 0.12f), Color.black);
                case Surface.BulbOn: return Emissive(new Color(1f, 0.9f, 0.7f), new Color(4f, 3.2f, 2.0f));
                case Surface.EmergencyOn: return Emissive(new Color(0.6f, 0.05f, 0.03f), new Color(2.5f, 0.2f, 0.1f));
                case Surface.CandleFlame: return Emissive(new Color(1f, 0.7f, 0.3f), new Color(6f, 3.2f, 1.0f));
                case Surface.Fuse: return Emissive(new Color(0.85f, 0.45f, 0.1f), new Color(0.8f, 0.35f, 0.05f));
                case Surface.FuseInstalled: return Emissive(new Color(0.85f, 0.45f, 0.1f), new Color(1.6f, 0.7f, 0.1f));
                case Surface.LockRed: return Emissive(new Color(0.7f, 0.05f, 0.05f), new Color(2f, 0.1f, 0.1f));
                case Surface.LockGreen: return Emissive(new Color(0.1f, 0.7f, 0.2f), new Color(0.2f, 2f, 0.4f));
                case Surface.LitWindow: return Emissive(new Color(0.3f, 0.22f, 0.12f), new Color(1.4f, 0.9f, 0.45f));
                // Dark, still-wet grave soil: a sheen that catches the lantern.
                case Surface.WetSoil: return Textured(ProceduralTextures.Id.Ground, null, new Color(0.2f, 0.17f, 0.15f), 0.45f, 0f);
                // Raw, pale wood under the varnish.
                case Surface.Gouge: return Plain(new Color(0.42f, 0.32f, 0.22f), 0.15f);
                default: return Plain(Color.magenta, 0f);
            }
        }

        private static Material Textured(ProceduralTextures.Id albedo, ProceduralTextures.Id? normal, Color tint, float smoothness, float bumpScale)
        {
            Material m = FromTemplate(TemplateLit, "Universal Render Pipeline/Lit");
            m.SetTexture(BaseMapId, ProceduralTextures.Get(albedo));
            m.SetColor(BaseColorId, tint);
            m.SetFloat(SmoothnessId, smoothness);
            m.SetFloat(MetallicId, 0f);
            if (normal.HasValue)
            {
                m.SetTexture(BumpMapId, ProceduralTextures.Get(normal.Value));
                m.SetFloat(BumpScaleId, bumpScale);
                m.EnableKeyword("_NORMALMAP");
            }
            else
            {
                m.SetTexture(BumpMapId, null);
                m.DisableKeyword("_NORMALMAP");
            }
            return m;
        }

        private static Material Metal(ProceduralTextures.Id albedo, Color tint, float smoothness, float metallic)
        {
            Material m = Textured(albedo, null, tint, smoothness, 0f);
            m.SetFloat(MetallicId, metallic);
            return m;
        }

        private static Material Plain(Color color, float smoothness)
        {
            Material m = FromTemplate(TemplateLit, "Universal Render Pipeline/Lit");
            m.SetTexture(BaseMapId, null);
            m.SetTexture(BumpMapId, null);
            m.DisableKeyword("_NORMALMAP");
            m.SetColor(BaseColorId, color);
            m.SetFloat(SmoothnessId, smoothness);
            return m;
        }

        private static Material Emissive(Color baseColor, Color emission)
        {
            Material m = FromTemplate(TemplateEmissive, "Universal Render Pipeline/Lit");
            m.SetTexture(BaseMapId, null);
            m.SetColor(BaseColorId, baseColor);
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        private static Material FromTemplate(string resource, string fallbackShader)
        {
            if (!Templates.TryGetValue(resource, out Material template) || template == null)
            {
                template = Resources.Load<Material>(resource);
                Templates[resource] = template;
            }

            if (template != null)
                return new Material(template);

            Shader shader = Shader.Find(fallbackShader);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            return new Material(shader);
        }
    }
}
