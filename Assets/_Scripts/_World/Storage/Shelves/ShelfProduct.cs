using Unity.VisualScripting;
using UnityEngine;

/// <summary>Marks a PickupObject as an item that can be stocked on a ShelfInventory.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Carriable))]
public class ShelfProduct : MonoBehaviour
{
    [Tooltip("Optional local offset from the assigned shelf slot.")]
    [SerializeField]
    Vector3 placementOffset;

    [Tooltip("Optional rotation, relative to the assigned shelf slot.")]
    [SerializeField]
    Vector3 placementRotation;

    [Tooltip("Used only when this is not a ProductPickup.")]
    [SerializeField]
    string fallbackDisplayName;

    public ShelfInventory Shelf { get; internal set; }
    public Vector3 PlacementOffset => placementOffset;
    public Quaternion PlacementRotation => Quaternion.Euler(placementRotation);

    public string ProductId
    {
        get
        {
            var product = GetComponent<ProductCarriable>();
            return product != null && product.Definition != null
                ? product.Definition.Id
                : gameObject.name;
        }
    }

    public string DisplayName
    {
        get
        {
            var product = GetComponent<ProductCarriable>();
            string value =
                product != null && product.Definition != null ? product.Definition.DisplayName
                : string.IsNullOrWhiteSpace(fallbackDisplayName)
                    ? GetComponent<HotbarItem>().Definition.DisplayName
                : fallbackDisplayName;
            return Humanize(value);
        }
    }

    static string Humanize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Product";
        value = value.Replace('_', ' ').Replace('-', ' ');
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
            value.ToLowerInvariant()
        );
    }

    void OnDisable()
    {
        if (Shelf != null)
            Shelf.Release(GetComponent<Carriable>(), defer: true); // parent may be deactivating
    }
}
