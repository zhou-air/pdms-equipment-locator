using PdmsEquipmentLocator.Parser;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// POS 解析测试：E/W/N/S/U/D → ±X/±Y/±Z，mm，不取整。
/// </summary>
public class PositionParserTests
{
    [Theory]
    [InlineData("E 71700mm N 3505mm U 2021mm", 71700, 3505, 2021)]
    [InlineData("W 83665mm N 8604mm U 2080mm", -83665, 8604, 2080)]
    [InlineData("E 6529.753mm N 7415.652mm U 1866mm", 6529.753, 7415.652, 1866)]
    [InlineData("E 2920mm N 1760mm D 1200mm", 2920, 1760, -1200)]
    [InlineData("W 1850mm S 250mm D 1250mm", -1850, -250, -1250)]
    public void Parse_FullThreeAxis(string pos, double x, double y, double z)
    {
        var r = PositionParser.Parse(pos);
        Assert.NotNull(r.Position);
        Assert.Equal(x, r.Position!.X, 6);
        Assert.Equal(y, r.Position.Y, 6);
        Assert.Equal(z, r.Position.Z, 6);
        Assert.Equal(string.Empty, r.Error);
    }

    [Fact]
    public void Parse_Empty_ReturnsError()
    {
        var r = PositionParser.Parse("");
        Assert.Null(r.Position);
        Assert.Contains("空", r.Error);
    }

    [Fact]
    public void Parse_MissingComponent_ReturnsError()
    {
        var r = PositionParser.Parse("E 100mm N 200mm");
        Assert.Null(r.Position);
        Assert.Contains("不足", r.Error);
    }

    [Fact]
    public void Parse_DuplicateDirection_ReturnsError()
    {
        var r = PositionParser.Parse("E 100mm E 200mm U 300mm");
        Assert.Null(r.Position);
        Assert.Contains("重复", r.Error);
    }

    [Fact]
    public void Parse_Garbage_ReturnsError()
    {
        var r = PositionParser.Parse("hello world");
        Assert.Null(r.Position);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }
}
