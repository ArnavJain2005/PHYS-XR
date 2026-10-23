using UnityEditor;

namespace PHYSXR.EditorTools
{
    // Import settings for The Hollow's body parts (Resources/PHYSXR/Hollow/,
    // exported by Tools/blender/build_hollow.py). Kept in code so a fresh
    // checkout imports them identically:
    //  - meshes only: the game builds the ghost's materials itself, and the
    //    figure is animated procedurally, so no rig, clips, cameras or lights;
    //  - readable: HollowVisual moves the robe's vertices on the CPU (cloth);
    //  - the Blender export already baked the axis conversion and uses metres.
    public class HollowModelImport : AssetPostprocessor
    {
        public const string Folder = "Assets/PHYSXR/Resources/PHYSXR/Hollow/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder))
                return;

            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = false;
            importer.isReadable = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.addCollider = false;
        }
    }
}
