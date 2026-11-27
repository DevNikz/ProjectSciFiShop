using UnityEngine;

public enum ProductCategory
{
    Other,
    Produce,
    Dairy,
    Drinks,
    Snacks,
    Household,
    Canned,
}

/// <summary>
/// Read-only authored data for one kind of product.
/// Writable values (sell price etc.) live in runtime state, never here.
/// </summary>
[CreateAssetMenu(fileName = "Product", menuName = "Shop/Product Definition")]
public class ProductDefinition : ScriptableObject
{
    [Tooltip(
        "Stable unique id used by saves. Never change after release. Empty = asset name. "
            + "The Product Group's id is added automatically as '_{group id}'."
    )]
    [SerializeField]
    string id;

    [Tooltip(
        "Shown on the hotbar and scanner. Empty = asset name. "
            + "The Product Group's name is added automatically as '_{group name}'."
    )]
    [SerializeField]
    string displayName;

    [Tooltip("Hotbar and scanner icon. Empty = shows Display Name instead.")]
    [SerializeField]
    Sprite icon;

    [SerializeField]
    ProductCategory category = ProductCategory.Other;

    [Tooltip(
        "The item this product is a brand of (e.g. Milk). Empty = it is its own item, so customers have no brand choice for it."
    )]
    [SerializeField]
    ProductGroup group;

    [Tooltip("Brand name shown in the shop. Empty = Display Name.")]
    [SerializeField]
    string brand;

    [Tooltip("Starting market price (coins, min 1).")]
    [SerializeField]
    int baseMarketPrice = 1;

    [Tooltip("What the shop pays per unit when ordering (coins, min 1).")]
    [SerializeField]
    int unitCost = 1;

    [Tooltip("Units in one delivery box (min 1). Check it fits with the box's Fill test.")]
    [SerializeField]
    int unitsPerBox = 1;

    [Tooltip("Coins to unlock this product for ordering. 0 = available from the start.")]
    [SerializeField]
    int unlockCost;

    [Tooltip(
        "Prefab spawned for each unit. Several definitions (brands) can share one prefab: the spawner sets the definition on each unit."
    )]
    [SerializeField, FolderAsset("Assets/_Prefab/World/Product")]
    ProductCarriable prefab;

    [Tooltip("Instances created at scene start. 0 = grow on demand.")]
    [SerializeField, Min(0)]
    int prewarmCount;

    // Group-suffix bookkeeping (see SyncAffixed). Not for designers.
    [SerializeField, HideInInspector]
    string baseId;

    [SerializeField, HideInInspector]
    string baseDisplayName;

    [SerializeField, HideInInspector]
    string appliedId;

    [SerializeField, HideInInspector]
    string appliedDisplayName;

    [SerializeField, HideInInspector]
    string baseAssetName;

    [SerializeField, HideInInspector]
    string appliedAssetName;

    public Carriable CarriablePrefab => prefab.Carriable;
    public int PrewarmCount => prewarmCount;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public ProductCategory Category => category;
    public ProductGroup Group => group;
    public string Brand => string.IsNullOrEmpty(brand) ? DisplayName : brand;

    /// <summary>What customers compare to decide two products are the same item: the group, else this product.</summary>
    public UnityEngine.Object GroupKey => group != null ? (UnityEngine.Object)group : this;
    public int BaseMarketPrice => baseMarketPrice;
    public int UnitCost => unitCost;
    public int UnitsPerBox => unitsPerBox;
    public int UnlockCost => unlockCost;
    public ProductCarriable Prefab => prefab;

    void OnValidate()
    {
        baseMarketPrice = Mathf.Max(1, baseMarketPrice);
        unitCost = Mathf.Max(1, unitCost);
        unitsPerBox = Mathf.Max(1, unitsPerBox);
        unlockCost = Mathf.Max(0, unlockCost);
        if (unitCost >= baseMarketPrice)
            Debug.LogWarning($"{name}: unit cost is not below the market price (no margin).", this);
        if (group == null)
            Debug.LogWarning(
                $"{name}: no Product Group, so it is its own item and customers have no brand choice for it.",
                this
            );

        string idSuffix = group != null ? group.Id : "";
        string nameSuffix = group != null ? group.DisplayName : "";

        // The asset name takes the same suffix as the id. Its base (no suffix) is the fallback below.
        string assetName = name;
        SyncAffixed(ref assetName, ref baseAssetName, ref appliedAssetName, idSuffix, name);

        string idBefore = id;
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = baseAssetName;

        SyncAffixed(ref id, ref baseId, ref appliedId, idSuffix, baseAssetName);
        SyncAffixed(
            ref displayName,
            ref baseDisplayName,
            ref appliedDisplayName,
            nameSuffix,
            baseAssetName
        );

        if (!string.IsNullOrEmpty(idBefore) && id != idBefore)
            Debug.LogWarning(
                $"{name}: id changed from '{idBefore}' to '{id}'. Saves using the old id will not match.",
                this
            );

        RenameAssetTo(assetName);
    }

    // Asset renames can't run inside OnValidate (the import is not allowed there), so it waits one editor tick.
    void RenameAssetTo(string newName)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(newName) || newName == name)
            return;
        if (string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(this)))
            return; // not a saved asset

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || name == newName)
                return;
            string error = UnityEditor.AssetDatabase.RenameAsset(
                UnityEditor.AssetDatabase.GetAssetPath(this),
                newName
            );
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning(
                    $"{name}: could not rename the asset to '{newName}' ({error}).",
                    this
                );
        };
#endif
    }

    // Keeps field = "{base}_{suffix}" (or just base when there is no suffix). When the field no longer
    // equals what was last applied, the designer edited it: re-base on their text, minus a trailing suffix.
    static void SyncAffixed(
        ref string field,
        ref string baseValue,
        ref string applied,
        string suffix,
        string fallback
    )
    {
        bool hasSuffix = !string.IsNullOrEmpty(suffix);

        if (field != applied)
        {
            baseValue = field ?? "";
            string tail = "_" + suffix;
            if (hasSuffix && baseValue.EndsWith(tail))
                baseValue = baseValue.Substring(0, baseValue.Length - tail.Length);
        }
        if (string.IsNullOrEmpty(baseValue) && hasSuffix)
            baseValue = fallback;

        field = hasSuffix ? $"{baseValue}_{suffix}" : baseValue ?? "";
        applied = field;
    }
}
