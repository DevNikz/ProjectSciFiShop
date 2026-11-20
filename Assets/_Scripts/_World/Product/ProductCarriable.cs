using UnityEngine;

/// <summary>A carryable product. Which product it is comes from its read-only definition.</summary>
[CarriableAddon("As Product")]
[RequireComponent(typeof(Carriable))]
public class ProductCarriable : MonoBehaviour, IHasCarriable
{
    [Header("Product")]
    [Tooltip(
        "What this object is. Overrides the Hotbar icon and name below. Brands that share one prefab "
            + "get theirs from SetDefinition when spawned, so this can stay empty or hold any one of them."
    )]
    [SerializeField]
    ProductDefinition definition;

    [Tooltip("Fallback displayName if there's no icon in product definition")]
    [SerializeField]
    string displayName;

    [Tooltip("Fallback Icon if there's no icon in product definition")]
    [SerializeField]
    Sprite icon;

    Carriable _carriable;
    public Carriable Carriable =>
        _carriable != null ? _carriable : (_carriable = GetComponent<Carriable>());

    public ProductDefinition Definition => definition;

    /// <summary>
    /// Sets which product this unit is. Call it right after spawning a unit from a prefab that several
    /// definitions (brands) share, before anything reads Definition.
    /// </summary>
    public void SetDefinition(ProductDefinition value)
    {
        if (value == null)
        {
            Debug.LogWarning($"{name}: SetDefinition needs a definition.", this);
            return;
        }
        definition = value;
    }

    public Sprite Icon => definition != null && definition.Icon != null ? definition.Icon : icon;

    public string DisplayName => definition != null ? definition.DisplayName : displayName;

    void Start()
    {
        if (definition == null)
            Debug.LogError($"{name}: ProductDefinition missing.", this);
    }
}
