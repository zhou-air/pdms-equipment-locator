namespace PdmsEquipmentLocator.Models;

/// <summary>
/// BOX 原体。PDMS BOX 以 POS 为几何中心，三边半长 XLEN/2, YLEN/2, ZLEN/2（经 P1081a
/// 泵组内部包络验证：BOX YLEN 164.15 恰好等于圆柱 HEIG 164.15 的完整包络）。
/// Phase 2A 支持。
/// </summary>
public class BoxPrimitive : PdmsObject
{
    /// <summary>X 方向边长（mm）；缺失或非法为 null</summary>
    public double? XLen => GetNumericAttribute("XLEN");

    /// <summary>Y 方向边长（mm）</summary>
    public double? YLen => GetNumericAttribute("YLEN");

    /// <summary>Z 方向边长（mm）</summary>
    public double? ZLen => GetNumericAttribute("ZLEN");
}

/// <summary>
/// CYLINDER 原体。PDMS CYLINDER 以 POS 为几何中心，轴沿局部 Z，两端面位于 ±HEIG/2
/// （经 V1021A 上下 DISH 位于 ±2250 = HEIG 4500/2 验证）。
/// Phase 2A 支持。
/// </summary>
public class CylinderPrimitive : PdmsObject
{
    /// <summary>直径（mm）</summary>
    public double? Diameter => GetNumericAttribute("DIAM");

    /// <summary>高度（mm，轴向全长）</summary>
    public double? Height => GetNumericAttribute("HEIG");
}
