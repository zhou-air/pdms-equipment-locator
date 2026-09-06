namespace PdmsEquipmentLocator.Models;

/// <summary>
/// 解析异常/警告记录。任何异常不得静默忽略，必须全部收集并输出。
/// </summary>
public class ParseIssue
{
    /// <summary>异常类型（缺失POS / POS解析失败 / 缺失名称 / 重复位号 / 坐标重合 / 层级异常 ...）</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>关联设备位号（可为空，如全局性异常）</summary>
    public string? EquipmentName { get; set; }

    /// <summary>源 TXT 行号（1 起；全局异常可为 0）</summary>
    public int LineNumber { get; set; }

    /// <summary>原始内容（相关行原文）</summary>
    public string RawContent { get; set; } = string.Empty;

    /// <summary>说明</summary>
    public string Description { get; set; } = string.Empty;

    public override string ToString() => $"[{Type}] {EquipmentName ?? "-"} L{LineNumber}: {Description}";
}
