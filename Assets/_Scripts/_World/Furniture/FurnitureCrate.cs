using System;
using UnityEngine;

/// <summary>
/// A delivered piece of furniture, still in its crate. Interact (even while carrying something) to
/// start placing it. It saves with the world: the definition id is its saved state. It has no
/// Rigidbody and is not a PickupObject, so it can't be carried or dragged.
/// </summary>
[InteractableAddon("As Crate")]
public class FurnitureCrate : MonoBehaviour
{
    [Serializable]
    class State
    {
        public string id;
    }

    Interactable _interactable;
    FurnitureDefinition _def;

    public FurnitureDefinition Definition => _def;

    public string SaveKey => "furnitureCrate";

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

    public string CaptureState() =>
        JsonUtility.ToJson(new State { id = _def != null ? _def.Id : null });

    public void RestoreState(string json)
    {
        var state = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<State>(json);
        var manager = FurnitureManager.Instance;
        var catalog = manager != null ? manager.Catalog : null;
        if (state == null || catalog == null || !catalog.TryGetById(state.id, out var def))
        {
            Debug.LogWarning(
                $"{name}: saved furniture '{state?.id}' is not in the catalog, so this crate does nothing.",
                this
            );
            return;
        }
        _def = def;
    }
}
