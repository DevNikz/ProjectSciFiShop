using UnityEngine;

/// <summary>
/// A delivered piece of furniture, still in its crate. Interact (even while carrying something) to
/// start placing it. Easy Save keeps its definition (ES3 Serializable). It has no
/// Rigidbody and is not a PickupObject, so it can't be carried or dragged.
/// </summary>
[InteractableAddon("As Crate")]
public class FurnitureCrate : MonoBehaviour
{
    Interactable _interactable;

    [ES3Serializable]
    FurnitureDefinition _def;

    public FurnitureDefinition Definition => _def;

    void Awake() => _interactable = GetComponent<Interactable>();

    void OnEnable()
    {
        if (_interactable == null)
        {
            Debug.LogError($"{name}: Interactable missing.", this);
            enabled = false;
            return;
        }
        _interactable.OnInteractEvent += HandleInteract;
    }

    void OnDisable()
    {
        if (_interactable != null)
            _interactable.OnInteractEvent -= HandleInteract;
    }

    /// <summary>Set by FurnitureManager right after the crate is spawned.</summary>
    public void Init(FurnitureDefinition def) => _def = def;

    void HandleInteract(Interactor who)
    {
        if (who == null || _def == null)
            return;
        var placer = FurniturePlacer.Instance;
        if (placer == null)
        {
            Debug.LogWarning($"{name}: no FurniturePlacer in the scene.", this);
            return;
        }
        placer.BeginPlace(this, who);
    }
}
