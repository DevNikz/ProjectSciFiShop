using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Interactable))]
public class ShelfInventory : MonoBehaviour
{
    [SerializeField]
    string shelfName = "Shelf";

    [Tooltip("Empty transforms marking item pivots. One product fits in each slot.")]
    [SerializeField]
    Transform[] slots = Array.Empty<Transform>();

    [Tooltip(
        "Where customers stand to collect products. Place this on the floor in front of the shelf. Falls back to one metre in front of the shelf."
    )]
    [SerializeField]
    Transform customerPickupPoint;

    public Vector3 CustomerPickupPosition =>
        customerPickupPoint != null
            ? customerPickupPoint.position
            : transform.position + transform.forward;

    [Serializable]
    struct AutoFillEntry
    {
        [Tooltip("Product to spawn. Its prefab needs a ShelfProduct.")]
        public ProductDefinition definition;

        [Tooltip("Units to spawn (min 0).")]
        public int count;
    }

    [Header("Debug")]
    [Tooltip("Debug: fill this shelf with the items below when the game starts.")]
    [SerializeField]
    bool autoFillOnStart;

    [Tooltip("Debug: what to spawn, in order, until the shelf is full.")]
    [SerializeField]
    AutoFillEntry[] autoFillItems = Array.Empty<AutoFillEntry>();

    sealed class Entry
    {
        public Carriable item;
        public ShelfProduct product;
        public Transform slot;
        public Transform previousParent;
        public bool gravity;
        public bool kinematic;
        public CustomerAI customer;
    }

    readonly List<Entry> entries = new();
    readonly HashSet<Transform> reserved = new(); // slots claimed by items still flying in from a box
    Interactable interactable;
    public event Action Changed;
    public event Action<string> PlacementRejected;
    public string ShelfName => shelfName;
    public int Count => entries.Count;
    public int Capacity => slots == null ? 0 : slots.Length;

    /// <summary>Available physical stock, excluding items another shopper already reserved.</summary>
    public void GetAvailableProducts(List<ProductCarriable> products)
    {
        foreach (var entry in entries)
            if (
                entry.customer == null
                && entry.item != null
                && entry.item.isActiveAndEnabled
                && entry.item.TryGetComponent<ProductCarriable>(out var product)
                && product.Definition != null
            )
                products.Add(product);
    }

    public bool TryReserveProduct(ProductCarriable product, CustomerAI customer)
    {
        var entry = entries.Find(candidate => candidate.item == product);
        if (!isActiveAndEnabled || customer == null || entry == null || entry.customer != null)
            return false;
        entry.customer = customer;
        return true;
    }

    public bool TryCollectProduct(ProductCarriable product, CustomerAI customer)
    {
        var entry = entries.Find(candidate => candidate.item == product);
        if (!isActiveAndEnabled || entry == null || customer == null || entry.customer != customer)
            return false;
        ReleaseEntry(entry);
        return true;
    }

    public void ReleaseCustomerReservations(CustomerAI customer)
    {
        foreach (var entry in entries)
            if (entry.customer == customer)
                entry.customer = null;
    }

    void Awake() => interactable = GetComponent<Interactable>();

    void OnEnable() => interactable.OnInteractEvent += HandleInteract;

    void Start()
    {
        if (autoFillOnStart)
            AutoFill();
    }

    [ContextMenu("Debug: fill shelf now")]
    void AutoFill()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"{name}: enter Play Mode to fill the shelf.", this);
            return;
        }

        int free = Capacity - Count - reserved.Count;
        int stocked = 0;
        bool stopped = false;
        foreach (var fill in autoFillItems)
        {
            if (stopped)
                break;
            var prefab = fill.definition != null ? fill.definition.Prefab : null;
            if (prefab == null || prefab.GetComponent<ShelfProduct>() == null)
            {
                Debug.LogWarning(
                    $"{name}: auto-fill entry has no definition, prefab or ShelfProduct, skipped.",
                    this
                );
                continue;
            }

            for (int i = 0; i < fill.count; i++)
            {
                if (free <= 0)
                {
                    Debug.LogWarning($"{name}: shelf full, auto-fill stopped.", this);
                    stopped = true;
                    break;
                }
                var item = ProductPool.Spawn(
                    fill.definition,
                    transform.position,
                    Quaternion.identity
                );
                if (item == null)
                {
                    Debug.LogWarning($"{name}: could not spawn '{prefab.name}'.", this);
                    stopped = true;
                    break;
                }
                if (!TryStoreFromBox(item.Carriable))
                {
                    ProductPool.Release(item);
                    Debug.LogWarning(
                        $"{name}: could not stock '{prefab.name}', auto-fill stopped.",
                        this
                    );
                    stopped = true;
                    break;
                }
                free--;
                stocked++;
            }
        }
        Debug.Log($"{name}: auto-fill stocked {stocked} item(s).", this);
    }

    void OnValidate()
    {
        if (autoFillItems == null)
            return;
        for (int i = 0; i < autoFillItems.Length; i++)
        {
            var fill = autoFillItems[i];
            fill.count = Mathf.Max(0, fill.count);
            autoFillItems[i] = fill;

            if (fill.definition == null)
                Debug.LogWarning($"{name}: auto-fill entry {i} has no definition.", this);
            else if (fill.definition.Prefab == null)
                Debug.LogWarning($"{name}: '{fill.definition.name}' has no prefab.", this);
            else if (fill.definition.Prefab.GetComponent<ShelfProduct>() == null)
                Debug.LogWarning(
                    $"{name}: prefab '{fill.definition.Prefab.name}' has no ShelfProduct.",
                    this
                );
        }
    }

    void OnDisable()
    {
        if (interactable != null)
            interactable.OnInteractEvent -= HandleInteract;
        while (entries.Count > 0)
            ReleaseEntry(entries[entries.Count - 1], defer: true); // parent may be deactivating
        reserved.Clear();
    }

    void HandleInteract(Interactor who)
    {
        if (who != null && !who.IsHolding && entries.Count > 0)
            TryTake(entries[entries.Count - 1].item, who);
    }

    public Transform GetPlacementSlot(Carriable item)
    {
        if (
            item == null
            || item.GetComponent<ShelfProduct>() == null
            || item.GetComponent<ShelfProduct>().Shelf != null
        )
            return null;
        foreach (var slot in slots)
            if (
                slot != null
                && !reserved.Contains(slot)
                && !entries.Exists(entry => entry.slot == slot)
            )
                return slot;
        return null;
    }

    /// <summary>Newest stocked item of this product (null productId = any product), else null.</summary>
    public Carriable FindLastStored(string productId)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var entry = entries[i];
            if (entry.item == null || entry.product == null)
                continue;
            if (productId == null || entry.product.ProductId == productId)
                return entry.item;
        }
        return null;
    }

    public Transform GetStoredSlot(ShelfProduct product)
    {
        var entry = entries.Find(candidate => candidate.product == product);
        return entry != null ? entry.slot : null;
    }

    public bool TryStore(Carriable item)
    {
        if (!isActiveAndEnabled || item == null || !item.IsHeld || item.Body == null)
            return false;
        var product = item.GetComponent<ShelfProduct>();
        if (product == null || product.Shelf != null)
        {
            PlacementRejected?.Invoke("This item cannot be stocked");
            return false;
        }
        var slot = GetPlacementSlot(item);
        if (slot == null)
        {
            PlacementRejected?.Invoke("Shelf full");
            return false;
        }

        item.Drop();
        StoreAtSlot(item, product, slot);
        return true;
    }

    /// <summary>Stores an item that was just released from a CardboardBox.</summary>
    public bool TryStoreFromBox(Carriable item)
    {
        if (!isActiveAndEnabled || item == null || item.Body == null)
            return false;
        var product = item.GetComponent<ShelfProduct>();
        var slot = GetPlacementSlot(item);
        if (product == null || slot == null)
            return false;
        StoreAtSlot(item, product, slot);
        return true;
    }

    /// <summary>Claims a free slot for an item about to arrive from a box. Null (and PlacementRejected) if it can't.</summary>
    public Transform TryReserveSlot(Carriable item)
    {
        if (!isActiveAndEnabled || item == null)
            return null;
        var product = item.GetComponent<ShelfProduct>();
        if (product == null || product.Shelf != null)
        {
            PlacementRejected?.Invoke("This item cannot be stocked");
            return null;
        }
        var slot = GetPlacementSlot(item);
        if (slot == null)
        {
            PlacementRejected?.Invoke("Shelf full");
            return null;
        }
        reserved.Add(slot);
        return slot;
    }

    public void CancelReservation(Transform slot)
    {
        if (slot != null)
            reserved.Remove(slot);
    }

    /// <summary>Stores an item that arrived at a slot claimed with TryReserveSlot. The claim is used up either way.</summary>
    public bool TryStoreFromBox(Carriable item, Transform reservedSlot)
    {
        if (reservedSlot == null || !reserved.Remove(reservedSlot))
            return false;
        if (!isActiveAndEnabled || item == null || item.Body == null)
            return false;
        var product = item.GetComponent<ShelfProduct>();
        if (
            product == null
            || product.Shelf != null
            || entries.Exists(entry => entry.slot == reservedSlot)
        )
            return false;
        StoreAtSlot(item, product, reservedSlot);
        return true;
    }

    void StoreAtSlot(Carriable item, ShelfProduct product, Transform slot)
    {
        var body = item.Body;
        var entry = new Entry
        {
            item = item,
            product = product,
            slot = slot,
            previousParent = item.transform.parent,
            gravity = body.useGravity,
            kinematic = body.isKinematic,
        };
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.useGravity = false;
        body.isKinematic = true;
        item.transform.SetParent(slot, false);
        item.transform.localPosition = product.PlacementOffset;
        item.transform.localRotation = product.PlacementRotation;
        product.Shelf = this;
        entries.Add(entry);
        Changed?.Invoke();
    }

    public bool TryTake(Carriable item, Interactor who)
    {
        var entry = entries.Find(candidate => candidate.item == item);
        if (entry == null || item == null || who == null || who.Carrier.AreHandsFull)
            return false;
        item.transform.SetParent(entry.previousParent, true);
        item.Body.isKinematic = entry.kinematic;
        item.Body.useGravity = entry.gravity;
        if (!who.Carrier.TryGrab(item))
        {
            item.Body.isKinematic = true;
            item.Body.useGravity = false;
            item.transform.SetParent(entry.slot, false);
            item.transform.localPosition = entry.product.PlacementOffset;
            item.transform.localRotation = entry.product.PlacementRotation;
            return false;
        }
        entry.product.Shelf = null;
        entries.Remove(entry);
        Changed?.Invoke();
        return true;
    }

    /// <param name="defer">True when called while a parent may be deactivating: the item is put back in the world next frame.</param>
    public void Release(Carriable item, bool defer = false)
    {
        var entry = entries.Find(candidate => candidate.item == item);
        if (entry != null)
            ReleaseEntry(entry, defer);
    }

    void ReleaseEntry(Entry entry, bool defer = false)
    {
        if (entry.product != null)
            entry.product.Shelf = null;
        entries.Remove(entry);
        if (defer)
            NextFrame.Run(() => RestoreItemToWorld(entry)); // SetParent is illegal during deactivation
        else
            RestoreItemToWorld(entry);
        Changed?.Invoke();
    }

    void RestoreItemToWorld(Entry entry)
    {
        if (entry.item == null || entry.item.Body == null)
            return;
        // Taken or stocked again before the deferred step ran: leave it alone.
        if (entry.product != null && entry.product.Shelf != null)
            return;
        entry.item.transform.SetParent(entry.previousParent, true);
        entry.item.Body.isKinematic = entry.kinematic;
        entry.item.Body.useGravity = entry.gravity;
    }

    public void GetStock(Dictionary<string, int> stock, Dictionary<string, string> names)
    {
        stock.Clear();
        names.Clear();
        foreach (var entry in entries)
        {
            if (entry.product == null)
                continue;
            string id = entry.product.ProductId;
            stock.TryGetValue(id, out int count);
            stock[id] = count + 1;
            names[id] = entry.product.DisplayName;
        }
    }

    void LateUpdate()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            if (
                entries[i].item == null
                || entries[i].product == null
                || entries[i].slot == null
                || entries[i].item.IsHeld
                || entries[i].item.IsClaimed
                || entries[i].item.transform.parent != entries[i].slot
            )
                ReleaseEntry(entries[i]);
    }
}
