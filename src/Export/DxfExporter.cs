using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using PdmsEquipmentLocator.Cad;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Export;

/// <summary>
/// DXF 导出器 —— 直接消费 Parser 得到的同一份 ParseResult，绝不重新解析 TXT。
/// 输出 equipment_location.dxf（DXF R2000，AutoCAD 2021 可直接打开）。
///
/// 图层：
///   EQUIP_POINT   真实设备定位点（十字，中心严格等于设备 X/Y）
///   EQUIP_MARKER  视觉定位小圆（仅定位符号，不代表设备尺寸）
///   EQUIP_TAG     设备位号文字（简单放在设备点上方，暂不做自动避让）
///   EQUIP_OUTLINE Phase 2：设备真实俯视轮廓（Primitive XY 投影凸包，闭合多段线）
///
/// 调试模式（--equipment V1011A）：只输出该设备，并附加
///   DEBUG_CENTER（Primitive 世界原点）、DEBUG_AXES（世界轴 X红/Y绿/Z蓝）、
///   DEBUG_NAME（Primitive 类型与行号标注）。
/// </summary>
public static class DxfExporter
{
    public const string FileName = "equipment_location.dxf";

    // 图层名
    public const string LayerPoint   = "EQUIP_POINT";
    public const string LayerMarker  = "EQUIP_MARKER";
    public const string LayerTag     = "EQUIP_TAG";
    public const string LayerOutline = "EQUIP_OUTLINE";
    public const string LayerDebugCenter = "DEBUG_CENTER";
    public const string LayerDebugAxes   = "DEBUG_AXES";
    public const string LayerDebugName   = "DEBUG_NAME";

    // 定位符号尺寸（均为“符号尺寸”，绝不代表设备实际尺寸）
    public const double CrossHalfLength = 200.0;   // 十字半长：中心左右/上下各 200mm → 十字总长 400mm
    public const double MarkerRadius    = 200.0;   // 定位圆半径
    public const double TagHeight       = 250.0;   // 位号文字高度
    public const double TagGap          = 80.0;    // 文字距定位圆的间隙
    public const double DebugAxisLength = 600.0;   // 调试轴长度（符号尺寸）

    /// <param name="outlines">Phase 2 轮廓记录（null 则不画轮廓）</param>
    /// <param name="debugEquipment">调试模式：只输出该设备（null = 全部设备）</param>
    public static DxfExportResult Export(ParseResult result, string outDir,
        IReadOnlyList<OutlineRecord>? outlines = null, string? debugEquipment = null)
    {
        string path = Path.Combine(outDir, FileName);

        var doc = new DxfDocument(DxfVersion.AutoCad2000);

        var layerPoint   = new Layer(LayerPoint)   { Color = AciColor.Red };
        var layerMarker  = new Layer(LayerMarker)  { Color = AciColor.Yellow };
        var layerTag     = new Layer(LayerTag)     { Color = AciColor.Cyan };
        var layerOutline = new Layer(LayerOutline) { Color = AciColor.Green };
        doc.Layers.Add(layerPoint);
        doc.Layers.Add(layerMarker);
        doc.Layers.Add(layerTag);
        doc.Layers.Add(layerOutline);

        Layer? layerDbgCenter = null, layerDbgAxes = null, layerDbgName = null;
        if (debugEquipment is not null)
        {
            layerDbgCenter = new Layer(LayerDebugCenter) { Color = new AciColor(255, 255, 255) };
            layerDbgAxes   = new Layer(LayerDebugAxes)   { Color = new AciColor(255, 255, 255) };
            layerDbgName   = new Layer(LayerDebugName)   { Color = AciColor.Magenta };
            doc.Layers.Add(layerDbgCenter);
            doc.Layers.Add(layerDbgAxes);
            doc.Layers.Add(layerDbgName);
        }

        // 位号文字使用 TrueType 字体样式（兼容中文位号，AutoCAD 可正常显示；txt.shx 会显示中文为 ???）
        var tagStyle = new TextStyle("TAG_STYLE", "Arial.ttf");
        doc.TextStyles.Add(tagStyle);

        var res = new DxfExportResult { FilePath = path, DebugEquipment = debugEquipment };

        foreach (var eq in result.Equipments)
        {
            if (eq.Position is null)
                continue; // 无有效坐标的设备不出现在 DXF（已记录在 Excel/JSON 异常中）

            if (debugEquipment is not null && !MatchesEquipment(eq, debugEquipment))
                continue;

            double x = eq.Position.X;
            double y = eq.Position.Y;

            res.Points.Add(new DxfPointRecord { Name = eq.Name, X = x, Y = y, SourceLine = eq.SourceLine });

            // ── EQUIP_POINT：十字（两条 LINE，交点 = 设备真实坐标） ──
            var hLine = new Line(new Vector2(x - CrossHalfLength, y), new Vector2(x + CrossHalfLength, y)) { Layer = layerPoint };
            var vLine = new Line(new Vector2(x, y - CrossHalfLength), new Vector2(x, y + CrossHalfLength)) { Layer = layerPoint };
            doc.Entities.Add(hLine);
            doc.Entities.Add(vLine);

            // ── EQUIP_MARKER：定位小圆（圆心 = 设备真实坐标） ──
            var circle = new Circle(new Vector2(x, y), MarkerRadius) { Layer = layerMarker };
            doc.Entities.Add(circle);
            res.MarkerCount++;

            // ── EQUIP_TAG：设备位号，简单放在设备点正上方 ──
            // 第一版不做自动避让；若文字重叠允许存在（下一步实现避让）。
            var text = new Text(eq.Name, new Vector2(x, y + MarkerRadius + TagGap), TagHeight)
            {
                Layer = layerTag,
                Style = tagStyle
            };
            doc.Entities.Add(text);
            res.TagCount++;
        }

        // ── Phase 2：EQUIP_OUTLINE（真实设备俯视轮廓，闭合多段线） ──
        if (outlines is not null)
        {
            foreach (var rec in outlines)
            {
                if (debugEquipment is not null && !string.Equals(rec.EquipmentName, debugEquipment, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (rec.Hull.Count < 3)
                    continue; // 无有效几何（缺失参数/未支持类型等已记录在报告）

                var pl = new Polyline2D(rec.Hull.Select(p => new Polyline2DVertex(p.X, p.Y, 0.0)), true)
                {
                    Layer = layerOutline
                };
                doc.Entities.Add(pl);
                res.OutlineCount++;

                if (debugEquipment is not null)
                {
                    // 调试：Primitive 世界原点小圆
                    var c = new Circle(new Vector2(rec.WorldOrigin.X, rec.WorldOrigin.Y), 100.0)
                    { Layer = layerDbgCenter! };
                    doc.Entities.Add(c);

                    // 调试：世界轴 X红 / Y绿 / Z蓝（显式 netDxf.Vector3，避免与 System.Numerics 混淆）
                    void AxisLine(System.Numerics.Vector3 dir, AciColor color)
                    {
                        var start = new netDxf.Vector3(rec.WorldOrigin.X, rec.WorldOrigin.Y, rec.WorldOrigin.Z);
                        var end = new netDxf.Vector3(
                            rec.WorldOrigin.X + dir.X * DebugAxisLength,
                            rec.WorldOrigin.Y + dir.Y * DebugAxisLength,
                            rec.WorldOrigin.Z + dir.Z * DebugAxisLength);
                        var l = new Line(start, end)
                        { Layer = layerDbgAxes!, Color = color };
                        doc.Entities.Add(l);
                    }
                    AxisLine(rec.WorldXAxis, AciColor.Red);
                    AxisLine(rec.WorldYAxis, AciColor.Green);
                    AxisLine(rec.WorldZAxis, AciColor.Blue);

                    // 调试：名称标注（类型 + 位号 + 行号）
                    var label = string.IsNullOrEmpty(rec.PrimitiveName)
                        ? $"{rec.PrimitiveType}@L{rec.SourceLine}"
                        : $"{rec.PrimitiveType} {rec.PrimitiveName}@L{rec.SourceLine}";
                    var t = new Text(label, new Vector2(rec.WorldOrigin.X, rec.WorldOrigin.Y + 150), 200)
                    { Layer = layerDbgName!, Style = tagStyle };
                    doc.Entities.Add(t);
                }
            }
        }

        doc.Save(path);
        return res;
    }

    /// <summary>调试设备匹配（忽略大小写，允许带或不带斜杠）</summary>
    private static bool MatchesEquipment(Equipment eq, string filter)
    {
        string f = filter.Trim().TrimStart('/');
        return string.Equals(eq.Name, f, StringComparison.OrdinalIgnoreCase);
    }
}
