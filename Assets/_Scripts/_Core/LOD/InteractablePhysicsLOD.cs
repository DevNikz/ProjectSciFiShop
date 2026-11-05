using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physics LOD for Interactables that have a Rigidbody but no Carriable (those use CarriablePhysicsLOD).
/// The frozen flag is kept here, so Interactable and Carriable stay untouched.
/// </summary>
public class InteractablePhysicsLOD : PhysicsLODBase<Rigidbody>
{
    private readonly HashSet<Rigidbody> _frozen = new();

    protected override bool TryGetItem(Interactable source, out Rigidbody item)
    {
        item = null;
        return !source.TryGetComponent<Carriable>(out _) && source.TryGetComponent(out item);
    }

    protected override Rigidbody GetBody(Rigidbody item) => item;

    protected override bool IsBusy(Rigidbody item) => false;

    protected override bool IsFrozen(Rigidbody item) => _frozen.Contains(item);

    protected override void SetFrozen(Rigidbody item, bool frozen)
    {
        if (frozen)
            _frozen.Add(item);
        else
            _frozen.Remove(item);
    }

    // A frozen body that leaves the list (e.g. pooled) must not stay kinematic.
    protected override void OnItemRemoved(Rigidbody item)
    {
        if (_frozen.Remove(item) && item != null)
            item.isKinematic = false;
    }
}
