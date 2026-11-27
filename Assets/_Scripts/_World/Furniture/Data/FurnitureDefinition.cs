using UnityEngine;

/// <summary>
/// Read-only authored data for one piece of furniture. Whether the player has paid its unlock
/// lives in ProgressionManager, never here.
/// </summary>
[CreateAssetMenu(fileName = "Furniture", menuName = "Shop/Furniture Definition")]
public class FurnitureDefinition : ScriptableObject
{
    [Tooltip("Stable unique id used by saves. Never change after release. Empty = asset name.")]
    [SerializeField]
    string id;

    [Tooltip("Shown on the furniture screen. Empty = asset name.")]
    [SerializeField]
    string displayName;

    [SerializeField]
    Sprite icon;

    [Tooltip("Coins to buy one (min 1).")]
    [SerializeField]
    int price = 100;

    [Tooltip("Coins to unlock it once. 0 = unlocked from the start.")]
    [SerializeField]
    int unlockCost;

    [Tooltip(
        "Optional. This expansion must be bought before the furniture can be unlocked or ordered."
    )]
    [SerializeField]
    ExpansionDefinition requiredExpansion;

    [Tooltip(
        "The furniture that gets placed. Needs an ES3 Prefab and an ES3 Auto Save (Easy Save) and a "
            + "PlacedFurniture. The pivot sits on the floor, in the middle."
    )]
    [SerializeField, FolderAsset("Assets/_Prefab")]
    FurnitureCarriable prefab;

    [Tooltip(
        "Half size of the space it takes up (m), for the placement test. Centered above the pivot."
    )]
    [SerializeField]
    Vector3 footprint = new(0.5f, 0.5f, 0.5f);

    public string Id => string.IsNullOrEmpty(id) ? name : id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public int Price => price;
    public int UnlockCost => unlockCost;
    public ExpansionDefinition RequiredExpansion => requiredExpansion;
    public FurnitureCarriable Prefab => prefab;
    public Vector3 Footprint => footprint;

    void OnValidate()
    {
        if (string.IsNullOrEmpty(id)) // lock an id in once, so renaming the asset can't break saves
            id = name;
        price = Mathf.Max(1, price);
        unlockCost = Mathf.Max(0, unlockCost);
        footprint = Vector3.Max(footprint, Vector3.one * 0.05f);

        if (prefab == null)
            Debug.LogWarning($"{name}: no prefab, so it can't be ordered.", this);
        else
        {
            if (prefab.GetComponent<FurnitureCarriable>() == null)
                Debug.LogWarning($"{name}: the prefab has no PlacedFurniture.", this);
        }
    }
}
