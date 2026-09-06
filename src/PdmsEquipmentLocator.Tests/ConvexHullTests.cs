using System.Numerics;
using PdmsEquipmentLocator.Cad;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// 二维凸包测试（Andrew 单调链）：矩形 / 旋转 / 共线 / 点数不足。
/// </summary>
public class ConvexHullTests
{
    [Fact]
    public void Compute_Square_Returns4VerticesCCW()
    {
        var pts = new[]
        {
            new Vector2(1, 1), new Vector2(3, 1),
            new Vector2(3, 3), new Vector2(1, 3)
        };
        var hull = ConvexHull.Compute(pts);
        Assert.Equal(4, hull.Count);
        // 逆时针验证：任意连续三点叉积 > 0
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var c = hull[(i + 2) % hull.Count];
            Assert.True(ConvexHull.Cross(a, b, c) > 0, $"顶点 {i} 处非逆时针");
        }
    }

    [Fact]
    public void Compute_Rectangle1000x500_BoundingBoxMatches()
    {
        // 用户规范用例：1000×500 矩形（无旋转）→ 凸包包围盒 1000×500
        var pts = new[]
        {
            new Vector2(-500, -250), new Vector2(500, -250),
            new Vector2(500, 250), new Vector2(-500, 250)
        };
        var hull = ConvexHull.Compute(pts);
        Assert.Equal(4, hull.Count);
        Assert.Equal(-500f, hull.Min(p => p.X), 3);
        Assert.Equal(500f, hull.Max(p => p.X), 3);
        Assert.Equal(-250f, hull.Min(p => p.Y), 3);
        Assert.Equal(250f, hull.Max(p => p.Y), 3);
    }

    [Fact]
    public void Compute_Rotated90Rectangle_Becomes500x1000()
    {
        // 用户规范用例：旋转 90° 后的 1000×500 → 500×1000
        // 旋转后角点：R90(±500, ±250) → (∓250·?, ...) —— 直接构造旋转后的点集
        var pts = new[]
        {
            new Vector2(250, -500), new Vector2(-250, -500),
            new Vector2(250, 500), new Vector2(-250, 500)
        };
        var hull = ConvexHull.Compute(pts);
        Assert.Equal(4, hull.Count);
        Assert.Equal(-250f, hull.Min(p => p.X), 3);
        Assert.Equal(250f, hull.Max(p => p.X), 3);
        Assert.Equal(-500f, hull.Min(p => p.Y), 3);
        Assert.Equal(500f, hull.Max(p => p.Y), 3);
    }

    [Fact]
    public void Compute_DuplicateAndInteriorPoints_Removed()
    {
        var pts = new[]
        {
            new Vector2(0, 0), new Vector2(0, 0),   // 重复
            new Vector2(10, 0), new Vector2(10, 0),
            new Vector2(10, 10), new Vector2(0, 10),
            new Vector2(5, 5)                        // 内部点
        };
        var hull = ConvexHull.Compute(pts);
        Assert.Equal(4, hull.Count);
    }

    [Fact]
    public void Compute_Collinear_ReturnsEndpoints()
    {
        var pts = new[]
        {
            new Vector2(0, 0), new Vector2(5, 0), new Vector2(10, 0)
        };
        var hull = ConvexHull.Compute(pts);
        Assert.Equal(2, hull.Count);
    }

    [Fact]
    public void Compute_LessThan3Points_ReturnsAsIs()
    {
        var one = ConvexHull.Compute(new[] { new Vector2(1, 2) });
        Assert.Single(one);
        Assert.Equal(new Vector2(1, 2), one[0]);

        var two = ConvexHull.Compute(new[] { new Vector2(1, 2), new Vector2(3, 4) });
        Assert.Equal(2, two.Count);
    }

    [Fact]
    public void Compute_Empty_ReturnsEmpty()
    {
        Assert.Empty(ConvexHull.Compute(Array.Empty<Vector2>()));
    }
}
