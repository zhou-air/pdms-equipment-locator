namespace PdmsEquipmentLocator.Models;

/// <summary>
/// 一次完整解析的结果：设备列表 + 异常列表 + 全局层级信息。
/// </summary>
public class ParseResult
{
    /// <summary>按出现顺序排列的所有 EQUIPMENT</summary>
    public List<Equipment> Equipments { get; } = new();

    /// <summary>解析过程中收集的全部异常/警告</summary>
    public List<ParseIssue> Issues { get; } = new();

    /// <summary>SITE 名称（如有）</summary>
    public string? SiteName { get; set; }

    /// <summary>源文件路径</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>解析结束时栈是否为空（层级是否完整闭合）</summary>
    public bool HierarchyClosed { get; set; }

    /// <summary>解析结束时栈中残留对象的描述（HierarchyClosed=false 时用于诊断）</summary>
    public IReadOnlyList<string> ResidualStack { get; set; } = Array.Empty<string>();
}
