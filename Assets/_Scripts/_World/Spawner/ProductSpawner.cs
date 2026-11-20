using System.Collections;
using Unity.VisualScripting;
using UnityEngine;

public class ProductSpawner : ObjectSpawnerBase<ProductCarriable>
{
    protected override ProductCarriable SpawnPooled(
        ProductCarriable source,
        Vector3 pos,
        Quaternion rot,
        Transform spawnParent
    )
    {
        return ProductPool.Spawn(source.Definition, pos, rot, spawnParent);
    }

    protected override IEnumerator PrewarmPooled(ProductCarriable source, int amount)
    {
        return ProductPool.Prewarm(source.Definition, amount);
    }
}
