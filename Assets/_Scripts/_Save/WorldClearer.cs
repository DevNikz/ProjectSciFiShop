using ES3Internal;
using UnityEngine;

/// <summary>
/// Removes everything the game spawned (boxes, products, coins, furniture), so a loaded save does not
/// add to the old world. Fixed scene objects such as shelves stay: they have no ES3Prefab.
/// </summary>
public static class WorldClearer
{
    /// <summary>Releases to the pool or destroys every spawned object. Returns how many were removed.</summary>
    public static int ClearSpawned()
    {
        var markers = Object.FindObjectsByType<ES3Prefab>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );
        int removed = 0;
        foreach (var marker in markers)
        {
            if (marker == null)
                continue;
            var go = marker.gameObject;
            var carriable = go.GetComponent<Carriable>();
            if (carriable != null && CarriablePool.IsPooledIdle(carriable))
                continue; // an unused pool unit is not part of the world

            var product = go.GetComponent<ProductCarriable>();
            if (product != null)
                ProductPool.Release(product);
            else if (carriable == null || !CarriablePool.TryRelease(carriable))
            {
                go.SetActive(false); // gone right away, Destroy waits for the end of the frame
                Object.Destroy(go);
            }
            removed++;
        }
        return removed;
    }
}
