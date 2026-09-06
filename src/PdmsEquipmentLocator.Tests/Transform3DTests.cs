using System.Numerics;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// Transform3D 测试：单位变换 / 平移 / 复合（父级局部坐标系语义）。
/// </summary>
public class Transform3DTests
{
    // 绕 Z 旋转 90°（X=S, Y=E, Z=U）—— 与 ORI "Y is E and Z is U" 等价
    private static readonly Transform3D Rot90Z = Transform3D.FromAxes(
        new Vector3(0, -1, 0),   // X = S
        new Vector3(1, 0, 0),    // Y = E
        new Vector3(0, 0, 1),    // Z = U
        Vector3.Zero);

    [Fact]
    public void Identity_LeavesPointUnchanged()
    {
        var p = new Vector3(5, -7, 3);
        Assert.Equal(p, Transform3D.Identity.TransformPoint(p));
        Assert.True(Transform3D.Identity.IsOrthonormal());
    }

    [Fact]
    public void FromTranslation_MovesPoint()
    {
        var t = Transform3D.FromTranslation(new Vector3(10, 20, 30));
        Assert.Equal(new Vector3(15, 15, 35), t.TransformPoint(new Vector3(5, -5, 5)));
    }

    [Fact]
    public void Compose_TranslationsAddUp()
    {
        // 父平移 (10,20,30)，子平移 (1,2,3) → 世界平移 (11,22,33)
        var parent = Transform3D.FromTranslation(new Vector3(10, 20, 30));
        var child = Transform3D.FromTranslation(new Vector3(1, 2, 3));
        var world = Transform3D.Compose(parent, child);
        Assert.Equal(new Vector3(11, 22, 33), world.Translation);
    }

    [Fact]
    public void Compose_RotationThenTranslation_AppliesRotationToChildTranslation()
    {
        // 父级绕 Z 旋转 90°（X=S, Y=E），子级平移 (1,0,0)：
        // 子平移在世界系 = R·(1,0,0) = (0,-1,0)
        var parent = Transform3D.Compose(Transform3D.FromTranslation(new Vector3(100, 200, 0)), Rot90Z);
        var child = Transform3D.FromTranslation(new Vector3(1, 0, 0));
        var world = Transform3D.Compose(parent, child);
        Assert.Equal(new Vector3(100, 199, 0), world.Translation);
    }

    [Fact]
    public void Compose_PointThroughChain_MatchesHandComputation()
    {
        // 父级：平移(100,200,300) + 旋转90°(X=S,Y=E)；子级：平移(10,0,0)
        // 子局部点 (2,3,4)：
        //   世界 = (100,200,300) + R·(10,0,0) + R·(2,3,4)
        //        = (100,200,300) + (0,-10,0) + (3,-2,4)
        //        = (103,188,304)
        var parent = Transform3D.Compose(Transform3D.FromTranslation(new Vector3(100, 200, 300)), Rot90Z);
        var child = Transform3D.FromTranslation(new Vector3(10, 0, 0));
        var world = Transform3D.Compose(parent, child);
        var got = world.TransformPoint(new Vector3(2, 3, 4));
        Assert.Equal(103f, got.X, 4);
        Assert.Equal(188f, got.Y, 4);
        Assert.Equal(304f, got.Z, 4);
    }

    [Fact]
    public void IsOrthonormal_DetectsBadAxes()
    {
        var bad = Transform3D.FromAxes(
            new Vector3(0, 0, 1),  // 与 Z 相同的 X
            new Vector3(1, 0, 0),
            new Vector3(0, 0, 1),
            Vector3.Zero);
        Assert.False(bad.IsOrthonormal());
    }

    [Fact]
    public void FromAxes_Rot90_TransformsPointCorrectly()
    {
        // (1,0,0) 经绕 Z 90° → (0,-1,0)
        var got = Rot90Z.TransformPoint(new Vector3(1, 0, 0));
        Assert.Equal(0f, got.X, 4);
        Assert.Equal(-1f, got.Y, 4);
        Assert.Equal(0f, got.Z, 4);
    }
}
