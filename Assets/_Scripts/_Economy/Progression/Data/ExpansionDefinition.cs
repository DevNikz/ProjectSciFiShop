using UnityEngine;

/// <summary>
/// Read-only authored data for one store expansion (an extra room or area). What it actually
/// switches on in the scene is set up on an ExpansionArea that points at this asset.
/// </summary>
[CreateAssetMenu(fileName = "Expansion", menuName = "Shop/Expansion Definition")]
public class ExpansionDefinition : ScriptableObject
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

    [Tooltip("Coins to buy it (min 1).")]
    [SerializeField]
    int cost = 100;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public string Description => description;
    public int Cost => cost;

    void OnValidate()
    {
        cost = Mathf.Max(1, cost);
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = name;
    }
}
