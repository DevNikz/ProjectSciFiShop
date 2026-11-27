using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saves what is stocked on a shelf and in which slot. Put it next to ShelfInventory and add it to that
/// object's ES3 Auto Save "Components To Save". The stocked products are saved by their own ES3 Auto Save.
/// </summary>
[RequireComponent(typeof(ShelfInventory))]
public class ShelfSaveAdapter : SaveParticipant
{
    // Saved by Easy Save (public fields of a listed component).
    public List<Carriable> items = new();
    public List<int> slotIndices = new();

    ShelfInventory _shelf;

    protected override void Awake()
    {
        base.Awake();
        _shelf = GetComponent<ShelfInventory>();
    }

    public override void BeforeSave() => _shelf.GetStored(items, slotIndices);

    public override void AfterLoad()
    {
        int count = Mathf.Min(items.Count, slotIndices.Count);
        for (int i = 0; i < count; i++)
        {
            if (items[i] == null)
                continue;
            if (!_shelf.TryRestoreStored(items[i], slotIndices[i]))
                Debug.LogWarning(
                    $"{name}: could not put '{items[i].name}' back in slot {slotIndices[i]}.",
                    this
                );
        }
    }
}
