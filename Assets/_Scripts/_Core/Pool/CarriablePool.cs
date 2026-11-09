using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CarriablePool
{
    // Rigidbody values that carry, drag and view lock change at runtime. Release puts them back.
    private struct BodyDefaults
    {
        public bool IsKinematic,
            UseGravity,
            DetectCollisions;
        public RigidbodyInterpolation Interpolation;
        public float AngularDamping;
    }

    private sealed class Pool
    {
        public Carriable Prefab;
        public BodyDefaults Body;
        public Vector3 Scale;
        public int Prewarmed;
        public bool WarnedGrow;
        public readonly Stack<Item> Free = new();
    }

    private sealed class Item
    {
        public Carriable Pickup;
        public Transform Tr;
        public Rigidbody Rb;
        public IPoolable[] Poolables;
        public Pool Owner;
        public bool InUse;
    }

    private static readonly Dictionary<Carriable, Pool> s_pools = new(); // prefab -> pool
    private static readonly Dictionary<Carriable, Item> s_items = new(); // instance -> item
    private static readonly HashSet<Carriable> s_skip = new(); // prefabs that are not pooled
    private static Transform s_root; // inactive parent of idle instances

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        s_pools.Clear();
        s_items.Clear();
        s_skip.Clear();
        s_root = null;
    }

    /// <summary>False for prefabs the pool leaves alone (boxes own their contents).</summary>
    public static bool CanPool(Carriable prefab)
    {
        if (prefab == null)
            return false;
        if (s_pools.ContainsKey(prefab))
            return true;
        if (s_skip.Contains(prefab))
            return false;
        // if (prefab.GetComponent<CardboardBox>() != null)
        // {
        //     s_skip.Add(prefab);
        //     return false;
        // }
        return true;
    }

    /// <summary>
    /// Pre-creates instances without a frame spike. Run it as a coroutine:
    /// StartCoroutine(PickupPool.Prewarm(prefab, n)).
    /// </summary>
    public static IEnumerator Prewarm(Carriable prefab, int count)
    {
        if (count <= 0 || !CanPool(prefab))
            yield break;

        EnsureRoot();
        var pool = GetPool(prefab);
        var op = Object.InstantiateAsync(prefab, count, s_root);
        while (!op.isDone)
            yield return null;

        // Scene unloaded or pool reset while waiting: the new instances went with it.
        if (s_root == null || !s_pools.TryGetValue(prefab, out var current) || current != pool)
            yield break;

        var made = op.Result;
        if (made == null)
            yield break;
        for (int i = 0; i < made.Length; i++)
        {
            if ((object)made[i] == null || made[i] == null)
                continue;
            pool.Free.Push(Adopt(pool, made[i]));
            pool.Prewarmed++;
        }
    }

    /// <summary>An active instance at the pose, parented like Instantiate(prefab, pos, rot, parent).</summary>
    public static Carriable Spawn(
        Carriable prefab,
        Vector3 position,
        Quaternion rotation,
        Transform parent = null
    )
    {
        if (prefab == null)
            return null;
        if (!CanPool(prefab))
            return Object.Instantiate(prefab, position, rotation, parent);

        EnsureRoot();
        var pool = GetPool(prefab);
        var item = TakeFree(pool) ?? Grow(pool);
        if (item == null)
            return null;

        item.InUse = true;
        var tr = item.Tr;
        tr.SetParent(parent, false);
        tr.SetPositionAndRotation(position, rotation);
        if (parent == null) // Instantiate would put it in the active scene
        {
            var scene = SceneManager.GetActiveScene();
            if (tr.gameObject.scene != scene)
                SceneManager.MoveGameObjectToScene(tr.gameObject, scene);
        }

        for (int i = 0; i < item.Poolables.Length; i++)
            item.Poolables[i].OnPoolSpawn();
        tr.gameObject.SetActive(true);
        return item.Pickup;
    }

    /// <summary>
    /// Takes a pickup back. True = handled (returned now, or already back). False = the caller
    /// destroys it: not from this pool, or it is stored in a box and the box must let go first.
    /// </summary>
    public static bool TryRelease(Carriable pickup)
    {
        if (pickup == null || s_root == null || !s_items.TryGetValue(pickup, out var item))
            return false;
        if (!item.InUse)
            return true; // already back in the pool
        if (!pickup.enabled) // stored in a CardboardBox
        {
            s_items.Remove(pickup);
            return false;
        }

        item.InUse = false;
        item.Tr.gameObject.SetActive(false); // OnDisable ends any carry/drag and drops listeners
        for (int i = item.Poolables.Length - 1; i >= 0; i--)
            item.Poolables[i].OnPoolRelease();

        ResetBody(item);
        item.Tr.localScale = item.Owner.Scale;
        item.Tr.SetParent(s_root, false);
        item.Owner.Free.Push(item);
        return true;
    }

    static void EnsureRoot()
    {
        if (s_root != null)
            return;
        // First use, or the root's scene was unloaded and took every instance with it.
        s_pools.Clear();
        s_items.Clear();
        var go = new GameObject("PickupPool");
        go.SetActive(false);
        s_root = go.transform;
    }

    static Pool GetPool(Carriable prefab)
    {
        if (s_pools.TryGetValue(prefab, out var pool))
            return pool;

        pool = new Pool { Prefab = prefab, Scale = prefab.transform.localScale };
        var rb = prefab.GetComponent<Rigidbody>();
        if (rb != null)
            pool.Body = new BodyDefaults
            {
                IsKinematic = rb.isKinematic,
                UseGravity = rb.useGravity,
                DetectCollisions = rb.detectCollisions,
                Interpolation = rb.interpolation,
                AngularDamping = rb.angularDamping,
            };
        s_pools.Add(prefab, pool);
        return pool;
    }

    static Item TakeFree(Pool pool)
    {
        while (pool.Free.Count > 0)
        {
            var item = pool.Free.Pop();
            if ((object)item.Pickup != null && item.Pickup != null) // skip destroyed
                return item;
        }
        return null;
    }

    static Item Grow(Pool pool)
    {
        if (pool.Prewarmed > 0 && !pool.WarnedGrow)
        {
            pool.WarnedGrow = true;
            Debug.LogWarning(
                $"PickupPool: '{pool.Prefab.name}' ran past its prewarm of {pool.Prewarmed}. Raise the prewarm count.",
                pool.Prefab
            );
        }
        var made = Object.Instantiate(pool.Prefab, s_root);
        return made != null ? Adopt(pool, made) : null;
    }

    // Registers a new instance and parks it deactivated; Spawn activates it with its final pose.
    static Item Adopt(Pool pool, Carriable pickup)
    {
        pickup.gameObject.SetActive(false);
        var item = new Item
        {
            Pickup = pickup,
            Tr = pickup.transform,
            Rb = pickup.GetComponent<Rigidbody>(),
            Poolables = pickup.GetComponentsInChildren<IPoolable>(true),
            Owner = pool,
        };
        s_items[pickup] = item;
        return item;
    }

    static void ResetBody(Item item)
    {
        var rb = item.Rb;
        if (rb == null)
            return;
        var d = item.Owner.Body;
        rb.isKinematic = d.IsKinematic; // first: velocity can't be set on a kinematic body
        rb.useGravity = d.UseGravity;
        rb.detectCollisions = d.DetectCollisions;
        rb.interpolation = d.Interpolation;
        rb.angularDamping = d.AngularDamping;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
