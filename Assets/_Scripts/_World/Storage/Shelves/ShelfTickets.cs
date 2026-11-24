using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Limits how many customers can browse one shelf at the same time. Put it next to a ShelfInventory.
/// A customer takes a ticket when it reaches the shelf and gives it back when it is done looking.
/// A shelf without this component is not browsed by customers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ShelfInventory))]
public class ShelfTickets : MonoBehaviour
{
    static readonly List<ShelfTickets> _all = new();

    /// <summary>Every enabled ShelfTickets in the scene.</summary>
    public static IReadOnlyList<ShelfTickets> All => _all;

    [Tooltip("How many customers can browse this shelf at once (min 1).")]
    [SerializeField, Min(1)]
    int ticketCount = 2;

    readonly List<CustomerAI> _holders = new();
    ShelfInventory _shelf;

    public ShelfInventory Shelf => _shelf ??= GetComponent<ShelfInventory>();

    public int FreeTickets
    {
        get
        {
            RemoveMissingHolders();
            return Mathf.Max(0, ticketCount - _holders.Count);
        }
    }

    public bool HasFreeTicket => FreeTickets > 0;

    /// <summary>Gives the customer a ticket. True if it already has one or a ticket was free.</summary>
    public bool TryTake(CustomerAI customer)
    {
        if (customer == null)
            return false;
        RemoveMissingHolders();
        if (_holders.Contains(customer))
            return true;
        if (_holders.Count >= ticketCount)
            return false;
        _holders.Add(customer);
        return true;
    }

    /// <summary>Gives a ticket back. Does nothing if the customer holds none.</summary>
    public void Release(CustomerAI customer) => _holders.Remove(customer);

    void RemoveMissingHolders() => _holders.RemoveAll(customer => customer == null);

    void OnEnable() => _all.Add(this);

    void OnDisable()
    {
        _all.Remove(this);
        _holders.Clear();
    }

    void OnValidate() => ticketCount = Mathf.Max(1, ticketCount);
}
