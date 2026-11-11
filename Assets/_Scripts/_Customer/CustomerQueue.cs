using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the line at one checkout. Queue slot 0 is the customer currently being served.
/// A cashier can call CompleteCheckout(CurrentCustomer) after payment succeeds.
/// </summary>
[DisallowMultipleComponent]
public class CustomerQueue : MonoBehaviour
{
    [SerializeField] Transform checkoutPoint;
    [SerializeField] Transform exitPoint;
    [Tooltip("Waiting positions behind the checkout point, ordered nearest-to-farthest from the cashier.")]
    [SerializeField] Transform[] queueSlots = Array.Empty<Transform>();
    [Header("Testing")]
    [Tooltip("For testing movement before the cashier exists, automatically finish the front customer's checkout.")]
    [SerializeField] bool autoCompleteCheckout;
    [SerializeField, Min(0.1f)] float automaticCheckoutDelay = 3f;

    readonly List<CustomerAI> customers = new();

    public event Action<CustomerAI> CustomerReady;
    public event Action QueueChanged;

    public CustomerAI CurrentCustomer => customers.Count > 0 ? customers[0] : null;
    public int Count => customers.Count;
    public Transform ExitPoint => exitPoint;

    public bool Join(CustomerAI customer)
    {
        if (customer == null || customers.Contains(customer))
            return false;
        if (checkoutPoint == null)
        {
            Debug.LogError($"{name}: assign a Checkout Point before spawning customers.", this);
            return false;
        }

        customers.Add(customer);
        customer.SetQueue(this);
        RefreshDestinations();
        QueueChanged?.Invoke();
        return true;
    }

    public void Leave(CustomerAI customer)
    {
        if (customer == null || !customers.Remove(customer))
            return;

        RefreshDestinations();
        QueueChanged?.Invoke();
    }

    /// <summary>Called by the cashier only after the order and payment have been accepted.</summary>
    public bool CompleteCheckout(CustomerAI customer)
    {
        if (customer == null || customer != CurrentCustomer || !customer.IsReadyForCheckout)
            return false;

        customers.RemoveAt(0);
        customer.LeaveStore();
        RefreshDestinations();
        QueueChanged?.Invoke();
        return true;
    }

    [ContextMenu("Complete Current Customer")]
    void CompleteCurrentCustomer()
    {
        if (CurrentCustomer != null)
            CompleteCheckout(CurrentCustomer);
    }

    internal void NotifyCustomerReady(CustomerAI customer)
    {
        if (customer == CurrentCustomer)
        {
            CustomerReady?.Invoke(customer);
            CustomerNotificationUI.Show("Customer arrived at checkout");
            // The Register owns payment completion. Keep the old automatic route only as a
            // fallback for the queue-only prototype scene where no Register is present.
            bool connectedToCashier = customer.BeginCashierCheckout();
            if (autoCompleteCheckout && !connectedToCashier)
                StartCoroutine(CompleteAfterDelay(customer));
        }
    }

    IEnumerator CompleteAfterDelay(CustomerAI customer)
    {
        yield return new WaitForSeconds(automaticCheckoutDelay);
        if (customer == CurrentCustomer && customer.IsReadyForCheckout)
            CompleteCheckout(customer);
    }

    void RefreshDestinations()
    {
        customers.RemoveAll(customer => customer == null);
        for (int index = 0; index < customers.Count; index++)
        {
            var customer = customers[index];
            if (customer == null)
                continue;

            customer.SetQueuePosition(index, GetTargetFor(index));
        }
    }

    // Keeps the prototype queue locked to its authored marker positions.
    void LateUpdate() => RefreshDestinations();

    Transform GetTargetFor(int index)
    {
        if (index == 0)
            return checkoutPoint;

        if (queueSlots != null && queueSlots.Length > 0)
            return queueSlots[Mathf.Min(index - 1, queueSlots.Length - 1)];
        return checkoutPoint;
    }

    void OnValidate()
    {
        if (queueSlots == null)
            queueSlots = Array.Empty<Transform>();
    }

    void OnDrawGizmos()
    {
        DrawMarker(checkoutPoint, Color.green, "Checkout");
        DrawMarker(exitPoint, Color.red, "Exit");

        if (queueSlots == null)
            return;

        for (int i = 0; i < queueSlots.Length; i++)
        {
            var slot = queueSlots[i];
            DrawMarker(slot, Color.yellow, $"Queue {i + 1}");
            if (i == 0 && checkoutPoint != null && slot != null)
                Gizmos.DrawLine(checkoutPoint.position, slot.position);
            else if (i > 0 && slot != null && queueSlots[i - 1] != null)
                Gizmos.DrawLine(queueSlots[i - 1].position, slot.position);
        }
    }

    static void DrawMarker(Transform marker, Color color, string label)
    {
        if (marker == null)
            return;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(marker.position, 0.3f);
#if UNITY_EDITOR
        UnityEditor.Handles.Label(marker.position + Vector3.up * 0.4f, label);
#endif
    }
}
