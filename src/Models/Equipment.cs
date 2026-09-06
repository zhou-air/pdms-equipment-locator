namespace PdmsEquipmentLocator.Models;

/// <summary>
/// 设备数据模型 —— 第一阶段核心数据结构。
/// Excel 与 DXF 输出必须共用同一份 Equipment 数据集。
/// </summary>
public class Equipment
{
    /// <summary>设备位号（保留原始大小写，不含斜杠）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>世界坐标（mm）。POS 解析失败或缺失时为 null。</summary>
    public Position3D? Position { get; set; }

    /// <summary>原始 POS 字符串（一字不改，便于追溯）</summary>
    public string? RawPosition { get; set; }

    /// <summary>原始 ORI 字符串。第一阶段仅保存，不参与几何计算；第二阶段用于轮廓投影。</summary>
    public string? Orientation { get; set; }

    /// <summary>所属 ZONE 名称（不含斜杠）</summary>
    public string ZoneName { get; set; } = string.Empty;

    /// <summary>NEW EQUIPMENT 行在源 TXT 中的行号（1 起）</summary>
    public int SourceLine { get; set; }

    /// <summary>POS 解析状态</summary>
    public ParseStatus Status { get; set; } = ParseStatus.Pending;

    /// <summary>状态补充说明（异常原因等）</summary>
    public string StatusNote { get; set; } = string.Empty;

    /// <summary>
    /// 子对象（SUBEQUIPMENT / NOZZLE / BOX / CYLINDER / CONE / DISH / PYRAMID ...）。
    /// 第一阶段仅保留层级接口，不解析几何；第二阶段在此之上实现轮廓投影。
    /// </summary>
    public List<PdmsObject> Children { get; } = new();

    public override string ToString() => $"/{Name} {Position} [{Status}]";
}
