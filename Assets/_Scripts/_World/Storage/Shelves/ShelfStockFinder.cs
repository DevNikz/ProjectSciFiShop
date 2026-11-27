using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Finds the shelves inside the store that hold something a customer wants.
/// Reads ShelfTickets.All, so only shelves customers can browse are listed.
/// </summary>
public static class ShelfStockFinder
{
    static readonly List<ProductCarriable> _stock = new();

    /// <summary>Fills results with in-store shelves holding an acceptable free item, nearest to from first.</summary>
    public static void FindShelves(
        Vector3 from,
        Func<ProductDefinition, bool> isAcceptable,
        List<ShelfInventory> results
    )
    {
        results.Clear();
        if (isAcceptable == null)
            return;

        foreach (var tickets in ShelfTickets.All)
        {
            if (tickets == null)
                continue;
            var shelf = tickets.Shelf;
            if (
                shelf != null
                && StoreBounds.IsInsideStore(shelf.CustomerPickupPosition)
                && HasAcceptableItem(shelf, isAcceptable)
            )
                results.Add(shelf);
        }
        results.Sort(
            (a, b) =>
                (a.CustomerPickupPosition - from).sqrMagnitude.CompareTo(
                    (b.CustomerPickupPosition - from).sqrMagnitude
                )
        );
    }

    static bool HasAcceptableItem(ShelfInventory shelf, Func<ProductDefinition, bool> isAcceptable)
    {
        _stock.Clear();
        shelf.GetAvailableProducts(_stock);
        foreach (var product in _stock)
            if (product != null && isAcceptable(product.Definition))
                return true;
        return false;
    }
}
