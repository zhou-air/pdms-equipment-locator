using System.Numerics;

namespace PdmsEquipmentLocator.Cad;

/// <summary>
/// 二维凸包（Andrew 单调链算法）。用于把三维 Primitive 的 XY 投影点集
/// 化简为闭合轮廓多边形，输出按逆时针方向排列。
/// </summary>
public static class ConvexHull
{
    /// <summary>计算点集凸包（逆时针）。少于 3 个有效点时按原序返回。</summary>
    public static List<Vector2> Compute(IEnumerable<Vector2> points)
    {
        var pts = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        int n = pts.Count;
        if (n < 3)
            return pts;

        var hull = new List<Vector2>(2 * n);

        // 下链
        for (int i = 0; i < n; i++)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], pts[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }

        // 上链
        int lower = hull.Count + 1;
        for (int i = n - 2; i >= 0; i--)
        {
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], pts[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(pts[i]);
        }

        hull.RemoveAt(hull.Count - 1); // 首尾重复
        return hull;
    }

    /// <summary>叉积 (b-a) × (c-a)：>0 左转（逆时针），≤0 共线或右转</summary>
    public static float Cross(Vector2 a, Vector2 b, Vector2 c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
