using System.IO;
using System.Reflection;
using PHYSXR.Game.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PHYSXR.EditorTools
{
    // Editor-only setup for the procedural art.
    //
    //  - CreateMaterialTemplates: writes the few material assets the runtime
    //    clones (Resources/PHYSXR/Mat_*.mat). Their only job is to make player
    //    builds keep the URP shader variants the game switches on at runtime
    //    (normal maps, emission, transparency, particles): Unity strips
    //    variants no shipped material uses. Idempotent.
    //  - ApplyGameSceneLighting: saves exponential-squared fog into the game
    //    scene's lighting settings, so the fog variants are kept too (the
    //    house also sets fog at runtime). Opens and saves the scene in place,
    //    keeping its GUID.
    //
    // Batch use:
    //   Unity -batchmode -quit -projectPath <proj> -executeMethod PHYSXR.EditorTools.PhysxrArtSetup.SetupAll
    public static class PhysxrArtSetup
    {
        private const string Folder = "Assets/PHYSXR/Resources/PHYSXR";

        [MenuItem("PHYS-XR/Art/Create Material Templates")]
        public static void CreateMaterialTemplates()
        {
            Directory.CreateDirectory(Folder);
            Texture2D flatNormal = FlatNormal();

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            Material normal = Template("Mat_LitNormal", lit);
            normal.SetTexture("_BumpMap", flatNormal);
            normal.EnableKeyword("_NORMALMAP");
            Save(normal);

            Material emissive = Template("Mat_LitEmissive", lit);
            emissive.SetColor("_EmissionColor", Color.white);
            emissive.EnableKeyword("_EMISSION");
            emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            Save(emissive);

            Material transparent = Template("Mat_LitTransparent", lit);
            transparent.SetFloat("_Surface", 1f);
            transparent.SetFloat("_Blend", 0f);
            transparent.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            transparent.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            transparent.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            transparent.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            transparent.SetFloat("_ZWrite", 0f);
            transparent.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.5f));
            transparent.SetColor("_EmissionColor", Color.black);
            transparent.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            transparent.EnableKeyword("_EMISSION");
            transparent.SetOverrideTag("RenderType", "Transparent");
            transparent.renderQueue = (int)RenderQueue.Transparent;
            Save(transparent);

            Material alpha = Template("Mat_ParticleAlpha", particles);
            ParticleBlend(alpha, false);
            Save(alpha);

            Material additive = Template("Mat_ParticleAdditive", particles);
            ParticleBlend(additive, true);
            Save(additive);

            AssetDatabase.SaveAssets();
            Debug.Log("PhysxrArtSetup: material templates written to " + Folder);
        }

        [MenuItem("PHYS-XR/Art/Apply Game Scene Lighting")]
        public static void ApplyGameSceneLighting()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            var scene = EditorSceneManager.OpenScene(M2SceneSetup.GameScenePath, OpenSceneMode.Single);
            ApplyLighting();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("PhysxrArtSetup: fog and ambient saved into " + M2SceneSetup.GameScenePath);
        }

        // Shared with M2SceneSetup.CreateScenes.
        public static void ApplyLighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.016f, 0.018f, 0.024f);
            RenderSettings.fogDensity = 0.05f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.010f, 0.012f, 0.018f);
            RenderSettings.skybox = null;
        }

        public static void SetupAll()
        {
            CreateMaterialTemplates();
            ApplyGameSceneLighting();
            if (Application.isBatchMode)
                EditorApplication.Exit(0);
        }

        private static Material Template(string name, Shader shader)
        {
            string path = Folder + "/" + name + ".mat";
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = shader;
                return existing;
            }

            var material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void Save(Material material)
        {
            // Let URP's own material validation settle keywords and render
            // state for the properties set above, where that API is available.
            MethodInfo setup = System.Type.GetType("UnityEditor.BaseShaderGUI, Unity.RenderPipelines.Universal.Editor")
                ?.GetMethod("SetupMaterialBlendMode", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            setup?.Invoke(null, new object[] { material });
            EditorUtility.SetDirty(material);
        }

        private static void ParticleBlend(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive)
                m.EnableKeyword("_BLENDMODE_ADD");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        // A 4x4 flat normal map asset, imported as a normal map.
        private static Texture2D FlatNormal()
        {
            string path = Folder + "/FlatNormal.png";
            if (!File.Exists(path))
            {
                var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(128, 128, 255, 255);
                tex.SetPixels32(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        [MenuItem("PHYS-XR/Art/Preview Procedural Textures")]
        public static void PreviewTextures()
        {
            ProceduralTextures.WarmAll();
            Debug.Log("PhysxrArtSetup: generated " + System.Enum.GetValues(typeof(ProceduralTextures.Id)).Length + " procedural textures.");
        }
    }
}
