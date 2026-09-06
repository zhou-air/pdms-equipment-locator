using System.Numerics;

namespace PdmsEquipmentLocator.Models;

/// <summary>
/// 三维变换（Phase 2 核心）：Translation + Rotation。
///
/// 旋转以三个轴向量（局部 X/Y/Z 轴在父坐标系中的方向）表达，列向量即旋转矩阵 R，
/// 满足右手系：X = Y × Z（与 PDMS 默认 X=E, Y=N, Z=U 一致）。
///
/// 变换链（PDMS Data Listing 语义，经泵组设备族 P1081a/P1083a/P1052 交叉验证）：
///   POS：相对父级原点、在父级局部坐标系中解释
///   ORI：相对父级坐标系的旋转（父级旋转 × 自身 ORI = 世界旋转）
///
/// 因此：P_world = T_parent × T_child × P_local（递归）。
/// </summary>
public class Transform3D
{
    /// <summary>局部 X 轴在父坐标系中的方向（单位向量）</summary>
    public Vector3 XAxis { get; private set; } = Vector3.UnitX;

    /// <summary>局部 Y 轴在父坐标系中的方向（单位向量）</summary>
    public Vector3 YAxis { get; private set; } = Vector3.UnitY;

    /// <summary>局部 Z 轴在父坐标系中的方向（单位向量）</summary>
    public Vector3 ZAxis { get; private set; } = Vector3.UnitZ;

    /// <summary>平移（父坐标系中）</summary>
    public Vector3 Translation { get; private set; } = Vector3.Zero;

    public static Transform3D Identity { get; } = new();

    /// <summary>由三根轴（须彼此正交、右手系 X=Y×Z）+ 平移构造</summary>
    public static Transform3D FromAxes(Vector3 x, Vector3 y, Vector3 z, Vector3 translation)
    {
        return new Transform3D
        {
            XAxis = x,
            YAxis = y,
            ZAxis = z,
            Translation = translation
        };
    }

    /// <summary>仅平移</summary>
    public static Transform3D FromTranslation(Vector3 translation)
    {
        return new Transform3D { Translation = translation };
    }

    /// <summary>点变换：P_parent = T + R · P_local</summary>
    public Vector3 TransformPoint(Vector3 p)
        => Translation + TransformDirection(p);

    /// <summary>方向变换（不含平移）：D_parent = R · D_local</summary>
    public Vector3 TransformDirection(Vector3 d)
        => XAxis * d.X + YAxis * d.Y + ZAxis * d.Z;

    /// <summary>
    /// 复合变换：world = parent ∘ child（即先施加 child 再施加 parent）。
    /// child 的轴/平移在 parent 的父坐标系（= parent 的局部系）中解释。
    /// </summary>
    public static Transform3D Compose(Transform3D parent, Transform3D child)
    {
        return new Transform3D
        {
            XAxis = Vector3.Normalize(parent.TransformDirection(child.XAxis)),
            YAxis = Vector3.Normalize(parent.TransformDirection(child.YAxis)),
            ZAxis = Vector3.Normalize(parent.TransformDirection(child.ZAxis)),
            Translation = parent.TransformPoint(child.Translation)
        };
    }

    /// <summary>旋转矩阵是否为单位正交右手系（容差校验，用于调试与测试）</summary>
    public bool IsOrthonormal(float tolerance = 1e-4f)
    {
        bool Unit(Vector3 v) => MathF.Abs(v.Length() - 1f) <= tolerance;
        bool Perp(Vector3 a, Vector3 b) => MathF.Abs(Vector3.Dot(a, b)) <= tolerance;
        return Unit(XAxis) && Unit(YAxis) && Unit(ZAxis)
            && Perp(XAxis, YAxis) && Perp(YAxis, ZAxis) && Perp(XAxis, ZAxis)
            && MathF.Abs(Vector3.Dot(Vector3.Cross(XAxis, YAxis), ZAxis) - 1f) <= tolerance;
    }

    public override string ToString()
        => $"T=({Translation.X:0.###},{Translation.Y:0.###},{Translation.Z:0.###}) "
         + $"X=({XAxis.X:0.####},{XAxis.Y:0.####},{XAxis.Z:0.####}) "
         + $"Y=({YAxis.X:0.####},{YAxis.Y:0.####},{YAxis.Z:0.####}) "
         + $"Z=({ZAxis.X:0.####},{ZAxis.Y:0.####},{ZAxis.Z:0.####})";
}
