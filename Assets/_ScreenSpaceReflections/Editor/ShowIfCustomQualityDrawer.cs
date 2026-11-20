// Hides a field unless the sibling SSRSettings.quality is Custom. Fails open (draws normally) if the sibling is not found.
using UnityEditor;
using UnityEngine;

namespace SSRFeature.Editor
{
    [CustomPropertyDrawer(typeof(ShowIfCustomQualityAttribute))]
    public sealed class ShowIfCustomQualityDrawer : PropertyDrawer
    {
        static bool IsHidden(SerializedProperty property)
        {
            string path = property.propertyPath;
            int dot = path.LastIndexOf('.');
            string parent = dot >= 0 ? path.Substring(0, dot + 1) : string.Empty;
            SerializedProperty quality = property.serializedObject.FindProperty(parent + "quality");
            return quality != null && quality.enumValueIndex != (int)SSRQuality.Custom;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => IsHidden(property) ? 0f : EditorGUI.GetPropertyHeight(property, label, true);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (IsHidden(property)) return;
            EditorGUI.PropertyField(position, property, label, true);
        }
    }
}
