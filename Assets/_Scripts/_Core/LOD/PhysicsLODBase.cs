using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

/// <summary>
/// Freezes settled bodies far from the focus and wakes them when it comes near.
/// Fed by Interactable.Enabled / Disabled. A subclass says which Interactables it owns (T),
/// where the body is, when to skip an item, and where the frozen flag lives.
/// </summary>
public abstract class PhysicsLODBase<T> : MonoBehaviour
    where T : Component
{
    [Tooltip("Measured from here. Empty = main camera.")]
    [SerializeField]
    private Transform focus;

    [Tooltip("Frozen pickups closer than this wake up (m). Keep well above the interact range.")]
    [SerializeField]
    private float thawRadius = 10f;

    [Tooltip("Sleeping pickups farther than this freeze (m). At least the thaw radius.")]
    [SerializeField]
    private float freezeRadius = 15f;

    [Tooltip("Seconds for one pass over every pickup.")]
    [SerializeField]
    private float scanSeconds = 0.25f;

    [Tooltip("Max freezes and thaws per frame.")]
    [SerializeField, Min(1)]
    private int maxChangesPerFrame = 32;

    private readonly List<Interactable> _sources = new(); // parallel to _items

    [SerializeField, ReadOnly]
    private List<T> _items = new();
    private readonly Dictionary<Interactable, int> _index = new(); // source -> slot
    private int _cursor;

    /// <summary>The item this LOD owns on this Interactable. False = not ours.</summary>
    protected abstract bool TryGetItem(Interactable source, out T item);

    /// <summary>The body to freeze. May be null (e.g. before the item's Awake): the item is skipped.</summary>
    protected abstract Rigidbody GetBody(T item);

    /// <summary>Held, claimed or otherwise driven by something else: never freeze or thaw.</summary>
    protected abstract bool IsBusy(T item);

    protected abstract bool IsFrozen(T item);

    protected abstract void SetFrozen(T item, bool frozen);

    /// <summary>Runs after an item leaves the list. Default does nothing.</summary>
    protected virtual void OnItemRemoved(T item) { }

    void Awake()
    {
        if (focus == null && Camera.main != null)
            focus = Camera.main.transform;
        if (focus == null)
        {
            Debug.LogError($"{name}: no focus. Assign a transform.", this);
            enabled = false;
        }
    }

    void OnEnable()
    {
        Interactable.Enabled += AddItem; // subscribe first so nothing is missed during the seed
        Interactable.Disabled += RemoveItem;
        SeedExisting();
    }

    void OnDisable()
    {
        Interactable.Enabled -= AddItem;
        Interactable.Disabled -= RemoveItem;
        for (int i = 0; i < _items.Count; i++) // never leave pickups frozen
        {
            var item = _items[i];
            if ((object)item != null && item != null && IsFrozen(item))
                Thaw(item, GetBody(item));
        }
        _sources.Clear();
        _items.Clear();
        _index.Clear();
        _cursor = 0;
    }

    void Update()
    {
        int total = _items.Count;
        if (total == 0)
            return;

        int steps = Mathf.Clamp(Mathf.CeilToInt(total * Time.deltaTime / scanSeconds), 1, total);
        Vector3 at = focus.position;
        float thawSqr = thawRadius * thawRadius;
        float freezeSqr = freezeRadius * freezeRadius;
        int changes = 0;

        for (int k = 0; k < steps; k++)
        {
            if (_cursor >= _items.Count)
                _cursor = 0;
            var item = _items[_cursor++];
            if ((object)item == null || item == null || IsBusy(item))
                continue;
            var rb = GetBody(item);
            if (rb == null)
                continue;

            float sqr = (rb.position - at).sqrMagnitude;
            if (IsFrozen(item))
            {
                if (sqr > thawSqr)
                    continue;
                Thaw(item, rb);
            }
            else
            {
                if (sqr < freezeSqr || rb.isKinematic || !rb.IsSleeping())
                    continue;
                rb.isKinematic = true;
                SetFrozen(item, true);
            }
            if (++changes >= maxChangesPerFrame)
                break;
        }
    }

    // Interactables enabled before this component are not announced by the static event.
    void SeedExisting()
    {
        var found = FindObjectsByType<Interactable>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );
        for (int i = 0; i < found.Length; i++)
            if (found[i].isActiveAndEnabled)
                AddItem(found[i]);
    }

    void AddItem(Interactable source)
    {
        if ((object)source == null || _index.ContainsKey(source))
            return;
        if (!TryGetItem(source, out T item))
            return;
        _index[source] = _items.Count;
        _sources.Add(source);
        _items.Add(item);
    }

    // Swap-remove: O(1). Keyed by the source, so a destroyed item is still removable.
    void RemoveItem(Interactable source)
    {
        if ((object)source == null || !_index.TryGetValue(source, out int slot))
            return;
        int last = _items.Count - 1;
        var item = _items[slot];
        _items[slot] = _items[last];
        _sources[slot] = _sources[last];
        _index[_sources[slot]] = slot;
        _items.RemoveAt(last);
        _sources.RemoveAt(last);
        _index.Remove(source);
        OnItemRemoved(item);
    }

    void Thaw(T item, Rigidbody rb)
    {
        SetFrozen(item, false);
        if (rb == null)
            return;
        rb.isKinematic = false;
        rb.WakeUp();
    }

    void OnValidate()
    {
        thawRadius = Mathf.Max(0.1f, thawRadius);
        freezeRadius = Mathf.Max(thawRadius, freezeRadius);
        scanSeconds = Mathf.Max(0.05f, scanSeconds);
    }
}
