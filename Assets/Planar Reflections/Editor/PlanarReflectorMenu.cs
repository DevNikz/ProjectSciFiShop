using UnityEditor;
using UnityEngine;

namespace Studio.PlanarReflections.Editor
{
    internal static class PlanarReflectorMenu
    {
        const string k_ShaderName = "PlanarReflections/PlanarReflectionLit";
        const string k_MaterialFolder = "Assets/Planar Reflections";

        [MenuItem("GameObject/Planar Reflections/Planar Reflector", false, 10)]
        static void CreatePlanarReflector(MenuCommand command)
        {
            Shader shader = Shader.Find(k_ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[PlanarReflections] Shader '{k_ShaderName}' not found. Reflector created without a material.");
            }

            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Planar Reflector";
            GameObjectUtility.SetParentAndAlign(plane, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(plane, "Create Planar Reflector");

            if (shader != null)
                plane.GetComponent<MeshRenderer>().sharedMaterial = CreateMaterialAsset(shader);

            Undo.AddComponent<PlanarReflector>(plane);
            Selection.activeGameObject = plane;
        }

        static Material CreateMaterialAsset(Shader shader)
        {
            if (!AssetDatabase.IsValidFolder(k_MaterialFolder))
                AssetDatabase.CreateFolder("Assets", "Planar Reflections");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{k_MaterialFolder}/PlanarReflector.mat");
            var material = new Material(shader) { name = "PlanarReflector" };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
