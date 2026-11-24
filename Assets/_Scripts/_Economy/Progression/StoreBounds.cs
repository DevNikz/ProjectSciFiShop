using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// One walkable region of the store, drawn as a box on the floor plan (height is ignored when testing
/// points). Add one for the base store and one per expansion room. A region with an Expansion only counts
/// once ProgressionManager says it is bought; with none it is always open. Customers browse and wander only
/// inside open regions. Put it on an object that stays active: an inactive object never registers.
/// With no StoreBounds in the scene, the whole scene counts as the store.
/// </summary>
public class StoreBounds : MonoBehaviour
{
    static readonly List<StoreBounds> _all = new();

    /// <summary>Every enabled StoreBounds in the scene, open or not.</summary>
    public static IReadOnlyList<StoreBounds> All => _all;

    [Tooltip(
        "Leave empty for the base store (always open). Otherwise open once this expansion is bought."
    )]
    [SerializeField]
    ExpansionDefinition expansion;

    [Tooltip(
        "Box size in metres, centred on this object and turned with it. Width and depth are the floor area; height is only for the gizmo."
    )]
    [SerializeField]
    Vector3 size = new(8f, 3f, 8f);

    ProgressionManager _progression;
    bool _isOpen;

    public bool IsOpen => _isOpen;

    float Area => Mathf.Abs(size.x * transform.lossyScale.x * size.z * transform.lossyScale.z);

    void Awake() => _isOpen = expansion == null;

    void OnEnable() => _all.Add(this);

    void OnDisable() => _all.Remove(this);

    // Start, not OnEnable: every manager has set its Instance by now.
    void Start()
    {
        if (expansion == null)
            return;

        _progression = ProgressionManager.Instance;
        if (_progression == null)
        {
            Debug.LogWarning(
                $"{name}: no ProgressionManager in the scene, so the area is open.",
                this
            );
            _isOpen = true;
            return;
        }

        _progression.Changed += HandleChanged;
        HandleChanged(); // a load may already have happened
    }

    void OnDestroy()
    {
        if (_progression != null)
            _progression.Changed -= HandleChanged;
    }

    void HandleChanged() => _isOpen = _progression.IsExpansionBought(expansion);

    /// <summary>True if the point is inside any open region. True for everything when the scene has no StoreBounds.</summary>
    public static bool IsInsideStore(Vector3 point)
    {
        if (_all.Count == 0)
            return true;
        foreach (var region in _all)
            if (region._isOpen && region.Contains(point))
                return true;
        return false;
    }

    /// <summary>A random point in the open regions, bigger regions more likely. False if none is open or no point fits the NavMesh.</summary>
    public static bool TryGetRandomPoint(bool onNavMesh, out Vector3 point)
    {
        point = default;
        float total = 0f;
        foreach (var region in _all)
            if (region._isOpen)
                total += region.Area;
        if (total <= 0f)
            return false;

        float roll = Random.value * total;
        StoreBounds picked = null;
        foreach (var region in _all)
        {
            if (!region._isOpen)
                continue;
            picked = region;
            roll -= region.Area;
            if (roll <= 0f)
                break;
        }
        return picked != null && picked.TryGetPointInside(onNavMesh, out point);
    }

    /// <summary>The nearest point inside any open region. False if none is open or that point is off the NavMesh.</summary>
    public static bool TryGetClosestPoint(Vector3 from, bool onNavMesh, out Vector3 point)
    {
        point = default;
        float best = float.MaxValue;
        bool found = false;
        foreach (var region in _all)
        {
            if (!region._isOpen)
                continue;
            var candidate = region.ClosestPoint(from);
            float distance = (candidate - from).sqrMagnitude;
            if (distance >= best)
                continue;
            best = distance;
            point = candidate;
            found = true;
        }
        if (found && onNavMesh)
        {
            if (!NavMesh.SamplePosition(point, out var hit, 2f, NavMesh.AllAreas))
                return false;
            point = hit.position;
        }
        return found;
    }

    bool Contains(Vector3 point)
    {
        var local = transform.InverseTransformPoint(point);
        return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
    }

    Vector3 ClosestPoint(Vector3 point)
    {
        var local = transform.InverseTransformPoint(point);
        local.x = Mathf.Clamp(local.x, -size.x * 0.5f, size.x * 0.5f);
        local.z = Mathf.Clamp(local.z, -size.z * 0.5f, size.z * 0.5f);
        local.y = 0f;
        return transform.TransformPoint(local);
    }

    bool TryGetPointInside(bool onNavMesh, out Vector3 point)
    {
        const int attempts = 8;
        float reach = size.y * 0.5f * transform.lossyScale.y + 1f;
        for (int i = 0; i < attempts; i++)
        {
            var local = new Vector3(
                Random.Range(-size.x * 0.5f, size.x * 0.5f),
                0f,
                Random.Range(-size.z * 0.5f, size.z * 0.5f)
            );
            point = transform.TransformPoint(local);
            if (!onNavMesh)
                return true;
            if (NavMesh.SamplePosition(point, out var hit, reach, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }
        point = default;
        return false;
    }

    void OnValidate() => size = Vector3.Max(size, Vector3.one * 0.1f);

    void OnDrawGizmos()
    {
        bool open = Application.isPlaying ? _isOpen : expansion == null;
        Gizmos.color = open ? Color.cyan : Color.gray;
        var previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.matrix = previous;
    }
}
