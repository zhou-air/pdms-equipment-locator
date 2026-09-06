# PDMS Equipment Locator · PDMS 设备定位工具

把 AVEVA PDMS / Plant Design 导出的 Data Listing 文本转换为设备坐标表、DXF 定位图和解析报告，减少设备定位资料的手工整理。

当前阶段：**Phase 2A**。采用结构化文本解析和几何计算，支持 BOX / CYLINDER 俯视轮廓。

## 主要功能

- 识别设备位号、ZONE、位置、方向及对象层级，保留原始属性与来源行号。
- 输出数值型 X/Y/Z 坐标 Excel、设备明细 JSON、DXF 定位图及 Primitive 报告。
- 通过三维变换、XY 投影与凸包生成 BOX / CYLINDER 轮廓。
- 支持按设备位号调试，输出局部轴、中心及变换矩阵。
- 未支持的几何类型和解析异常进入报告，便于人工复核。

## 环境与运行

源码构建需要 .NET 10 SDK。生成 Excel / DXF 无需安装 Excel 或 AutoCAD。

```powershell
dotnet restore ./src/PdmsEquipmentLocator.csproj
dotnet run --project ./src/PdmsEquipmentLocator.csproj -- convert "D:/example/input.txt" "D:/example/output"
```

仅调试指定设备：

```powershell
dotnet run --project ./src/PdmsEquipmentLocator.csproj -- convert "D:/example/input.txt" "D:/example/output" --equipment V1011A
```

## 输出文件

| 文件 | 内容 |
| --- | --- |
| `step1_parse_result.json` | 设备明细与解析信息 |
| `equipment_coordinates.xlsx` | 设备坐标、解析异常与统计 |
| `equipment_location.dxf` | 定位点、标记、位号及支持的俯视轮廓 |
| `primitive_report.xlsx` | Primitive 统计、未支持类型及方向解析异常 |

## 测试与打包

```powershell
dotnet test ./src/PdmsEquipmentLocator.Tests/PdmsEquipmentLocator.Tests.csproj -c Release
dotnet publish ./src/PdmsEquipmentLocator.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

Windows 自包含发布后，可将输入 TXT 拖到生成的 EXE 上运行。

## 当前边界

- 坐标约定为 E=+X、N=+Y、U=+Z，单位 mm。
- 目前只支持 BOX / CYLINDER 轮廓；多个 Primitive 不做布尔合并。
- 位号文字暂不自动避让，定位圆是显示标记，不代表设备尺寸。
- 非零 ZONE 位置会进入异常提示，使用结果前应复核坐标体系。
- 仓库包含源码与说明；实际工程输入、输出和预编译程序不随源码上传。

详细说明见 [使用说明](使用说明.txt) 与 [项目功能与结构说明](项目功能与结构说明.md)。
