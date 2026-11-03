using System;

// Aggregate exact, oriented boxes in the requested orthonormal coordinate frame.
// Rows of axes/directions are X/Y/Z axes, expressed in absolute coordinates.
public sealed class BoundingBoxMath
{
    private readonly double[] _min = { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
    private readonly double[] _max = { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
    public int Count { get; private set; }

    public void Add(double[] corner, double[,] directions, double[] distances, double[] origin, double[] axes)
    {
        // Validate before changing bounds, so a failed body cannot poison earlier results.
        foreach (double value in corner) CheckFinite(value);
        foreach (double value in directions) CheckFinite(value);
        foreach (double value in origin) CheckFinite(value);
        foreach (double value in axes) CheckFinite(value);
        foreach (double value in distances)
        {
            CheckFinite(value);
            if (value < 0) throw new ArgumentException("负包围盒尺寸。");
        }
        var low = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var high = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        for (int mask = 0; mask < 8; ++mask)
        {
            var point = (double[])corner.Clone();
            for (int edge = 0; edge < 3; ++edge)
                if ((mask & (1 << edge)) != 0)
                    for (int axis = 0; axis < 3; ++axis)
                        point[axis] += directions[edge, axis] * distances[edge];
            for (int axis = 0; axis < 3; ++axis)
            {
                double value = 0;
                for (int k = 0; k < 3; ++k) value += axes[axis * 3 + k] * (point[k] - origin[k]);
                CheckFinite(value);
                low[axis] = Math.Min(low[axis], value);
                high[axis] = Math.Max(high[axis], value);
            }
        }
        for (int axis = 0; axis < 3; ++axis)
        {
            _min[axis] = Math.Min(_min[axis], low[axis]);
            _max[axis] = Math.Max(_max[axis], high[axis]);
        }
        ++Count;
    }

    public double[] Dimensions()
    {
        if (Count == 0) throw new InvalidOperationException("没有可测量实体。");
        return new[] { _max[0] - _min[0], _max[1] - _min[1], _max[2] - _min[2] };
    }

    private static void CheckFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("包围盒包含非有限值。");
    }
}
