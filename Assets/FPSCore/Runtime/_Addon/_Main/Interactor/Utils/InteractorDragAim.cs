using UnityEngine;

/// <summary>
/// Lives with the Interactor addon. Lets Dragger use the Interactor's aim, hands-busy state and
/// obstacle probe without Dragger knowing about Interactor.
/// </summary>
[InteractorAddon("Dragger Interactor Link")]
[RequireComponent(typeof(Interactor))]
public class InteractorDragAim : MonoBehaviour, IDragAim
{
    [Tooltip(
        "Reach (m) of the fallback probe used when no Interactable is under the crosshair, so a plain Draggable can still be grabbed."
    )]
    [SerializeField]
    float fallbackReach = 3f;

    [Header("Debug")]
    [SerializeField]
    private bool drawGizmos = true;

    Interactor _source;

    Interactor Source
    {
        get
        {
            if (_source == null)
                _source = GetComponent<Interactor>();
            return _source;
        }
    }

    private Interactable _target;

    [Header("Debug (read-only)")]
    [SerializeField]
    private Interactable _targetDebug;

    /// <summary>Interactable under the crosshair that can be used in the current state.</summary>
    public Interactable Target
    {
        get => _target;
        private set
        {
            _targetDebug = value;
            if (_target == value)
                return;
            _target = value;
        }
    }

    public Transform Origin => Source.Origin;
    public CharacterController Body => Source.Body;
    public bool IsHandsBusy => Source.IsHolding;

    public GameObject GetAimedObject()
    {
        Target = Source.Target; // Interactable under the crosshair
        if (Target != null)
            return Target.gameObject;

        // No Interactable aimed at: probe straight ahead so plain Draggables work too.
        var origin = Source.Origin;
        if (origin == null)
            return null;
        if (!Source.Cast(origin.position, origin.forward, fallbackReach, transform, out var hit))
            return null;
        return hit.rigidbody != null ? hit.rigidbody.gameObject : hit.collider.gameObject;
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos || Source.Origin == null)
            return;

        Gizmos.color = Color.yellow; // interact ray
        Gizmos.DrawLine(
            Source.Origin.position,
            Source.Origin.position + Source.Origin.forward * Source.InteractRange
        );

        Gizmos.color = Color.red; // interact ray
        Gizmos.DrawLine(
            Source.Origin.position,
            Source.Origin.position + Source.Origin.forward * fallbackReach
        );
    }

    void OnValidate() => fallbackReach = Mathf.Max(0.1f, fallbackReach);

    public bool Cast(
        Vector3 from,
        Vector3 dir,
        float distance,
        Transform ignore,
        out RaycastHit hit
    ) => Source.Cast(from, dir, distance, ignore, out hit);
}
