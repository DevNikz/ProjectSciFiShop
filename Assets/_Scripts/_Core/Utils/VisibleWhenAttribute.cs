using System;
using UnityEngine;

public enum VisibleCondition
{
    Assigned, // the named object reference is not null
    Null, // the named object reference is null
    EnumEquals, // the named enum field equals the value
    EnumNotEquals, // the named enum field differs from the value
}

/// <summary>
/// Shows a serialized field in the Inspector only while a condition on a sibling field holds.
/// Stack several (or subclass it) to combine conditions: all must hold. Needs VisibleWhenDrawer (Editor).
/// Use Header instead of [Header] for a group title that must hide with its fields.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class VisibleWhenAttribute : PropertyAttribute
{
    public readonly string Field;
    public readonly VisibleCondition Condition;
    public readonly int Value;

    /// <summary>Optional group title drawn above this field while it is visible.</summary>
    public string Header;

    /// <summary>
    /// Optional info box drawn in place of this field when only an Assigned/Null condition hides it
    /// (for example "a UI Style is overriding these settings").
    /// </summary>
    public string Note;

    /// <param name="field">Name of the sibling serialized field to test.</param>
    /// <param name="condition">The test.</param>
    /// <param name="value">Enum value as int, for EnumEquals and EnumNotEquals.</param>
    public VisibleWhenAttribute(string field, VisibleCondition condition, int value = 0)
    {
        Field = field;
        Condition = condition;
        Value = value;
    }
}
