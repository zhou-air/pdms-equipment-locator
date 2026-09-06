namespace PdmsEquipmentLocator.Models;

/// <summary>
/// 解析状态。第一阶状态只描述 EQUIPMENT 自身 POS 的解析结果。
/// </summary>
public enum ParseStatus
{
    /// <summary>尚未解析（初始状态）</summary>
    Pending,

    /// <summary>成功解析出世界坐标</summary>
    Ok,

    /// <summary>EQUIPMENT 块中没有任何 POS 属性行</summary>
    MissingPos,

    /// <summary>POS 行存在，但格式无法解析（如缺少分量、非法数值）</summary>
    InvalidPos,

    /// <summary>NEW EQUIPMENT 之后没有名称</summary>
    MissingName
}
