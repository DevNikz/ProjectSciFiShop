using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shelf-packs boxes into a cavity (rows along X, rows along Z, layers along Y),
/// then aligns each row, layer and the whole stack. Pure math, no Unity objects.
/// </summary>
public sealed class BoxLayoutPacker
{
    private struct Cell
    {
        public Vector3 position,
            size;
        public int row;
    }

    private struct Row
    {
        public float width,
            depth;
        public int layer;
    }

    private struct Layer
    {
        public float depth,
            height;
    }

    private readonly List<Cell> _cells = new();
    private readonly List<Row> _rows = new();
    private readonly List<Layer> _layers = new();

    /// <summary>
    /// Writes one center (cavity space) per size into <paramref name="centers"/>.
    /// False if the sizes don't fit. <paramref name="align"/> = 0..1 per axis (X left..right, Y bottom..top, Z lower..upper).
    /// </summary>
    public bool TryPack(
        IReadOnlyList<Vector3> sizes,
        Vector3 cavityMin,
        Vector3 cavityMax,
        Vector3 spacing,
        Vector3 align,
        List<Vector3> centers
    )
    {
        centers.Clear();
        const float eps = 1e-4f;
        float width = cavityMax.x - cavityMin.x,
            depth = cavityMax.z - cavityMin.z,
            height = cavityMax.y - cavityMin.y;
        if (sizes.Count == 0)
            return true;

        _cells.Clear();
        _rows.Clear();
        _layers.Clear();
        _rows.Add(default);
        _layers.Add(default);

        float x = 0f,
            y = 0f,
            z = 0f,
            rowDepth = 0f,
            layerHeight = 0f;
        int layer = 0;

        for (int i = 0; i < sizes.Count; i++)
        {
            Vector3 size = sizes[i];
            if (size.x > width + eps || size.z > depth + eps)
                return false;
            if (x + size.x > width + eps) // next row
            {
                x = 0f;
                z += rowDepth + spacing.z;
                rowDepth = 0f;
                _rows.Add(new Row { layer = layer });
            }
            if (z + size.z > depth + eps) // next layer
            {
                x = 0f;
                z = 0f;
                y += layerHeight + spacing.y;
                layerHeight = 0f;
                rowDepth = 0f;
                layer++;
                _layers.Add(default);
                _rows.Add(new Row { layer = layer });
            }
            if (y + size.y > height + eps)
                return false;

            int rowIndex = _rows.Count - 1;
            _cells.Add(
                new Cell
                {
                    position = new Vector3(x, y, z),
                    size = size,
                    row = rowIndex,
                }
            );
            var row = _rows[rowIndex];
            row.width = x + size.x;
            row.depth = Mathf.Max(row.depth, size.z);
            _rows[rowIndex] = row;
            var currentLayer = _layers[layer];
            currentLayer.depth = Mathf.Max(currentLayer.depth, z + size.z);
            currentLayer.height = Mathf.Max(currentLayer.height, size.y);
            _layers[layer] = currentLayer;

            x += size.x + spacing.x;
            rowDepth = Mathf.Max(rowDepth, size.z);
            layerHeight = Mathf.Max(layerHeight, size.y);
        }

        float offsetY = (height - (y + layerHeight)) * align.y;
        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            var row = _rows[cell.row];
            var cellLayer = _layers[row.layer];
            Vector3 offset = new(
                (width - row.width) * align.x,
                offsetY,
                (depth - cellLayer.depth) * align.z + (row.depth - cell.size.z) * align.z
            );
            centers.Add(cavityMin + cell.position + offset + cell.size * 0.5f);
        }
        return true;
    }
}
