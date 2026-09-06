namespace PdmsEquipmentLocator.Models;

/// <summary>
/// PDMS 世界坐标，单位 mm。
/// E=+X, W=-X, N=+Y, S=-Y, U=+Z, D=-Z
/// </summary>
public class Position3D
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public Position3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public override string ToString() => $"X={X} Y={Y} Z={Z}";
}
