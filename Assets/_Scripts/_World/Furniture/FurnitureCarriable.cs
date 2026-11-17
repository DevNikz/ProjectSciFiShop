using UnityEngine;

/// <summary>
/// Put on a furniture prefab (next to its SaveableObject). Interact, even while carrying something,
/// to pick it up and move it, or sell it. Position and rotation are saved by SaveableObject. The
/// piece has no Rigidbody and is not a PickupObject.
/// </summary>
[CarriableAddon("As Furniture")]
public class FurnitureCarriable : MonoBehaviour, IPromptContextFlag
{
    [Tooltip("What this piece is (price, refund and footprint come from it).")]
    [SerializeField]
    FurnitureDefinition definition;

    Interactable _interactable;

    public FurnitureDefinition Definition => definition;

    public PromptContext Flag => throw new System.NotImplementedException();

    void Awake() => _interactable = GetComponent<Interactable>();

    void OnEnable()
    {
        if (_interactable == null || definition == null)
        {
            Debug.LogError($"{name}: Interactable/definition missing.", this);
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

    void HandleInteract(Interactor who)
    {
        if (who == null)
            return;
        var placer = FurniturePlacer.Instance;
        if (placer == null)
        {
            Debug.LogWarning($"{name}: no FurniturePlacer in the scene.", this);
            return;
        }
        placer.BeginMove(this, who);
    }
}
