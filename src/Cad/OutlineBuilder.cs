using System.Numerics;
using PdmsEquipmentLocator.Models;
using PdmsEquipmentLocator.Parser;

namespace PdmsEquipmentLocator.Cad;

/// <summary>
/// 单个 Primitive 的俯视轮廓计算结果。
/// </summary>
public class OutlineRecord
{
    public string EquipmentName { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public string PrimitiveType { get; set; } = string.Empty;
    public string PrimitiveName { get; set; } = string.Empty;
    public int SourceLine { get; set; }
    /// <summary>父级链（如 EQUIPMENT/SUBEQUIPMENT）</summary>
    public string ParentChain { get; set; } = string.Empty;
    /// <summary>局部 POS（相对父级）</summary>
    public string? LocalPosRaw { get; set; }
    /// <summary>局部 ORI 原文</summary>
    public string? LocalOriRaw { get; set; }
    /// <summary>解析后的局部位置（相对父级，mm；调试/报告用）</summary>
    public Vector3? LocalPosition { get; set; }
    /// <summary>局部变换（POS+ORI 复合；调试模式才填充）</summary>
    public Transform3D? LocalTransform { get; set; }
    /// <summary>状态：OK / 未支持类型 / 参数缺失 / ORI解析失败 / POS解析失败 / 设备无坐标</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusNote { get; set; } = string.Empty;
    /// <summary>世界坐标下的俯视轮廓（凸包，逆时针，mm）</summary>
    public List<Vector2> Hull { get; } = new();
    /// <summary>世界 Z 范围（mm）</summary>
    public double WorldZMin { get; set; }
    public double WorldZMax { get; set; }
    /// <summary>世界坐标下的 Primitive 原点（局部原点映射）</summary>
    public Vector3 WorldOrigin { get; set; }
    /// <summary>世界坐标系下的三根轴方向（调试用）</summary>
    public Vector3 WorldXAxis { get; set; }
    public Vector3 WorldYAxis { get; set; }
    public Vector3 WorldZAxis { get; set; }
    /// <summary>世界坐标下的全部三维顶点（投影前；调试模式才填充，供数值验证）</summary>
    public List<Vector3> WorldVertices { get; } = new();
}

/// <summary>
/// Phase 2 轮廓构建器：从 Parser 得到的 Equipment[]（同一份数据集）出发，
/// 沿 EQUIPMENT → SUBEQUIPMENT → Primitive 层级递归复合 Transform3D，
/// 生成每个 Primitive 的世界坐标 XY 投影凸包。
///
/// 变换语义（经泵组设备族 P1081a/P1083a/P1052 与 V1021A/V1081 数据交叉验证）：
///   W_child = W_parent + R_parent · POS_child
///   R_child = R_parent · R_ORI(child)
/// </summary>
public class OutlineBuilder
{
    /// <summary>圆柱圆周采样点数（可配置）</summary>
    public int CylinderSegments { get; set; } = 32;

    /// <summary>调试模式：OutlineRecord 额外记录世界三维顶点（供 --equipment 数值验证）</summary>
    public bool DebugMode { get; set; }

    /// <summary>每台设备的世界变换（含设备级 ORI 旋转；首现位号为准）</summary>
    public Dictionary<string, Transform3D> EquipmentTransforms { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已支持的 Primitive 类型（不区分大小写）</summary>
    public static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "BOX", "CYLINDER"
    };

    /// <summary>非几何对象（容器/属性类，不参与"未支持 Primitive"明细）</summary>
    public static readonly HashSet<string> NonGeometricTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUBEQUIPMENT", "NOZZLE", "GASKET", "FLANGE", "ELBOW", "TEE", "REDUCER",
        "OLET", "VALVE", "BRANCH", "PIPE", "SNODE", "PAVERT", "SJOINT", "POGON",
        "INSTRUMENT", "PLOOP", "DPCARTESIAN", "DPSPHERICAL", "DDATA", "VVALUE",
        "PJOINT", "PNODE", "POINSP", "DPSET", "DDSE", "LOOP", "POINT", "VERTEX",
        "CURVE", "SPINE", "TMPLATE", "GENSEC", "FITTING", "PCOMPONENT", "PCOMP",
        "DUNION", "DATUM"
    };

    /// <summary>逐台设备构建轮廓</summary>
    public List<OutlineRecord> Build(ParseResult parse)
    {
        var records = new List<OutlineRecord>();

        foreach (var eq in parse.Equipments)
        {
            if (eq.Position is null)
            {
                // 设备本身无有效坐标：其下所有 Primitive 记录为不可计算（不静默）
                CollectEquipmentWithoutPosition(eq, records);
                continue;
            }

            // 设备级变换：POS 为世界坐标（ZONE 无偏移，若存在则 Parse 已记录守护 Issue）
            var eqWorld = Transform3D.FromTranslation(
                new Vector3((float)eq.Position.X, (float)eq.Position.Y, (float)eq.Position.Z));

            var ori = OrientationParser.ParseToTransform(eq.Orientation);
            if (ori.Transform is null)
            {
                // 设备 ORI 无法解析：记录该设备所有 Primitive 为 ORI 解析失败
                foreach (var node in eq.Children)
                    CollectAllDescendants(eq, node, records,
                        status: "ORI解析失败", note: $"设备级 ORI 解析失败: {ori.Error}");
                _oriIssues.Add((eq.Name, "EQUIPMENT", eq.SourceLine, eq.Orientation ?? "", ori.Error));
                continue;
            }
            eqWorld = Transform3D.Compose(eqWorld, ori.Transform);
            EquipmentTransforms.TryAdd(eq.Name, eqWorld);

            foreach (var child in eq.Children)
                Walk(eq, child, eqWorld, $"{eq.Name}", records);
        }

        return records;
    }

    /// <summary>ORI 解析异常汇总（对象名/类型/行号/原文/错误）</summary>
    public List<(string Owner, string Type, int Line, string Raw, string Error)> OriIssues => _oriIssues;
    private readonly List<(string, string, int, string, string)> _oriIssues = new();

    // ────────────────────────────────────────────────────────
    // 层级递归：SUBEQUIPMENT（及其他中间容器）携带自己的 POS/ORI 参与变换链
    // ────────────────────────────────────────────────────────
    private void Walk(Equipment eq, PdmsObject node, Transform3D parentWorld, string parentChain, List<OutlineRecord> records)
    {
        // 节点局部变换：POS（缺省 0,0,0）+ ORI（缺省单位）
        Transform3D? local = BuildLocal(node);
        if (local is null)
        {
            // 节点自身 POS/ORI 无法解析 → 该节点及其全部后代标记为变换失败，不下钻计算几何
            var (status, note) = _lastNodeError;
            CollectAllDescendants(eq, node, records, status, note);
            return;
        }

        var nodeWorld = Transform3D.Compose(parentWorld, local);

        if (node is BoxPrimitive or CylinderPrimitive)
            records.Add(BuildPrimitiveOutline(eq, node, parentChain, nodeWorld, local));

        var chain = string.IsNullOrEmpty(node.Name)
            ? $"{parentChain}/{node.ObjectType}@L{node.SourceLine}"
            : $"{parentChain}/{node.Name}";
        foreach (var child in node.Children)
            Walk(eq, child, nodeWorld, chain, records);
    }

    private (string status, string note) _lastNodeError;

    /// <summary>由节点 POS/ORI 构造局部变换；失败返回 null（错误写入 _lastNodeError）</summary>
    private Transform3D? BuildLocal(PdmsObject node)
    {
        Vector3 pos = Vector3.Zero;
        if (node.RawPosition is not null)
        {
            var outcome = PositionParser.Parse(node.RawPosition);
            if (outcome.Position is null)
            {
                _lastNodeError = ("POS解析失败", $"节点 {node} POS 解析失败: {outcome.Error}");
                return null;
            }
            pos = new Vector3((float)outcome.Position.X, (float)outcome.Position.Y, (float)outcome.Position.Z);
        }

        var ori = OrientationParser.ParseToTransform(node.Orientation);
        if (ori.Transform is null)
        {
            _lastNodeError = ("ORI解析失败", $"节点 {node} ORI 解析失败: {ori.Error}");
            _oriIssues.Add((DescribeOwner(node), node.ObjectType, node.SourceLine, node.Orientation ?? "", ori.Error));
            return null;
        }

        return Transform3D.Compose(Transform3D.FromTranslation(pos), ori.Transform);
    }

    // ────────────────────────────────────────────────────────
    // Primitive 几何：统一 3D 顶点 → 世界变换 → XY 投影 → 凸包
    // ────────────────────────────────────────────────────────
    private OutlineRecord BuildPrimitiveOutline(Equipment eq, PdmsObject node, string parentChain, Transform3D world, Transform3D local)
    {
        var rec = new OutlineRecord
        {
            EquipmentName = eq.Name,
            ZoneName = eq.ZoneName,
            PrimitiveType = node.ObjectType,
            PrimitiveName = node.Name ?? string.Empty,
            SourceLine = node.SourceLine,
            ParentChain = parentChain,
            LocalPosRaw = node.RawPosition,
            LocalOriRaw = node.Orientation,
            LocalPosition = local.Translation,
            LocalTransform = DebugMode ? local : null,
            WorldOrigin = world.Translation,
            WorldXAxis = world.XAxis,
            WorldYAxis = world.YAxis,
            WorldZAxis = world.ZAxis
        };

        List<Vector3> localVerts;
        switch (node)
        {
            case BoxPrimitive box:
                localVerts = BuildBoxVertices(box, rec);
                break;
            case CylinderPrimitive cyl:
                localVerts = BuildCylinderPoints(cyl, rec);
                break;
            default:
                rec.Status = "未支持类型";
                rec.StatusNote = $"{node.ObjectType} 尚未实现";
                return rec;
        }

        if (localVerts.Count == 0)
            return rec; // 参数缺失已写入 rec.Status

        // 世界坐标 → XY 投影 → 凸包
        var projected = new List<Vector2>(localVerts.Count);
        float zMin = float.MaxValue, zMax = float.MinValue;
        foreach (var v in localVerts)
        {
            var w = world.TransformPoint(v);
            projected.Add(new Vector2(w.X, w.Y));
            zMin = MathF.Min(zMin, w.Z);
            zMax = MathF.Max(zMax, w.Z);
            if (DebugMode) rec.WorldVertices.Add(w);
        }

        foreach (var p in ConvexHull.Compute(projected))
            rec.Hull.Add(p);

        rec.WorldZMin = zMin;
        rec.WorldZMax = zMax;
        if (rec.Status.Length == 0) rec.Status = "OK";
        return rec;
    }

    /// <summary>BOX：以 POS 为中心的 8 个角点（±XLEN/2, ±YLEN/2, ±ZLEN/2）</summary>
    private List<Vector3> BuildBoxVertices(BoxPrimitive box, OutlineRecord rec)
    {
        var x = box.XLen;
        var y = box.YLen;
        var z = box.ZLen;
        if (x is null || y is null || z is null)
        {
            var missing = new List<string>();
            if (x is null) missing.Add("XLEN");
            if (y is null) missing.Add("YLEN");
            if (z is null) missing.Add("ZLEN");
            rec.Status = "参数缺失";
            rec.StatusNote = $"BOX 缺少 {string.Join("/", missing)}（原始值: " +
                $"XLEN={box.GetAttribute("XLEN") ?? "-"}, YLEN={box.GetAttribute("YLEN") ?? "-"}, ZLEN={box.GetAttribute("ZLEN") ?? "-"}）";
            return new List<Vector3>();
        }

        float hx = (float)x.Value / 2f, hy = (float)y.Value / 2f, hz = (float)z.Value / 2f;
        var verts = new List<Vector3>(8);
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sy in new[] { -1f, 1f })
        foreach (var sz in new[] { -1f, 1f })
            verts.Add(new Vector3(sx * hx, sy * hy, sz * hz));
        return verts;
    }

    /// <summary>CYLINDER：轴沿局部 Z，中心在 POS，两端面 ±HEIG/2，圆周采样 N 点 × 2 端</summary>
    private List<Vector3> BuildCylinderPoints(CylinderPrimitive cyl, OutlineRecord rec)
    {
        var d = cyl.Diameter;
        var h = cyl.Height;
        if (d is null || h is null)
        {
            var missing = new List<string>();
            if (d is null) missing.Add("DIAM");
            if (h is null) missing.Add("HEIG");
            rec.Status = "参数缺失";
            rec.StatusNote = $"CYLINDER 缺少 {string.Join("/", missing)}（原始值: " +
                $"DIAM={cyl.GetAttribute("DIAM") ?? "-"}, HEIG={cyl.GetAttribute("HEIG") ?? "-"}）";
            return new List<Vector3>();
        }

        float r = (float)d.Value / 2f, hh = (float)h.Value / 2f;
        int n = CylinderSegments;
        var pts = new List<Vector3>(2 * n);
        for (int e = 0; e < 2; e++)
        {
            float z = e == 0 ? -hh : hh;
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                pts.Add(new Vector3(r * MathF.Cos((float)a), r * MathF.Sin((float)a), z));
            }
        }
        return pts;
    }

    // ────────────────────────────────────────────────────────
    // 异常收集辅助
    // ────────────────────────────────────────────────────────
    private void CollectEquipmentWithoutPosition(Equipment eq, List<OutlineRecord> records)
    {
        foreach (var node in eq.Children)
            CollectAllDescendants(eq, node, records, "设备无坐标", $"设备 /{eq.Name} 无有效 POS，无法计算世界坐标");
    }

    private void CollectAllDescendants(Equipment eq, PdmsObject node, List<OutlineRecord> records, string status, string note)
    {
        var rec = MakeUnsupportedRecord(eq, node, DescribeChain(node), status, note);
        records.Add(rec);
        foreach (var child in node.Children)
            CollectAllDescendants(eq, child, records, status, note);
    }

    private OutlineRecord MakeUnsupportedRecord(Equipment eq, PdmsObject node, string parentChain, string status, string note)
        => new()
        {
            EquipmentName = eq.Name,
            ZoneName = eq.ZoneName,
            PrimitiveType = node.ObjectType,
            PrimitiveName = node.Name ?? string.Empty,
            SourceLine = node.SourceLine,
            ParentChain = parentChain,
            LocalPosRaw = node.RawPosition,
            LocalOriRaw = node.Orientation,
            Status = status,
            StatusNote = note
        };

    private static string DescribeChain(PdmsObject node) => node.ToString() ?? string.Empty;
    private static string DescribeOwner(PdmsObject node) => string.IsNullOrEmpty(node.Name) ? $"(unnamed@L{node.SourceLine})" : node.Name;
}
