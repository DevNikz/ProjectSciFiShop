using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

public static class RichTextExtensions
{
    private static readonly Regex GetCompRegex = new Regex(
        @"^GetComponent(InParent|InChildren)?<(\w+)>\(\)\.(.+)$",
        RegexOptions.Compiled
    );

    public static string FormatWithVariables(
        this string text,
        Component hostComponent,
        object contextStruct = null
    )
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Matches anything inside { ... } (supports < > ( ) . and word characters)
        return Regex.Replace(
            text,
            @"\{([^{}]+)\}",
            match =>
            {
                string token = match.Value.Trim('{', '}').Trim();

                // 1. Check {GetComponent<Type>().member.path}
                Match compMatch = GetCompRegex.Match(token);
                if (compMatch.Success && hostComponent != null)
                {
                    string scope = compMatch.Groups[1].Value; // "", "InParent", or "InChildren"
                    string componentName = compMatch.Groups[2].Value;
                    string memberPath = compMatch.Groups[3].Value;

                    Component targetComp = FindComponentByName(hostComponent, componentName, scope);
                    if (targetComp != null)
                    {
                        string val = GetMemberValue(targetComp, memberPath);
                        if (val != null)
                            return val;
                    }
                    return match.Value;
                }

                // 2. Check sibling field inside the PromptEntry struct (e.g. {action})
                if (contextStruct != null)
                {
                    string structVal = GetMemberValue(contextStruct, token);
                    if (structVal != null)
                        return structVal;
                }

                // 3. Check the host MonoBehaviour itself
                if (hostComponent != null)
                {
                    string hostVal = GetMemberValue(hostComponent, token);
                    if (hostVal != null)
                        return hostVal;
                }

                return match.Value;
            }
        );
    }

    private static Component FindComponentByName(Component host, string componentName, string scope)
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
}
