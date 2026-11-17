using System;
using System.Collections.Generic;

/// <summary>
/// What the player is about to order at the furniture terminal. Plain data: nothing is spent here,
/// FurnitureManager.TryPlaceOrder does the paying.
/// </summary>
public class FurnitureCart
{
    /// <summary>Most of one piece in the cart.</summary>
    public const int MaxPerLine = 10;

    public struct Line
    {
        public FurnitureDefinition definition;
        public int count;
    }

    readonly List<Line> _lines = new();

    /// <summary>Raised after any change to the cart.</summary>
    public event Action Changed;

    public IReadOnlyList<Line> Lines => _lines;
    public bool IsEmpty => _lines.Count == 0;

    public int TotalCount()
    {
        int n = 0;
        foreach (var l in _lines)
            n += l.count;
        return n;
    }

    public int TotalCost()
    {
        int sum = 0;
        foreach (var l in _lines)
            sum += l.definition.Price * l.count;
        return sum;
    }

    public int GetCount(FurnitureDefinition def)
    {
        int i = Find(def);
        return i < 0 ? 0 : _lines[i].count;
    }

    public void Add(FurnitureDefinition def, int count)
    {
        if (def == null || count < 1)
            return;
        int i = Find(def);
        if (i < 0)
            _lines.Add(new Line { definition = def, count = Math.Min(count, MaxPerLine) });
        else
        {
            var l = _lines[i];
            int next = Math.Min(l.count + count, MaxPerLine);
            if (next == l.count)
                return;
            l.count = next;
            _lines[i] = l;
        }
        Changed?.Invoke();
    }

    public void Remove(FurnitureDefinition def, int count)
    {
        int i = Find(def);
        if (i < 0 || count < 1)
            return;
        var l = _lines[i];
        l.count -= count;
        if (l.count <= 0)
            _lines.RemoveAt(i);
        else
            _lines[i] = l;
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_lines.Count == 0)
            return;
        _lines.Clear();
        Changed?.Invoke();
    }

    int Find(FurnitureDefinition def)
    {
        if (def == null)
            return -1;
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].definition == def)
                return i;
        return -1;
    }
}
