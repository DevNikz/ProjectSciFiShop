using UnityEngine;

/// <summary>A CardboardBox that is filled with units of one product, for delivery.</summary>
[DisallowMultipleComponent]
[CarriableAddon("As Delivery Box")]
public class DeliveryBox : CardboardBox
{
    [Tooltip("Product for the 'Fill test' context menu (Play mode only).")]
    [SerializeField]
    ProductDefinition testProduct;

    [Tooltip(
        "Seconds an emptied box waits before it is removed (never while carried or dragged). 0 = keep forever."
    )]
    [SerializeField]
    float emptyRemoveDelay = 5f;

    Carriable _pickupObject;
    float _emptyFor;

    /// <summary>Product this box was filled with. Null until filled.</summary>
    public ProductDefinition Product { get; private set; }

    /// <summary>Units stored by the Fill call.</summary>
    public int Units { get; private set; }

    /// <summary>For loading a save: records what the box was filled with, without spawning anything.</summary>
    public void MarkFilled(ProductDefinition def, int units)
    {
        Product = def;
        Units = Mathf.Max(0, units);
    }

    protected override void Awake()
    {
        base.Awake();
        _pickupObject = GetComponent<Carriable>();
    }

    protected override void Update()
    {
        base.Update();
        TickEmptyRemoval();
    }

    // Removes the box once it has been emptied for a while.
    void TickEmptyRemoval()
    {
        if (emptyRemoveDelay <= 0f || Product == null)
            return;

        bool busy = _pickupObject != null && (_pickupObject.IsHeld || _pickupObject.IsClaimed);
        if (Count > 0 || busy)
        {
            _emptyFor = 0f;
            return;
        }

        _emptyFor += Time.deltaTime;
        if (_emptyFor < emptyRemoveDelay)
            return;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        emptyRemoveDelay = Mathf.Max(0f, emptyRemoveDelay);
    }

    /// <summary>
    /// Spawns up to units of def and stores them. Returns how many fit (0 if refused).
    /// One product per box: a second Fill is refused.
    /// </summary>
    public int Fill(ProductDefinition def, int units)
    {
        if (def == null || units < 1)
            return 0;
        if (def.Prefab == null)
        {
            Debug.LogError($"{name}: '{def.name}' has no prefab.", this);
            return 0;
        }
        if (Product != null || Count > 0)
        {
            Debug.LogError($"{name}: already filled.", this);
            return 0;
        }

        int stored = 0;
        for (int i = 0; i < units; i++)
        {
            ProductCarriable item = Instantiate(def.Prefab, transform.position, transform.rotation);
            if (!TryStoreItem(item.Carriable))
            {
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
                break; // the same product will not fit next time either
            }
            stored++;
        }

        if (stored < units)
            Debug.LogError(
                $"{name}: only {stored}/{units} '{def.name}' fit. Lower Units Per Box.",
                this
            );
        if (stored > 0)
        {
            Product = def;
            Units = stored;
        }
        return stored;
    }

    [ContextMenu("Fill test")]
    void FillTest()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning($"{name}: Fill test only works in Play mode.", this);
            return;
        }
        if (testProduct == null)
        {
            Debug.LogError($"{name}: Test Product missing.", this);
            return;
        }
        int n = Fill(testProduct, testProduct.UnitsPerBox);
        Debug.Log($"{name}: stored {n}/{testProduct.UnitsPerBox} '{testProduct.name}'.", this);
    }
}
