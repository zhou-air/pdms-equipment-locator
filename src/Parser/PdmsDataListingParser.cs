using System.Text;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Parser;

/// <summary>
/// PDMS Data Listing TXT 层级解析器。
///
/// 文件由 "NEW &lt;TYPE&gt; [/名称]" 打开对象、由 "END" 关闭对象，
/// 属性行（POS / ORI / BUIL / ...）归属于栈顶对象。
/// 因此 EQUIPMENT 内部子对象（CYLINDER / BOX / NOZZLE ...）的 POS
/// 永远不会误挂在 EQUIPMENT 自身——只有 EQUIPMENT 直接拥有的属性行
/// （即遇到下一个 NEW 之前）才会成为设备自身 POS。
///
/// 第一阶段仅深度解析 EQUIPMENT（及所属 ZONE 归属），
/// 其余对象按通用 PdmsObject 保留在层级树中，为第二阶段 Primitive
/// 解析预留。
/// </summary>
public class PdmsDataListingParser
{
    private const int MaxDepth = 64; // 层级保护，防止异常文件导致无限嵌套

    /// <summary>
    /// 解析 Data Listing 文件。
    /// </summary>
    public ParseResult Parse(string filePath)
    {
        var result = new ParseResult { SourceFile = filePath };

        var stack = new Stack<PdmsObject>();
        string? currentZone = null;
        // 若文件没有 INPUT BEGIN/END 包裹，则整文件按可解析区处理
        bool insideInput = true;
        bool inputEnded = false;

        var lines = File.ReadAllLines(filePath, DetectEncoding(filePath));

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1; // 1 起行号
            string raw = lines[i];
            string line = raw.Trim();

            // 空行与注释
            if (line.Length == 0) continue;
            if (line.StartsWith("--")) continue;
            if (line.StartsWith("$S")) continue;

            // INPUT BEGIN / INPUT END 之外的区域基本是错误处理样板，跳过结构解析
            if (line.StartsWith("INPUT BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                insideInput = true;
                continue;
            }
            if (line.StartsWith("INPUT END", StringComparison.OrdinalIgnoreCase))
            {
                insideInput = false;
                inputEnded = true;
                continue;
            }
            if (!insideInput) continue;
            if (inputEnded) continue;

            // NEW <TYPE> [/name]
            if (line.StartsWith("NEW ", StringComparison.OrdinalIgnoreCase))
            {
                var (type, name) = ParseNewLine(line);

                // 顶层命令误以 NEW 开头的情况不太可能出现在 INPUT 区内，此处按对象处理
                PdmsObject obj = type.ToUpperInvariant() switch
                {
                    "BOX"      => new BoxPrimitive { ObjectType = type, Name = name, SourceLine = lineNumber },
                    "CYLINDER" => new CylinderPrimitive { ObjectType = type, Name = name, SourceLine = lineNumber },
                    _          => new PdmsObject { ObjectType = type, Name = name, SourceLine = lineNumber }
                };

                if (stack.Count >= MaxDepth)
                {
                    result.Issues.Add(new ParseIssue
                    {
                        Type = "层级异常",
                        EquipmentName = name,
                        LineNumber = lineNumber,
                        RawContent = raw,
                        Description = $"对象嵌套深度超过 {MaxDepth}，忽略该 NEW 行"
                    });
                    continue;
                }

                if (type.Equals("SITE", StringComparison.OrdinalIgnoreCase))
                {
                    result.SiteName = name;
                    stack.Push(obj);
                    continue;
                }

                if (type.Equals("ZONE", StringComparison.OrdinalIgnoreCase))
                {
                    currentZone = name ?? string.Empty;
                    stack.Push(obj);
                    _zoneNodes.Add(obj);
                    continue;
                }

                if (type.Equals("EQUIPMENT", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = new Equipment
                    {
                        Name = name ?? string.Empty,
                        ZoneName = currentZone ?? string.Empty,
                        SourceLine = lineNumber,
                        Status = name is null ? ParseStatus.MissingName : ParseStatus.Pending
                    };
                    if (name is null)
                    {
                        result.Issues.Add(new ParseIssue
                        {
                            Type = "缺失名称",
                            EquipmentName = null,
                            LineNumber = lineNumber,
                            RawContent = raw,
                            Description = "NEW EQUIPMENT 之后没有位号名称"
                        });
                    }
                    _equipmentByNode[obj] = eq;
                    stack.Push(obj);
                    continue;
                }

                // 其余对象（SUBEQUIPMENT / NOZZLE / Primitive / 管道元件 ...）：
                // 进入层级树，但只按通用节点记录
                if (stack.Count > 0)
                    stack.Peek().Children.Add(obj);
                stack.Push(obj);
                continue;
            }

            // END
            if (line.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count > 0)
                {
                    var closed = stack.Pop();
                    if (_equipmentByNode.Remove(closed, out var eq))
                        FinalizeEquipment(eq, closed, result);
                }
                else
                {
                    result.Issues.Add(new ParseIssue
                    {
                        Type = "层级异常",
                        LineNumber = lineNumber,
                        RawContent = raw,
                        Description = "遇到多余 END：对象栈为空"
                    });
                }
                continue;
            }

            // 属性行：归属栈顶对象
            if (stack.Count == 0) continue; // 顶层散落属性（理论上不应出现），忽略
            if (TrySplitAttribute(line, out var attr, out var value))
            {
                var owner = stack.Peek();
                owner.Attributes.Add((attr, value)); // Phase 2：保存全部原始属性
                if (attr.Equals("POS", StringComparison.OrdinalIgnoreCase))
                {
                    owner.RawPosition ??= value; // 只取第一条 POS
                }
                else if (attr.Equals("ORI", StringComparison.OrdinalIgnoreCase))
                {
                    owner.Orientation ??= value;
                }
            }
            // 无法识别的行：不报错（Data Listing 属性关键字极多），静默归类为未知属性
        }

        // ZONE 级坐标守护：本工具将设备 POS 视为世界坐标（Step 1 语义），
        // 若 ZONE 自身带 POS/ORI 则显式记录，绝不静默产生坐标偏差。
        foreach (var zone in _zoneNodes)
        {
            if (zone.RawPosition is not null)
                result.Issues.Add(new ParseIssue
                {
                    Type = "ZONE坐标偏移",
                    LineNumber = zone.SourceLine,
                    RawContent = $"POS {zone.RawPosition}",
                    Description = $"ZONE /{zone.Name} 带 POS，本工具未将其纳入坐标链，设备坐标可能整体偏移"
                });
            if (zone.Orientation is not null)
                result.Issues.Add(new ParseIssue
                {
                    Type = "ZONE坐标偏移",
                    LineNumber = zone.SourceLine,
                    RawContent = $"ORI {zone.Orientation}",
                    Description = $"ZONE /{zone.Name} 带 ORI，本工具未将其纳入坐标链，设备朝向可能整体旋转"
                });
        }

        result.HierarchyClosed = stack.Count == 0;
        if (stack.Count > 0)
        {
            result.ResidualStack = stack.Select(o => o.ToString()).Reverse().ToArray();
            result.Issues.Add(new ParseIssue
            {
                Type = "层级异常",
                LineNumber = 0,
                RawContent = string.Join(" -> ", result.ResidualStack),
                Description = $"文件结束时仍有 {stack.Count} 个对象未闭合（END 数量不足）"
            });
        }

        return result;
    }

    // ---- 内部：EQUIPMENT 节点 -> Equipment 映射 ----
    private readonly Dictionary<PdmsObject, Equipment> _equipmentByNode = new();

    // ---- 内部：ZONE 节点（用于坐标偏移守护检查） ----
    private readonly List<PdmsObject> _zoneNodes = new();

    /// <summary>NEW 行拆解：类型 + 可选名称</summary>
    private static (string Type, string? Name) ParseNewLine(string line)
    {
        // 形如: NEW EQUIPMENT /V1011A   或   NEW CYLINDER
        var rest = line.Substring(4).Trim();
        int sp = rest.IndexOf(' ');
        string type, tail;
        if (sp < 0)
        {
            type = rest;
            tail = string.Empty;
        }
        else
        {
            type = rest[..sp].Trim();
            tail = rest[sp..].Trim();
        }

        string? name = null;
        if (tail.StartsWith("/"))
            name = tail.Substring(1).Trim();
        else if (tail.Length > 0)
            name = tail; // 容错：名称未带斜杠

        return (type, name);
    }

    /// <summary>属性行拆解：第一个空白前为关键字，其余为值</summary>
    private static bool TrySplitAttribute(string line, out string attr, out string value)
    {
        attr = string.Empty;
        value = string.Empty;
        int sp = line.IndexOf(' ');
        if (sp < 0)
        {
            attr = line;
            return true; // 无值属性（如 PLANU unset 之外的裸关键字，少见）
        }
        attr = line[..sp];
        value = line[(sp + 1)..].Trim();
        return true;
    }

    /// <summary>设备块闭合：汇总其自身 POS/ORI，完成坐标解析与状态判定</summary>
    private static void FinalizeEquipment(Equipment eq, PdmsObject node, ParseResult result)
    {
        eq.RawPosition = node.RawPosition;
        eq.Orientation = node.Orientation;
        eq.Children.AddRange(node.Children);

        if (eq.Status == ParseStatus.MissingName)
        {
            // 名称缺失时不再覆盖状态，但若同时缺 POS 一并记录
        }

        if (node.RawPosition is null)
        {
            if (eq.Status == ParseStatus.Pending)
                eq.Status = ParseStatus.MissingPos;
            result.Issues.Add(new ParseIssue
            {
                Type = "缺失POS",
                EquipmentName = string.IsNullOrEmpty(eq.Name) ? null : eq.Name,
                LineNumber = eq.SourceLine,
                RawContent = $"NEW EQUIPMENT /{eq.Name}",
                Description = $"设备 /{eq.Name} 块内没有 POS 属性行（ZONE={eq.ZoneName}）"
            });
        }
        else
        {
            var outcome = PositionParser.Parse(node.RawPosition);
            if (outcome.Position is null)
            {
                if (eq.Status == ParseStatus.Pending)
                    eq.Status = ParseStatus.InvalidPos;
                eq.StatusNote = outcome.Error;
                result.Issues.Add(new ParseIssue
                {
                    Type = "POS解析失败",
                    EquipmentName = string.IsNullOrEmpty(eq.Name) ? null : eq.Name,
                    LineNumber = eq.SourceLine,
                    RawContent = $"POS {node.RawPosition}",
                    Description = outcome.Error
                });
            }
            else
            {
                if (eq.Status == ParseStatus.Pending)
                    eq.Status = ParseStatus.Ok;
                eq.Position = outcome.Position;
            }
        }

        result.Equipments.Add(eq);
    }

    private static Encoding DetectEncoding(string path)
    {
        // Data Listing 通常为 UTF-8（含 BOM）；逐字节探测，失败回退 UTF-8
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bom = new byte[4];
        int n = fs.Read(bom, 0, 4);
        if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(true);
        return new UTF8Encoding(false);
    }
}
