using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns and returns products by their ProductDefinition. A thin layer over CarriablePool:
/// the definition's Carriable Prefab is the pool key. Use this instead of Instantiate/Destroy for products.
/// </summary>
public static class ProductPool
{
    private static readonly HashSet<ProductDefinition> s_warned = new(); // definitions already reported

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => s_warned.Clear();

    /// <summary>An active product at the pose, parented like Instantiate. Null (and an error) if the definition can't spawn.</summary>
    public static ProductCarriable Spawn(
        ProductDefinition definition,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null
    )
    {
        if (!TryGetPrefab(definition, out Carriable prefab))
            return null;

        var carriable = CarriablePool.Spawn(prefab, position, rotation, parent);
        return carriable != null ? carriable.GetComponent<ProductCarriable>() : null;
    }

    /// <summary>
    /// Returns the product to the pool, or destroys it if the pool can't take it.
    /// A product stored in a CardboardBox must be enabled again (the box lets go) before this call,
    /// or the pool refuses it and it is destroyed. A product on a shelf is taken off it first.
    /// </summary>
    public static void Release(ProductCarriable product)
    {
        if (product == null)
            return;
        // Stocked on a shelf: the shelf lets go first, so it frees the slot and hands the body back.
        if (product.TryGetComponent(out ShelfProduct shelfProduct) && shelfProduct.Shelf != null)
            shelfProduct.Shelf.Release(product.Carriable);
        if (!CarriablePool.TryRelease(product.Carriable))
            Object.Destroy(product.gameObject);
    }

    /// <summary>
    /// Pre-creates each definition's Prewarm Count. Run as a coroutine:
    /// StartCoroutine(ProductPool.Prewarm(definitions)).
    /// </summary>
    public static IEnumerator Prewarm(IReadOnlyList<ProductDefinition> definitions)
    {
        if (definitions == null)
            yield break;
        for (int i = 0; i < definitions.Count; i++)
        {
            var definition = definitions[i];
            if (definition == null || definition.PrewarmCount <= 0)
                continue;
            if (TryGetPrefab(definition, out Carriable prefab))
                yield return CarriablePool.Prewarm(prefab, definition.PrewarmCount);
        }
    }

    // Reports each bad definition once, so a broken one can't flood the console.
    private static bool TryGetPrefab(ProductDefinition definition, out Carriable prefab)
    {
        prefab = null;
        if (definition == null)
        {
            Debug.LogError("ProductPool: no ProductDefinition given.");
            return false;
        }

        prefab = definition.CarriablePrefab;
        if (prefab == null)
            return Reject(definition, "has no Carriable Prefab.");
        if (
            !prefab.TryGetComponent(out ProductCarriable product)
            || product.Definition != definition
        )
            return Reject(
                definition,
                "Carriable Prefab needs a ProductCarriable using this definition."
            );
        return true;
    }

    private static bool Reject(ProductDefinition definition, string reason)
    {
        if (s_warned.Add(definition))
            Debug.LogError($"ProductPool: '{definition.name}' {reason}", definition);
        return false;
    }
}
