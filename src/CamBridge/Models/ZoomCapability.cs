namespace CamBridge.Models;

public sealed record ZoomCapability(double Min, double Max, double Step)
{
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}
