using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdmsEquipmentLocator.Cad;
using PdmsEquipmentLocator.Export;
using PdmsEquipmentLocator.Models;
using PdmsEquipmentLocator.Parser;
using PdmsEquipmentLocator.Validation;

// ─────────────────────────────────────────────────────────────
// PdmsEquipmentLocator — 设备坐标解析 → JSON + Excel + DXF 定位图 + 设备俯视轮廓
// 用法1: pdms-locator convert <输入TXT> [输出目录] [--equipment 位号]
// 用法2（兼容 Step1）: pdms-locator step1 <输入TXT> [输出目录]
// 用法3（桌面拖放）: 把 Data Listing TXT 直接拖到 exe 图标上
// 输出（一次运行全部生成）:
//   step1_parse_result.json    设备明细 JSON（调试/追溯）
//   equipment_coordinates.xlsx 设备坐标 + 解析异常 + 统计（三 Sheet）
//   equipment_location.dxf     设备定位图（EQUIP_POINT/MARKER/TAG/OUTLINE）
//   primitive_report.xlsx      Primitive 统计 / 未支持明细 / ORI 解析异常
// --equipment V1011A：调试模式，只输出该设备（含 Primitive 中心/轴/标注与变换矩阵数值）
// ─────────────────────────────────────────────────────────────
Console.OutputEncoding = Encoding.UTF8;

bool interactive = false; // 拖放/双击模式：结束时停留等待查看结果
string inputPath;
string outDir;
string? debugEquipment = null;

// 解析 --equipment 参数（可与位置参数混合）
var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] is "--equipment" or "-e")
    {
        if (i + 1 < args.Length) { debugEquipment = args[++i]; }
        continue;
    }
    positional.Add(args[i]);
}
args = positional.ToArray();

if (args.Length >= 2 && (args[0] == "step1" || args[0] == "convert"))
{
    inputPath = Path.GetFullPath(args[1]);
    outDir = args.Length > 2
        ? Path.GetFullPath(args[2])
        : Path.Combine(AppContext.BaseDirectory, "output");
}
else if (args.Length == 1 && File.Exists(args[0]))
{
    // 拖放模式：直接把 TXT 拖到 exe 上
    interactive = true;
    inputPath = Path.GetFullPath(args[0]);
    outDir = Path.Combine(AppContext.BaseDirectory, "output");
}
else
{
    Console.WriteLine("PDMS Equipment Locator — 设备坐标解析 → Excel + DXF + 设备俯视轮廓");
    Console.WriteLine();
    Console.WriteLine("用法:");
    Console.WriteLine("  方式1  命令行: pdms-locator convert <输入TXT> [输出目录] [--equipment 位号]");
    Console.WriteLine("  方式2  桌面  : 把 Data Listing TXT 文件直接拖到本程序图标上");
    Console.WriteLine();
    Console.WriteLine("输出（一次运行全部生成）:");
    Console.WriteLine("  step1_parse_result.json    设备明细 JSON");
    Console.WriteLine("  equipment_coordinates.xlsx 设备坐标 / 解析异常 / 统计");
    Console.WriteLine("  equipment_location.dxf     设备定位图（十字定位点 + 位号 + 俯视轮廓）");
    Console.WriteLine("  primitive_report.xlsx      Primitive 统计 / 未支持 / ORI 异常");
    Console.WriteLine();
    Console.WriteLine("调试模式: convert input.txt --equipment V1011A");
    Console.WriteLine("  只输出该设备（含 Primitive 中心/轴/标注与变换矩阵数值验证）");
    Console.WriteLine();
    Console.WriteLine("按回车键退出...");
    Console.ReadLine();
    return 2;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"错误：输入文件不存在: {inputPath}");
    if (interactive) { Console.WriteLine("按回车键退出..."); Console.ReadLine(); }
    return 1;
}
Directory.CreateDirectory(outDir);

var sw = System.Diagnostics.Stopwatch.StartNew();

var parser = new PdmsDataListingParser();
ParseResult result = parser.Parse(inputPath);
var stats = EquipmentValidator.Validate(result);

sw.Stop();

// ── 控制台报表 ──────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"输入文件 : {inputPath}");
Console.WriteLine($"解析耗时 : {sw.ElapsedMilliseconds} ms");
Console.WriteLine($"层级闭合 : {(result.HierarchyClosed ? "是" : "否")}");
Console.WriteLine();
Console.WriteLine($"══════════ 设备明细（共 {result.Equipments.Count} 台） ══════════");
Console.WriteLine($"{"#",-3} {"位号",-14} {"X",-12} {"Y",-12} {"Z",-12} {"ZONE",-32} {"ORI",-40} {"行号",-6} 状态");
Console.WriteLine(new string('─', 160));

int idx = 0;
foreach (var eq in result.Equipments)
{
    idx++;
    string x = eq.Position?.X.ToString("0.###") ?? "-";
    string y = eq.Position?.Y.ToString("0.###") ?? "-";
    string z = eq.Position?.Z.ToString("0.###") ?? "-";
    string ori = eq.Orientation ?? "-";
    string rawPos = eq.RawPosition ?? "-";
    Console.WriteLine($"{idx,-3} {eq.Name,-14} {x,-12} {y,-12} {z,-12} {eq.ZoneName,-32} {ori,-40} {eq.SourceLine,-6} {eq.Status}");
    Console.WriteLine($"    ↳ 原始POS: {rawPos}   子对象数: {eq.Children.Count}");
}

Console.WriteLine();
Console.WriteLine("══════════ 统计 ══════════");
Console.WriteLine($"EQUIPMENT 总数          : {stats.TotalEquipment}");
Console.WriteLine($"成功解析 POS            : {stats.ParsedOk}");
Console.WriteLine($"缺失 POS                : {stats.MissingPos}");
Console.WriteLine($"POS 解析失败            : {stats.InvalidPos}");
Console.WriteLine($"缺失名称                : {stats.MissingName}");
Console.WriteLine($"重复位号                : {stats.DuplicateNames}");
Console.WriteLine($"XY 完全相同分组         : {stats.CoincidentXyGroups}（涉及 {stats.CoincidentXyEquipment} 台）");
if (stats.HasCoordinate)
{
    Console.WriteLine($"X 范围 : {stats.MinX} ~ {stats.MaxX}");
    Console.WriteLine($"Y 范围 : {stats.MinY} ~ {stats.MaxY}");
    Console.WriteLine($"Z 范围 : {stats.MinZ} ~ {stats.MaxZ}");
}

Console.WriteLine();
Console.WriteLine("══════════ 解析异常 / 警告 ══════════");
if (result.Issues.Count == 0)
{
    Console.WriteLine("（无）");
}
else
{
    foreach (var issue in result.Issues)
        Console.WriteLine(issue);
}

// ── JSON 调试输出 ───────────────────────────────────────────
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
};

var payload = new
{
    SourceFile = inputPath,
    result.SiteName,
    result.HierarchyClosed,
    Statistics = stats,
    Equipments = result.Equipments.Select(e => new
    {
        e.Name,
        e.ZoneName,
        e.SourceLine,
        e.RawPosition,
        e.Orientation,
        Position = e.Position is null ? null : new { e.Position.X, e.Position.Y, e.Position.Z },
        Status = e.Status.ToString(),
        e.StatusNote,
        ChildCount = e.Children.Count,
        ChildTypes = e.Children.Select(c => c.ObjectType).Distinct().OrderBy(t => t).ToArray()
    }),
    Issues = result.Issues
};

string jsonPath = Path.Combine(outDir, "step1_parse_result.json");
File.WriteAllText(jsonPath, JsonSerializer.Serialize(payload, jsonOptions));
Console.WriteLine();
Console.WriteLine($"JSON 输出已写入: {jsonPath}");

// ── Excel 输出（同一份 Equipment[]） ────────────────────────
string excelPath = ExcelExporter.Export(result, stats, outDir);
Console.WriteLine($"Excel 输出已写入: {excelPath}");

// ── Phase 2：设备俯视轮廓（同一份 Equipment[]，经统一 Transform3D） ──
var outlineBuilder = new OutlineBuilder { DebugMode = debugEquipment is not null };
var outlines = outlineBuilder.Build(result);

// ── Phase 2 控制台统计 ──────────────────────────────────────
PrintPhase2Report(outlines, outlineBuilder);

// ── DXF 输出（同一份 Equipment[] + 同一份轮廓记录） ─────────
DxfExportResult dxf = DxfExporter.Export(result, outDir, outlines, debugEquipment);
Console.WriteLine($"DXF  输出已写入: {dxf.FilePath}");

// ── Primitive 报告（统计 / 未支持明细 / ORI 异常） ──────────
string primitiveReportPath = PrimitiveReportExporter.Export(result, outlines, outlineBuilder.OriIssues, outDir);
Console.WriteLine($"Primitive 报告已写入: {primitiveReportPath}");

// ── 调试模式：变换矩阵与顶点数值输出（坐标变换正确性优先于图纸美观） ──
if (debugEquipment is not null)
    PrintDebugTransform(outlineBuilder, outlines, debugEquipment);

// ── 导出一致性自动验证（调试模式仅输出单台设备，跳过全量一致性检查） ──
Console.WriteLine();
if (debugEquipment is not null)
{
    Console.WriteLine($"（调试模式：仅输出 /{debugEquipment}，跳过全量一致性验证）");
}
else
{
    Console.WriteLine("══════════ 导出一致性验证 ══════════");
    int excelRows = result.Equipments.Count;
    int parsedOk = stats.ParsedOk;
    int dxfPoints = dxf.Points.Count;
    Console.WriteLine($"Parser 设备总数           : {result.Equipments.Count}");
    Console.WriteLine($"成功解析 POS（有效设备）   : {parsedOk}");
    Console.WriteLine($"Excel 设备行数             : {excelRows}");
    Console.WriteLine($"DXF 定位点数（EQUIP_POINT）: {dxfPoints}");
    Console.WriteLine($"DXF 定位圆数（EQUIP_MARKER）: {dxf.MarkerCount}");
    Console.WriteLine($"DXF 位号数（EQUIP_TAG）    : {dxf.TagCount}");
    Console.WriteLine($"DXF 轮廓数（EQUIP_OUTLINE）: {dxf.OutlineCount}");

    bool countOk = parsedOk == excelRows && excelRows == dxfPoints;
    Console.WriteLine(countOk ? "✔ 三者数量一致" : "✘ 数量不一致！");

    int hullOk = outlines.Count(r => r.Hull.Count >= 3);
    Console.WriteLine($"Primitive 有效凸包数         : {hullOk}");
    Console.WriteLine(hullOk == dxf.OutlineCount
        ? "✔ 凸包记录与 DXF 轮廓数一致"
        : "✘ 凸包记录与 DXF 轮廓数不一致！");

    int mismatch = 0;
    foreach (var p in dxf.Points)
    {
        var eq = result.Equipments.FirstOrDefault(e => e.Name == p.Name && e.SourceLine == p.SourceLine);
        if (eq?.Position is null || Math.Abs(eq.Position.X - p.X) > 1e-9 || Math.Abs(eq.Position.Y - p.Y) > 1e-9)
            mismatch++;
    }
    Console.WriteLine(mismatch == 0
        ? $"✔ DXF 定位点 ↔ Equipment 全量坐标比对一致（{dxf.Points.Count} 点，误差 < 1e-9 mm）"
        : $"✘ 有 {mismatch} 处 DXF 定位点坐标与 Equipment 不一致！");

    // ── 抽查代表性设备（Excel/DXF 坐标对照） ────────────────────
    var samples = PickSamples(result, dxf);
    Console.WriteLine();
    Console.WriteLine("── 抽查（Excel 与 DXF 坐标对照，误差阈值 1e-9 mm）──");
    Console.WriteLine($"{"位号",-14} {"Excel X",-14} {"Excel Y",-14} {"DXF X",-14} {"DXF Y",-14} 一致");
    foreach (var (eq, p) in samples)
    {
        bool same = Math.Abs(eq.Position!.X - p.X) < 1e-9 && Math.Abs(eq.Position.Y - p.Y) < 1e-9;
        Console.WriteLine($"{eq.Name,-14} {eq.Position.X,-14:0.###} {eq.Position.Y,-14:0.###} {p.X,-14:0.###} {p.Y,-14:0.###} {(same ? "✔" : "✘")}");
    }
}

if (interactive)
{
    Console.WriteLine();
    Console.WriteLine("按回车键退出...");
    Console.ReadLine();
}

return 0;

// ── Phase 2 统计报表 ────────────────────────────────────────
static void PrintPhase2Report(List<OutlineRecord> outlines, OutlineBuilder builder)
{
    Console.WriteLine();
    Console.WriteLine("══════════ Phase 2：设备俯视轮廓 ══════════");
    var ok = outlines.Where(r => r.Status == "OK").ToList();
    var fail = outlines.Where(r => r.Status != "OK").ToList();
    Console.WriteLine($"Primitive 轮廓记录        : {outlines.Count}");
    Console.WriteLine($"  生成成功（OK）          : {ok.Count}"
        + $"（BOX {ok.Count(r => r.PrimitiveType.Equals("BOX", StringComparison.OrdinalIgnoreCase))}"
        + $" + CYLINDER {ok.Count(r => r.PrimitiveType.Equals("CYLINDER", StringComparison.OrdinalIgnoreCase))}）");
    foreach (var g in fail.GroupBy(r => r.Status).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Key,-14}: {g.Count()}");
    Console.WriteLine($"ORI 解析异常              : {builder.OriIssues.Count} 条"
        + (builder.OriIssues.Count > 0 ? "（详见 primitive_report.xlsx「ORI解析异常」）" : ""));
    if (ok.Count > 0)
    {
        double zMin = ok.Min(r => r.WorldZMin), zMax = ok.Max(r => r.WorldZMax);
        Console.WriteLine($"World Z 范围（预留接地结构）: {zMin:0.###} ~ {zMax:0.###} mm");
    }
}

// ── 调试模式：变换矩阵与顶点数值输出 ────────────────────────
static void PrintDebugTransform(OutlineBuilder builder, List<OutlineRecord> outlines, string eqName)
{
    Console.WriteLine();
    Console.WriteLine($"══════════ 调试模式：/{eqName} 数值验证 ══════════");

    if (builder.EquipmentTransforms.TryGetValue(eqName, out var eqT))
        PrintMatrix($"Equipment World Transform（/{eqName}）", eqT);
    else
        Console.WriteLine("（设备无有效世界变换：POS 缺失或设备级 ORI 解析失败）");

    var recs = outlines
        .Where(r => string.Equals(r.EquipmentName, eqName, StringComparison.OrdinalIgnoreCase))
        .ToList();
    Console.WriteLine();
    Console.WriteLine($"Primitive 记录: {recs.Count} 条（含异常状态）");
    foreach (var r in recs)
    {
        Console.WriteLine();
        Console.WriteLine($"── {r.PrimitiveType} {r.PrimitiveName}  @L{r.SourceLine}  [{r.Status}]");
        Console.WriteLine($"   Local POS  : {(r.LocalPosition is null
            ? "-"
            : $"{r.LocalPosition.Value.X:0.###}, {r.LocalPosition.Value.Y:0.###}, {r.LocalPosition.Value.Z:0.###}")}"
            + $"   raw: {r.LocalPosRaw ?? "-"}");
        Console.WriteLine($"   Local ORI  : {r.LocalOriRaw ?? "-"}");
        if (r.LocalTransform is not null)
            PrintMatrix("Local Transform", r.LocalTransform);
        if (r.Status == "OK")
        {
            Console.WriteLine($"   World POS  : {r.WorldOrigin.X:0.###}, {r.WorldOrigin.Y:0.###}, {r.WorldOrigin.Z:0.###}");
            Console.WriteLine($"   World Z    : {r.WorldZMin:0.###} ~ {r.WorldZMax:0.###} mm");
            Console.WriteLine($"   World Vertices（{r.WorldVertices.Count}）:");
            foreach (var v in r.WorldVertices)
                Console.WriteLine($"     ({v.X:0.###}, {v.Y:0.###}, {v.Z:0.###})");
        }
        else
        {
            Console.WriteLine($"   （无几何顶点 — {r.StatusNote}）");
        }
    }
}

static void PrintMatrix(string title, Transform3D t)
{
    // 列向量旋转矩阵，4×4（世界 = R·局部 + T）：
    // | Xx Yx Zx Tx |
    // | Xy Yy Zy Ty |
    // | Xz Yz Zz Tz |
    // | 0  0  0  1  |
    Console.WriteLine($"   {title}:");
    Console.WriteLine($"     | {t.XAxis.X,10:0.####} {t.YAxis.X,10:0.####} {t.ZAxis.X,10:0.####} {t.Translation.X,12:0.###} |");
    Console.WriteLine($"     | {t.XAxis.Y,10:0.####} {t.YAxis.Y,10:0.####} {t.ZAxis.Y,10:0.####} {t.Translation.Y,12:0.###} |");
    Console.WriteLine($"     | {t.XAxis.Z,10:0.####} {t.YAxis.Z,10:0.####} {t.ZAxis.Z,10:0.####} {t.Translation.Z,12:0.###} |");
    Console.WriteLine($"     | {0,10:0} {0,10:0} {0,10:0} {1,12:0} |");
}

// ── 抽查选择：前5台 + W方向 + E方向 + 小数坐标 + XY重合 ─────
static List<(Equipment Eq, DxfPointRecord Pt)> PickSamples(ParseResult result, DxfExportResult dxf)
{
    var byKey = result.Equipments
        .Where(e => e.Position is not null)
        .ToDictionary(e => (e.Name, e.SourceLine));
    var samples = new List<(Equipment, DxfPointRecord)>();

    void Add(string name, int line)
    {
        if (!byKey.TryGetValue((name, line), out var eq)) return;
        var pt = dxf.Points.FirstOrDefault(p => p.Name == name && p.SourceLine == line);
        if (pt is null) return;
        if (!samples.Contains((eq, pt)))
            samples.Add((eq, pt));
    }

    foreach (var p in dxf.Points.Take(5)) Add(p.Name, p.SourceLine);
    foreach (var p in dxf.Points.Skip(Math.Max(0, dxf.Points.Count - 3))) Add(p.Name, p.SourceLine); // 末 3 台
    var w = dxf.Points.FirstOrDefault(p => p.X < 0);          // W 方向（X 为负）
    if (w is not null) Add(w.Name, w.SourceLine);
    var e = dxf.Points.FirstOrDefault(p => p.X > 0);          // E 方向（X 为正）
    if (e is not null) Add(e.Name, e.SourceLine);
    var frac = dxf.Points.FirstOrDefault(p => p.X != Math.Floor(p.X) || p.Y != Math.Floor(p.Y)); // 小数坐标
    if (frac is not null) Add(frac.Name, frac.SourceLine);
    foreach (var eq in result.Equipments
                 .Where(x => x.Position is not null
                          && result.Issues.Any(i => i.Type == "XY坐标重合" && i.EquipmentName == x.Name))
                 .Take(2))
        Add(eq.Name, eq.SourceLine);

    return samples.Take(10).ToList();
}
