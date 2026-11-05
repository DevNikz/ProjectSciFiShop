using UnityEngine;

/// <summary>A carryable product. Which product it is comes from its read-only definition.</summary>
[CarriableAddon("Add as Product")]
public class ProductCarriable : MonoBehaviour
{
    [Header("Product")]
    [Tooltip("What this object is. Overrides the Hotbar icon and name below.")]
    [SerializeField]
    ProductDefinition definition;

    [Tooltip("Fallback displayName if there's no icon in product definition")]
    [SerializeField]
    string displayName;

    [Tooltip("Fallback Icon if there's no icon in product definition")]
    [SerializeField]
    Sprite icon;

    public ProductDefinition Definition => definition;

    public Sprite Icon => definition != null && definition.Icon != null ? definition.Icon : icon;

    public string DisplayName => definition != null ? definition.DisplayName : displayName;

    void Start()
    {
        if (definition == null)
            Debug.LogError($"{name}: ProductDefinition missing.", this);
    }
}
