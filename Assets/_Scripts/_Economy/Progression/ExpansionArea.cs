using System;
using UnityEngine;

/// <summary>
/// One store expansion in the scene. When the player buys the expansion, the objects in "Activate When
/// Bought" are switched on and the ones in "Deactivate When Bought" are switched off. The state is
/// applied the moment ProgressionManager changes, so a load has the area ready before the world restores.
/// Author the "activate" objects inactive in the scene.
/// </summary>
public class ExpansionArea : MonoBehaviour
{
    [Tooltip("Which expansion this area belongs to.")]
    [SerializeField]
    ExpansionDefinition expansion;

    [Tooltip("Switched ON when bought: rooms, shelves, lights. Leave them inactive in the scene.")]
    [SerializeField]
    GameObject[] activateWhenBought = Array.Empty<GameObject>();

    [Tooltip("Switched OFF when bought: barriers, blocking walls, 'closed' signs.")]
    [SerializeField]
    GameObject[] deactivateWhenBought = Array.Empty<GameObject>();

    [Header("Gizmo")]
    [Tooltip("Size of the box drawn in the Scene view (orange = locked, green = bought in Play mode).")]
    [SerializeField]
    Vector3 gizmoSize = new(6f, 3f, 6f);

    ProgressionManager _progression;

    // Start, not OnEnable: every manager has set its Instance by now.
    void Start()
    {
        if (expansion == null)
        {
            Debug.LogError($"{name}: expansion missing.", this);
            enabled = false;
            return;
        }

        _progression = ProgressionManager.Instance;
        if (_progression == null)
        {
            Debug.LogWarning($"{name}: no ProgressionManager in the scene, so the area is open.", this);
            Apply(true);
            return;
        }

        _progression.Changed += HandleChanged;
        Apply(_progression.IsExpansionBought(expansion)); // a load may already have happened
    }

    void OnDestroy()
    {
        if (_progression != null)
            _progression.Changed -= HandleChanged;
    }

    void HandleChanged() => Apply(_progression.IsExpansionBought(expansion));

    void Apply(bool bought)
    {
        foreach (var go in activateWhenBought)
            if (go != null && go.activeSelf != bought) // Unity-null check: skips destroyed objects
                go.SetActive(bought);
        foreach (var go in deactivateWhenBought)
            if (go != null && go.activeSelf == bought)
                go.SetActive(!bought);
    }

    [ContextMenu("Debug: preview bought (changes the scene)")]
    void DebugPreviewBought() => Apply(true);

    [ContextMenu("Debug: preview locked (changes the scene)")]
    void DebugPreviewLocked() => Apply(false);

    void OnValidate()
    {
        gizmoSize = Vector3.Max(gizmoSize, Vector3.one * 0.1f);
        Check(activateWhenBought);
        Check(deactivateWhenBought);
    }

    void Check(GameObject[] list)
    {
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null)
                Debug.LogWarning($"{name}: an entry is empty.", this);
            else if (list[i] == gameObject)
                Debug.LogWarning($"{name}: this object is in its own list, so it would switch itself.", this);
        }
    }

    void OnDrawGizmos()
    {
        bool bought =
            Application.isPlaying
            && _progression != null
            && expansion != null
            && _progression.IsExpansionBought(expansion);
        Gizmos.color = bought ? Color.green : new Color(1f, 0.55f, 0f);
        Gizmos.DrawWireCube(transform.position, gizmoSize);

        Gizmos.color = Color.yellow; // switched on when bought
        foreach (var go in activateWhenBought)
            if (go != null)
                Gizmos.DrawLine(transform.position, go.transform.position);
        Gizmos.color = Color.red; // switched off when bought
        foreach (var go in deactivateWhenBought)
            if (go != null)
                Gizmos.DrawLine(transform.position, go.transform.position);
    }
}
