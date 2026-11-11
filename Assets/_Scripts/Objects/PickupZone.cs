using System.Collections.Generic;
using UnityEngine;

/// <summary>A box-shaped area that reports the pickups resting inside it (counter, change tray).</summary>
public class PickupZone : MonoBehaviour
{
    [Tooltip("Center of the area, relative to this object (m).")]
    [SerializeField]
    Vector3 center = new(0f, 0.15f, 0f);

    [Tooltip("Half size of the area (m).")]
    [SerializeField]
    Vector3 halfExtents = new(0.4f, 0.15f, 0.3f);

    [Tooltip("What counts as inside.")]
    [SerializeField]
    LayerMask mask = ~0;

    [Tooltip("Items faster than this (m/s) are still moving and are ignored.")]
    [SerializeField]
    float settleSpeed = 0.1f;

    public Vector3 CustomerStagingOrigin =>
        transform.TransformPoint(center) + transform.right * (halfExtents.x + 0.7f);

    /// <summary>
    /// Fills results (cleared first) with the settled, un-held pickups of type T inside the zone.
    /// Allocates a small array per call, so don't call it every frame.
    /// </summary>
    public int Collect<T>(List<T> results)
        where T : Component, IHasCarriable
    {
        if (results == null)
            return 0;
        results.Clear();

        Collider[] hits = Physics.OverlapBox(
            transform.TransformPoint(center),
            halfExtents,
            transform.rotation,
            mask,
            QueryTriggerInteraction.Ignore
        );
        foreach (var hit in hits)
        {
            var item = hit.GetComponentInParent<T>();
            if (item == null || results.Contains(item) || !IsSettled(item))
                continue;
            results.Add(item);
        }
        return results.Count;
    }

    bool IsSettled(IHasCarriable item)
    {
        var p = item.Carriable;
        if (!p.isActiveAndEnabled || p.IsHeld || p.IsClaimed)
            return false;
        var rb = p.Body;
        return rb == null || rb.linearVelocity.sqrMagnitude <= settleSpeed * settleSpeed;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(center, halfExtents * 2f);
    }

    void OnValidate()
    {
        halfExtents = Vector3.Max(halfExtents, Vector3.one * 0.05f);
        settleSpeed = Mathf.Max(0f, settleSpeed);
    }
}
