namespace PdmsEquipmentLocator.Export;

/// <summary>
/// DXF 中单个设备定位点的记录 —— 用于“Equipment → CAD Entity”对应关系验证。
/// 十字中心（X, Y）必须严格等于 Equipment 世界坐标 X/Y。
/// </summary>
public class DxfPointRecord
{
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public int SourceLine { get; set; }
}

/// <summary>
/// DxfExporter 的导出结果：文件路径 + 每个设备定位点记录 + 各类实体计数。
/// 供控制台自动验证（DXF 定位点数 == Excel 有效设备数 == Parser 成功数）。
/// </summary>
public class DxfExportResult
{
    public string FilePath { get; set; } = string.Empty;
    public List<DxfPointRecord> Points { get; } = new();
    public int MarkerCount { get; set; }
    public int TagCount { get; set; }
    /// <summary>Phase 2：EQUIP_OUTLINE 闭合多段线数量</summary>
    public int OutlineCount { get; set; }
    /// <summary>调试模式下的设备名（null = 全量输出）</summary>
    public string? DebugEquipment { get; set; }
}
