using System;
using System.Collections.Generic;

public struct OrderLine
{
    public ProductDefinition definition;
    public int boxes;
}

/// <summary>What the player is about to buy. Plain data: no scene or coin access.</summary>
public class OrderCart
{
    readonly List<OrderLine> _lines = new();

    public IReadOnlyList<OrderLine> Lines => _lines;
    public bool IsEmpty => _lines.Count == 0;

    public event Action Changed;

    /// <summary>Coins for boxes x unitsPerBox x unitCost. 0 for bad input; capped at int.MaxValue.</summary>
    public static int CostOf(int boxes, int unitsPerBox, int unitCost)
    {
        if (boxes < 1 || unitsPerBox < 1 || unitCost < 1)
            return 0;
        long cost = (long)boxes * unitsPerBox * unitCost;
        return (int)Math.Min(cost, int.MaxValue);
    }

    public static int LineCost(ProductDefinition def, int boxes) =>
        def == null ? 0 : CostOf(boxes, def.UnitsPerBox, def.UnitCost);

    public int TotalCost()
    {
        long total = 0;
        foreach (var l in _lines)
            total += LineCost(l.definition, l.boxes);
        return (int)Math.Min(total, int.MaxValue);
    }

    public int TotalBoxes()
    {
        long total = 0;
        foreach (var l in _lines)
            total += l.boxes;
        return (int)Math.Min(total, int.MaxValue);
    }

    public int GetBoxes(ProductDefinition def)
    {
        int i = IndexOf(def);
        return i < 0 ? 0 : _lines[i].boxes;
    }

    /// <summary>Add boxes of a product. False if def is null or boxes &lt; 1.</summary>
    public bool Add(ProductDefinition def, int boxes = 1)
    {
        if (def == null || boxes < 1)
            return false;

        int i = IndexOf(def);
        if (i < 0)
            _lines.Add(new OrderLine { definition = def, boxes = boxes });
        else
            _lines[i] = new OrderLine
            {
                definition = def,
                boxes = (int)Math.Min((long)_lines[i].boxes + boxes, int.MaxValue),
            };
        Changed?.Invoke();
        return true;
    }

    /// <summary>Remove boxes of a product (the line goes at 0). False if it is not in the cart.</summary>
    public bool Remove(ProductDefinition def, int boxes = 1)
    {
        int i = IndexOf(def);
        if (i < 0 || boxes < 1)
            return false;

        int left = _lines[i].boxes - boxes;
        if (left <= 0)
            _lines.RemoveAt(i);
        else
            _lines[i] = new OrderLine { definition = def, boxes = left };
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (_lines.Count == 0)
            return;
        _lines.Clear();
        Changed?.Invoke();
    }

    int IndexOf(ProductDefinition def)
    {
        if (def == null)
            return -1;
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].definition == def)
                return i;
        return -1;
    }
}
