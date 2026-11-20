using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[CustomPropertyDrawer(typeof(FolderAssetAttribute))]
public class FolderAssetDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.ObjectReference)
        {
            EditorGUI.LabelField(
                position,
                label.text,
                "Use [FolderAsset] with Object references only."
            );
            return;
        }

        FolderAssetAttribute folderAttr = (FolderAssetAttribute)attribute;
        string folderPath = folderAttr.FolderPath;

        // Detect the exact type of the serialized field (e.g., Coin, GameObject, ScriptableObject)
        Type targetType = fieldInfo.FieldType;
        if (targetType.IsArray)
        {
            targetType = targetType.GetElementType();
        }
        else if (
            targetType.IsGenericType
            && targetType.GetGenericTypeDefinition() == typeof(List<>)
        )
        {
            targetType = targetType.GetGenericArguments()[0];
        }

        List<Object> validAssets = new List<Object>();
        List<string> displayNames = new List<string> { $"None ({targetType.Name})" };

        if (AssetDatabase.IsValidFolder(folderPath))
        {
            // Search all assets in the folder
            string[] guids = AssetDatabase.FindAssets("", new string[] { folderPath });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (typeof(Component).IsAssignableFrom(targetType))
                {
                    // Handle MonoBehaviour / Component types (like Coin) on Prefabs
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null && prefab.TryGetComponent(targetType, out Component comp))
                    {
                        validAssets.Add(comp);
                        displayNames.Add(comp.name);
                    }
                }
                else
                {
                    // Handle GameObjects, ScriptableObjects, Materials, AudioClips, etc.
                    Object asset = AssetDatabase.LoadAssetAtPath(path, targetType);
                    if (asset != null)
                    {
                        validAssets.Add(asset);
                        displayNames.Add(asset.name);
                    }
                }
            }
        }
        else
        {
            displayNames[0] = $"Invalid Folder: {folderPath}";
        }

        // Find currently assigned item
        int currentIndex = 0;
        if (property.objectReferenceValue != null)
        {
            int foundIndex = validAssets.IndexOf(property.objectReferenceValue);
            if (foundIndex >= 0)
                currentIndex = foundIndex + 1;
        }

        // Draw dropdown
        EditorGUI.BeginProperty(position, label, property);
        int selectedIndex = EditorGUI.Popup(
            position,
            label.text,
            currentIndex,
            displayNames.ToArray()
        );
        property.objectReferenceValue = selectedIndex == 0 ? null : validAssets[selectedIndex - 1];
        EditorGUI.EndProperty();
    }
}
