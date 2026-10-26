using UnityEditor;

namespace Facecast.EditorTools
{
    /// <summary>
    /// Import settings for the Blender FBX exports: metres, no materials (colour is in the vertex colours and the
    /// prefab builder assigns the shared materials), no animation, cameras or lights, imported flat normals, and UVs
    /// kept (the avatar's Face screen maps the webcam with them).
    /// </summary>
    public class ModelImport : AssetPostprocessor
    {
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Paths.ModelsDir)) return;
            var importer = (ModelImporter)assetImporter;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.addCollider = false;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.generateSecondaryUV = false;
            importer.preserveHierarchy = true;
            importer.sortHierarchyByName = false;
        }
    }
}
