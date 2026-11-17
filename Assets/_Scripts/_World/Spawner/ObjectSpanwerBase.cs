using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ObjectSpawnMode
{
    Burst, // N once
    Interval, // N every X seconds
}

/// <summary>
/// Spawns objects of type T around this object. T is the component the prefab must have, so each
/// child class gets its own typed prefab slot (PickupObject, Carrier, Interactable...).
/// Children override the two pool hooks when their type has a pool; the default is plain Instantiate.
/// </summary>
public abstract class ObjectSpawnerBase<T> : MonoBehaviour
    where T : Component
{
    [Header("Spawn")]
    [Tooltip("Currency prefab to spawn.")]
    [SerializeField]
    private T prefab;

    [Tooltip("Parent of spawned objects. Empty = scene root.")]
    [SerializeField]
    private Transform parent;

    [Tooltip("Burst = N at once. Interval = N every X seconds.")]
    [SerializeField]
    private ObjectSpawnMode mode = ObjectSpawnMode.Burst;

    [Tooltip("Objects per spawn (N).")]
    [SerializeField, Min(1)]
    private int count = 5;

    [Tooltip("Seconds between spawns (Interval mode).")]
    [SerializeField, Min(0.05f)]
    private float interval = 2f;

    [Tooltip("Random scatter radius around this object, flat (m).")]
    [SerializeField, Min(0f)]
    private float scatterRadius = 0.5f;

    [Tooltip("Run automatically on Start (play mode).")]
    [SerializeField]
    private bool playOnStart = true;

    [Header("Pooling")]
    [Tooltip("Instances to pre-create at Start so later spawns do not hitch. 0 = grow on demand.")]
    [SerializeField, Min(0)]
    private int prewarm = 50;

    [Tooltip("Spawn at most this many per frame. 0 = all at once.")]
    [SerializeField, Min(0)]
    private int maxPerFrame;

    private Coroutine _loop;

    public int Count => count;
    public bool IsRunning => _loop != null;

    void Start()
    {
        if (prewarm > 0 && prefab != null)
            StartCoroutine(PrewarmPooled(prefab, prewarm));
        if (playOnStart)
            Run();
    }

    void OnDisable() => Stop();

    /// <summary>Spawns one instance in play mode. Override to use a pool. Default: Instantiate.</summary>
    protected virtual T SpawnPooled(T source, Vector3 pos, Quaternion rot, Transform spawnParent) =>
        Instantiate(source, pos, rot, spawnParent);

    /// <summary>Pre-creates pooled instances. Override to use a pool. Default: nothing.</summary>
    protected virtual IEnumerator PrewarmPooled(T source, int amount)
    {
        yield break;
    }

    /// <summary>Start the configured mode.</summary>
    public void Run()
    {
        if (!HasPrefab())
            return;
        if (mode == ObjectSpawnMode.Burst)
        {
            if (maxPerFrame > 0 && count > maxPerFrame)
            {
                if (_loop == null)
                    _loop = StartCoroutine(BurstLoop());
            }
            else
                Spawn(count);
        }
        else if (_loop == null)
            _loop = StartCoroutine(IntervalLoop());
    }

    /// <summary>Stop Interval spawning. Safe anytime.</summary>
    public void Stop()
    {
        if (_loop == null)
            return;
        StopCoroutine(_loop);
        _loop = null;
    }

    /// <summary>Spawn n objects now. Returns how many were made.</summary>
    public int Spawn(int n)
    {
        if (!HasPrefab())
            return 0;
        int made = 0;
        for (int i = 0; i < n; i++)
            if (Create() != null)
                made++;
        return made;
    }

    // Burst over several frames when it is bigger than maxPerFrame.
    private IEnumerator BurstLoop()
    {
        int left = count;
        while (left > 0)
        {
            int step = Mathf.Min(left, maxPerFrame);
            Spawn(step);
            left -= step;
            yield return null;
        }
        _loop = null;
    }

    private IEnumerator IntervalLoop()
    {
        while (true)
        {
            int left = count;
            while (left > 0)
            {
                int step = maxPerFrame > 0 ? Mathf.Min(left, maxPerFrame) : left;
                Spawn(step);
                left -= step;
                if (left > 0)
                    yield return null;
            }
            yield return new WaitForSeconds(interval);
        }
    }

    private bool HasPrefab()
    {
        if (prefab != null)
            return true;
        Debug.LogWarning($"{name}: currency prefab missing.", this);
        return false;
    }

    private T Create()
    {
        Vector2 o = Random.insideUnitCircle * scatterRadius;
        Vector3 parentPos = parent ? parent.position : transform.position;
        Vector3 pos = parentPos + new Vector3(o.x, 0f, o.y);
        Quaternion rot = Random.rotation;

#if UNITY_EDITOR
        // Edit mode: keep the prefab link and allow undo.
        if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            var made = (T)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (made == null)
                return null;
            made.transform.SetPositionAndRotation(pos, rot);
            Undo.RegisterCreatedObjectUndo(made.gameObject, "Spawn Currency");
            return made;
        }
#endif
        if (!Application.isPlaying)
            return Instantiate(prefab, pos, rot, parent); // no pool objects in edit mode
        return SpawnPooled(prefab, pos, rot, parent);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, scatterRadius);
    }
}
