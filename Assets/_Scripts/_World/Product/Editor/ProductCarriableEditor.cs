#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ProductCarriable inspector: the definition field plus a "New" button that creates a
/// ProductDefinition in the Product _Data folder and assigns it. The other serialized fields
/// (fallback name and icon) are drawn normally below it.
/// Lives next to ProductCarriable (editor-only via #if) so it travels with the Product addon.
/// </summary>
[CustomEditor(typeof(ProductCarriable))]
[CanEditMultipleObjects]
public class ProductCarriableEditor : Editor
{
    const string DataFolder = "Assets/_Data/Product/Product";

    SerializedProperty _definition;

    void OnEnable()
    {
        _definition = serializedObject.FindProperty("definition");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool create;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(_definition);
            create = GUILayout.Button(
                new GUIContent("New", "Create a new Product Definition in _Data and assign it."),
                GUILayout.Width(44f)
            );
        }

        if (create)
            CreateDefinition();

        // Every other serialized field (fallback displayName, icon, future fields).
        // Greyed out while the definition supplies them (left editable if the selection is mixed).
        bool isDefinitionAssigned =
            !_definition.hasMultipleDifferentValues && _definition.objectReferenceValue != null;
        using (new EditorGUI.DisabledScope(isDefinitionAssigned))
            DrawPropertiesExcluding(serializedObject, "m_Script", "definition");

        serializedObject.ApplyModifiedProperties();
    }

    // One new asset, assigned to every selected ProductCarriable. Named after the first selected object.
    void CreateDefinition()
    {
        EnsureFolder(DataFolder);

        string baseName = string.Concat(
            ((ProductCarriable)target).gameObject.name.Split(Path.GetInvalidFileNameChars())
        );
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "ProductCarriable";

        string path = AssetDatabase.GenerateUniqueAssetPath($"{DataFolder}/{baseName}.asset");
        var asset = CreateInstance<ProductDefinition>();
        AssetDatabase.CreateAsset(asset, path);

        var so = new SerializedObject(asset);
        so.FindProperty("displayName").stringValue = $"{baseName}";
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();

        _definition.objectReferenceValue = asset;
        EditorGUIUtility.PingObject(asset);
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
#endif
