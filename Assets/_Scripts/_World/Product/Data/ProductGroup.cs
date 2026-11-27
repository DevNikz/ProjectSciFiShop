using UnityEngine;

/// <summary>
/// Read-only authored data for one kind of item, such as "Milk". Each brand of it is a
/// ProductDefinition that points here. Customers want an item and may prefer one brand.
/// </summary>
[CreateAssetMenu(fileName = "ProductGroup", menuName = "Shop/Product Group")]
public class ProductGroup : ScriptableObject
{
    [Tooltip("Stable unique id used by saves. Never change after release. Empty = asset name.")]
    [SerializeField]
    string id;

    [Tooltip("Shown to the player. Empty = asset name.")]
    [SerializeField]
    string displayName;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = name;
    }
}
