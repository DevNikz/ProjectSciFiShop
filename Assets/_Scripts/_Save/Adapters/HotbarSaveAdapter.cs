using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Saves the items in the player's hotbar, including the one in the hands. After a load they are all
/// stowed in the hotbar (the hands start empty). Put it on the player next to the Hotbar, and add it to
/// the player's ES3 Auto Save "Components To Save" so its list is written with the world.
/// </summary>
[RequireComponent(typeof(Hotbar))]
public class HotbarSaveAdapter : SaveParticipant
{
    // Saved by Easy Save (public fields of a listed component).
    public List<Carriable> items = new();

    Hotbar _hotbar;

    protected override void Awake()
    {
        base.Awake();
        _hotbar = GetComponent<Hotbar>();
    }

    public override void BeforeSave() => _hotbar.GetStoredItems(items);

    // The items are about to be removed with the world: forget them first.
    public override void BeforeLoad() => _hotbar.ClearAll();

    public override void AfterLoad()
    {
        foreach (var item in items)
        {
            if (item == null)
                continue;
            if (!_hotbar.TryRestoreItem(item))
                Debug.LogWarning($"{name}: no room for '{item.name}' in the hotbar.", this);
        }
    }
}
