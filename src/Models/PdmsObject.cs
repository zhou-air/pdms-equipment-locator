using System.Globalization;

namespace PdmsEquipmentLocator.Models;

/// <summary>
/// PDMS Data Listing 中任意对象的通用节点（层级模型）。
/// Phase 1：承载 EQUIPMENT 的子对象，仅记录类型/名称/POS/ORI/行号。
/// Phase 2：增加全部原始属性行 Attributes，并在其上派生 Primitive 几何模型。
/// </summary>
public class PdmsObject
{
    /// <summary>PDMS 对象类型关键字，如 EQUIPMENT / ZONE / CYLINDER / NOZZLE</summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>对象名称（不含斜杠）；可能为 null（未命名对象很常见，如 NEW CYLINDER）</summary>
    public string? Name { get; set; }

    /// <summary>原始 POS 字符串（未修改，便于追溯）；可能为 null</summary>
    public string? RawPosition { get; set; }

    /// <summary>原始 ORI 字符串；可能为 null</summary>
    public string? Orientation { get; set; }

    /// <summary>NEW 行在源 TXT 中的行号（1 起）</summary>
    public int SourceLine { get; set; }

    /// <summary>全部原始属性行（关键字, 值），按出现顺序；供 Primitive 参数解析与追溯</summary>
    public List<(string Key, string Value)> Attributes { get; } = new();

    /// <summary>子对象</summary>
    public List<PdmsObject> Children { get; } = new();

    /// <summary>取第一个匹配关键字的属性值（大小写不敏感）；不存在返回 null</summary>
    public string? GetAttribute(string key)
    {
        foreach (var (k, v) in Attributes)
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                return v;
        return null;
    }

    /// <summary>取数值属性（自动剥离 mm 等单位后缀）；失败返回 null</summary>
    public double? GetNumericAttribute(string key)
    {
        var raw = GetAttribute(key);
        if (raw is null) return null;
        return ParseMmNumber(raw);
    }

    /// <summary>解析 "1234mm" / "1234.5mm" / "1234" → double</summary>
    public static double? ParseMmNumber(string raw)
    {
        raw = raw.Trim();
        if (raw.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
            raw = raw[..^2].Trim();
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public override string ToString()
    {
        var name = string.IsNullOrEmpty(Name) ? "(unnamed)" : "/" + Name;
        return $"{ObjectType}{name} @L{SourceLine}";
    }
}
