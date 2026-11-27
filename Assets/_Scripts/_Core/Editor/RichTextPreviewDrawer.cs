using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(RichTextPreviewAttribute))]
public class RichTextPreviewDrawer : PropertyDrawer
{
    private float _lastWidth = 300f;

    private static readonly Regex GetCompRegex = new Regex(
        @"^GetComponent(InParent|InChildren)?<(\w+)>\(\)\.(.+)$",
        RegexOptions.Compiled
    );

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
            return EditorGUIUtility.singleLineHeight;

        GUIStyle previewStyle = GetPreviewStyle();
        string resolvedText = ResolveVariables(property);

        float textAreaHeight = EditorGUIUtility.singleLineHeight * 3f;
        float previewHeight = previewStyle.CalcHeight(new GUIContent(resolvedText), _lastWidth);

        return textAreaHeight + previewHeight + 12f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.LabelField(position, label.text, "Use [RichTextPreview] with strings only.");
            return;
        }

        if (position.width > 1f)
            _lastWidth = position.width;

        EditorGUI.BeginProperty(position, label, property);

        float textAreaHeight = EditorGUIUtility.singleLineHeight * 3f;
        EditorGUI.LabelField(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            label
        );

        Rect inputRect = new Rect(
            position.x,
            position.y + EditorGUIUtility.singleLineHeight,
            position.width,
            textAreaHeight - EditorGUIUtility.singleLineHeight
        );
        property.stringValue = EditorGUI.TextArea(inputRect, property.stringValue);

        string resolvedText = ResolveVariables(property);
        GUIStyle previewStyle = GetPreviewStyle();
        float previewHeight = position.height - textAreaHeight - 4f;
        Rect previewRect = new Rect(
            position.x,
            position.y + textAreaHeight + 4f,
            position.width,
            previewHeight
        );

        EditorGUI.LabelField(previewRect, resolvedText, previewStyle);

        EditorGUI.EndProperty();
    }

    private string ResolveVariables(SerializedProperty property)
    {
        string rawText = property.stringValue;
        UnityEngine.Object rootTarget = property.serializedObject.targetObject;

        if (string.IsNullOrEmpty(rawText) || rootTarget == null)
            return rawText;

        return Regex.Replace(
            rawText,
            @"\{([^{}]+)\}",
            match =>
            {
                string token = match.Value.Trim('{', '}').Trim();

                // 1. Check {GetComponent<Type>().member.path}
                Match compMatch = GetCompRegex.Match(token);
                if (compMatch.Success)
                {
                    if (rootTarget is Component hostComponent)
                    {
                        string scope = compMatch.Groups[1].Value;
                        string componentName = compMatch.Groups[2].Value;
                        string memberPath = compMatch.Groups[3].Value;

                        Component targetComp = FindComponentByName(
                            hostComponent,
                            componentName,
                            scope
                        );
                        if (targetComp == null)
                            return $"<color=#FF5555>{{Missing {componentName}}}</color>";

                        string val = GetMemberValue(targetComp, memberPath);
                        return val ?? $"<color=#FF5555>{{{memberPath}?}}</color>";
                    }
                    return $"<color=#FF5555>{{Not a Component}}</color>";
                }

                // 2. Check sibling field inside this specific array/list struct element
                string parentPath = GetParentPropertyPath(property.propertyPath);
                if (!string.IsNullOrEmpty(parentPath))
                {
                    SerializedProperty siblingProp = property.serializedObject.FindProperty(
                        $"{parentPath}.{token}"
                    );
                    if (siblingProp != null)
                        return GetSerializedPropertyValue(siblingProp);
                }

                // 3. Fallback: check the parent MonoBehaviour/ScriptableObject
                string rootVal = GetMemberValue(rootTarget, token);
                return rootVal ?? $"<color=#FF5555>{{{token}?}}</color>";
            }
        );
    }

    private Component FindComponentByName(Component host, string componentName, string scope)
    {
        Component[] candidates = scope switch
        {
            "InParent" => host.GetComponentsInParent<Component>(true),
            "InChildren" => host.GetComponentsInChildren<Component>(true),
            _ => host.GetComponents<Component>(),
        };

        return candidates.FirstOrDefault(c =>
            c != null && c.GetType().Name.Equals(componentName, StringComparison.Ordinal)
        );
    }

    private string GetParentPropertyPath(string propertyPath)
    {
        int lastDot = propertyPath.LastIndexOf('.');
        return lastDot > 0 ? propertyPath.Substring(0, lastDot) : null;
    }

    private string GetSerializedPropertyValue(SerializedProperty prop)
    {
        return prop.propertyType switch
        {
            SerializedPropertyType.String => prop.stringValue,
            SerializedPropertyType.Integer => prop.intValue.ToString(),
            SerializedPropertyType.Float => prop.floatValue.ToString(),
            SerializedPropertyType.Boolean => prop.boolValue.ToString(),
            SerializedPropertyType.Enum => prop.enumDisplayNames[prop.enumValueIndex],
            _ => prop.displayName,
        };
    }

    private static string GetMemberValue(object obj, string memberPath)
    {
        if (obj == null)
            return null;

        // Supports {GetComponent<ProductCarriable>().GetType().Name} or .typeName
        if (memberPath == "GetType().Name" || memberPath == "typeName")
            return obj.GetType().Name;

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object current = obj;

        foreach (string part in memberPath.Split('.'))
        {
            if (current == null)
                return null;

            var type = current.GetType();
            FieldInfo field = type.GetField(part, flags);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            PropertyInfo prop = type.GetProperty(part, flags);
            if (prop != null)
            {
                current = prop.GetValue(current);
                continue;
            }

            return null;
        }

        return current?.ToString() ?? "null";
    }

    private GUIStyle GetPreviewStyle()
    {
        GUIStyle style = new GUIStyle(EditorStyles.helpBox);
        style.richText = true;
        style.wordWrap = true;
        style.fontSize = 12;
        return style;
    }
}
