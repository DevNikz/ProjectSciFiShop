using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saves what is stored in a CardboardBox or DeliveryBox (and what a delivery box was filled with).
/// Put it on the box prefab and add it to the prefab's ES3 Auto Save "Components To Save".
/// </summary>
[RequireComponent(typeof(CardboardBox))]
public class BoxSaveAdapter : SaveParticipant
{
    // Saved by Easy Save (public fields of a listed component).
    public List<Carriable> items = new();
    public ProductDefinition filledProduct;
    public int filledUnits;

    CardboardBox _box;
    DeliveryBox _delivery;

    protected override void Awake()
    {
        base.Awake();
        _box = GetComponent<CardboardBox>();
        _delivery = _box as DeliveryBox;
    }

    public override void BeforeSave()
    {
        _box.GetItems(items);
        filledProduct = _delivery != null ? _delivery.Product : null;
        filledUnits = _delivery != null ? _delivery.Units : 0;
    }

    public override void AfterLoad()
    {
        foreach (var item in items)
        {
            if (item == null)
                continue;
            if (!_box.TryStoreItem(item))
                Debug.LogWarning($"{name}: could not put '{item.name}' back in the box.", this);
        }
        if (_delivery != null && filledProduct != null)
            _delivery.MarkFilled(filledProduct, filledUnits);
    }
}
