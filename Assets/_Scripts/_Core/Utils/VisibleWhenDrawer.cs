using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Draws or hides a field from its VisibleWhen attributes. Put this file in an Editor folder.</summary>
[CustomPropertyDrawer(typeof(VisibleWhenAttribute), true)]
public class VisibleWhenDrawer : PropertyDrawer
{
    const float HeaderGap = 8f;
    const float NoteLines = 2f;

    enum Visibility
    {
        Shown,
        Hidden,
        HiddenWithNote, // hidden by an Assigned/Null condition and a Note is set
    }

    static readonly Dictionary<FieldInfo, VisibleWhenAttribute[]> RuleCache = new();
    static readonly HashSet<string> Warned = new();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var rules = GetRules();
        switch (Evaluate(property, rules))
        {
            case Visibility.Hidden:
                return -EditorGUIUtility.standardVerticalSpacing; // cancels the gap the Inspector adds
            case Visibility.HiddenWithNote:
                return EditorGUIUtility.singleLineHeight * NoteLines;
        }

        float height = EditorGUI.GetPropertyHeight(property, label, true);
        if (FirstHeader(rules) != null)
            height +=
                HeaderGap
                + EditorGUIUtility.singleLineHeight
                + EditorGUIUtility.standardVerticalSpacing;
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var rules = GetRules();
        switch (Evaluate(property, rules))
        {
            case Visibility.Hidden:
                return;
            case Visibility.HiddenWithNote:
                EditorGUI.HelpBox(position, FirstNote(rules), MessageType.Info);
                return;
        }

        string header = FirstHeader(rules);
        if (header != null)
        {
            position.y += HeaderGap;
            position.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(position, header, EditorStyles.boldLabel);
            position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
        }
        position.height = EditorGUI.GetPropertyHeight(property, label, true);
        EditorGUI.PropertyField(position, property, label, true);
    }

    Visibility Evaluate(SerializedProperty property, VisibleWhenAttribute[] rules)
    {
        bool anyFailed = false;
        bool onlyObjectConditionsFailed = true;
        foreach (var rule in rules)
        {
            if (IsMet(property.serializedObject, rule))
                continue;
            anyFailed = true;
            if (
                rule.Condition != VisibleCondition.Assigned
                && rule.Condition != VisibleCondition.Null
            )
                onlyObjectConditionsFailed = false;
        }

        if (!anyFailed)
            return Visibility.Shown;
        bool hasNote = onlyObjectConditionsFailed && FirstNote(rules) != null;
        return hasNote ? Visibility.HiddenWithNote : Visibility.Hidden;
    }

    static bool IsMet(SerializedObject owner, VisibleWhenAttribute rule)
    {
        var target = owner.FindProperty(rule.Field);
        if (target == null)
        {
            string key = $"{owner.targetObject.GetType().Name}.{rule.Field}";
            if (Warned.Add(key))
                Debug.LogWarning(
                    $"VisibleWhen: no serialized field '{rule.Field}' on {owner.targetObject.GetType().Name}, so the field stays visible."
                );
            return true; // never hide a field because of a typo
        }

        switch (rule.Condition)
        {
            case VisibleCondition.Assigned:
                return target.propertyType == SerializedPropertyType.ObjectReference
                    && target.objectReferenceValue != null;
            case VisibleCondition.Null:
                return target.propertyType == SerializedPropertyType.ObjectReference
                    && target.objectReferenceValue == null;
            case VisibleCondition.EnumEquals:
                return target.intValue == rule.Value;
            case VisibleCondition.EnumNotEquals:
                return target.intValue != rule.Value;
            default:
                return true;
        }
    }

    // fieldInfo holds every VisibleWhen on the field: Unity only runs one drawer, so it reads them all here.
    VisibleWhenAttribute[] GetRules()
    {
        if (RuleCache.TryGetValue(fieldInfo, out var cached))
            return cached;
        var found = fieldInfo.GetCustomAttributes(typeof(VisibleWhenAttribute), true);
        var rules = Array.ConvertAll(found, a => (VisibleWhenAttribute)a);
        RuleCache[fieldInfo] = rules;
        return rules;
    }

    static string FirstHeader(VisibleWhenAttribute[] rules)
    {
        foreach (var rule in rules)
            if (!string.IsNullOrEmpty(rule.Header))
                return rule.Header;
        return null;
    }

    static string FirstNote(VisibleWhenAttribute[] rules)
    {
        foreach (var rule in rules)
            if (!string.IsNullOrEmpty(rule.Note))
                return rule.Note;
        return null;
    }
}
