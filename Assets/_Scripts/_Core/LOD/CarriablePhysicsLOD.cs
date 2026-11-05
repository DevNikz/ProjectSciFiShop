using UnityEngine;

/// <summary>Physics LOD for Interactables that have a Carriable. The frozen flag lives on the Carriable.</summary>
public class CarriablePhysicsLOD : PhysicsLODBase<Carriable>
{
    protected override bool TryGetItem(Interactable source, out Carriable item) =>
        source.TryGetComponent(out item);

    protected override Rigidbody GetBody(Carriable item) => item.Body;

    protected override bool IsBusy(Carriable item) => item.IsHeld || item.IsClaimed;

    protected override bool IsFrozen(Carriable item) => item.IsLodFrozen;

    protected override void SetFrozen(Carriable item, bool frozen) => item.IsLodFrozen = frozen;
}
