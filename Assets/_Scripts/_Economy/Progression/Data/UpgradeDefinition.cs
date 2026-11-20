using System;
using UnityEngine;

public enum UpgradeKind
{
    DeliveryTime, // multiplies the delivery delay: below 1 = faster
    ShelfCapacity, // multiplies what a shelf holds: above 1 = more
}

[Serializable]
public struct UpgradeLevel
{
    [Tooltip("Coins to buy this level (min 1).")]
    public int cost;

    [Tooltip("The multiplier while this is the owned level. It does not stack on lower levels.")]
    public float multiplier;
}

/// <summary>
/// Read-only authored data for one upgrade. Level 0 (not bought) always means a multiplier of 1.
/// How many levels the player owns lives in ProgressionManager, never here.
/// </summary>
[CreateAssetMenu(fileName = "Upgrade", menuName = "Shop/Upgrade Definition")]
public class UpgradeDefinition : ScriptableObject
{
    [Tooltip("Stable unique id used by saves. Never change after release. Empty = asset name.")]
    [SerializeField]
    string id;

    [Tooltip("Shown on the progression screen. Empty = asset name.")]
    [SerializeField]
    string displayName;

    [Tooltip("Optional line shown under the name.")]
    [SerializeField]
    string description;

    [Tooltip("What this upgrade changes. Several upgrades of one kind multiply together.")]
    [SerializeField]
    UpgradeKind kind;

    [Tooltip("One entry per level, level 1 first.")]
    [SerializeField]
    UpgradeLevel[] levels = Array.Empty<UpgradeLevel>();

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public string Description => description;
    public UpgradeKind Kind => kind;
    public int LevelCount => levels != null ? levels.Length : 0;

    /// <summary>The multiplier at this owned level (0 or less = 1, above the last level = the last).</summary>
    public float MultiplierAt(int level)
    {
        int n = LevelCount;
        if (level < 1 || n == 0)
            return 1f;
        return levels[Mathf.Min(level, n) - 1].multiplier;
    }

    /// <summary>Coins for this level (1-based). False if there is no such level.</summary>
    public bool TryGetCost(int level, out int cost)
    {
        cost = 0;
        if (level < 1 || level > LevelCount)
            return false;
        cost = levels[level - 1].cost;
        return true;
    }

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = name;
        if (levels == null)
            return;

        for (int i = 0; i < levels.Length; i++)
        {
            var l = levels[i];
            l.cost = Mathf.Max(1, l.cost);
            l.multiplier = Mathf.Max(0.05f, l.multiplier);
            levels[i] = l;

            if (kind == UpgradeKind.DeliveryTime && l.multiplier > 1f)
                Debug.LogWarning(
                    $"{name}: level {i + 1} makes deliveries slower (multiplier above 1).",
                    this
                );
            if (kind == UpgradeKind.ShelfCapacity && l.multiplier < 1f)
                Debug.LogWarning(
                    $"{name}: level {i + 1} makes shelves smaller (multiplier below 1).",
                    this
                );
        }
        if (levels.Length == 0)
            Debug.LogWarning($"{name}: no levels, so it can't be bought.", this);
    }
}
