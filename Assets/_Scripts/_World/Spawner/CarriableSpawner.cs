using System.Collections;
using UnityEngine;

public class CarriableSpawner : ObjectSpawnerBase<Carriable>
{
    protected override Carriable SpawnPooled(
        Carriable source,
        Vector3 pos,
        Quaternion rot,
        Transform spawnParent
    )
    {
        return CarriablePool.Spawn(source, pos, rot, spawnParent);
    }

    protected override IEnumerator PrewarmPooled(Carriable source, int amount)
    {
        return CarriablePool.Prewarm(source, amount);
    }
}
