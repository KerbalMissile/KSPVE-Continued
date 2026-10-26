namespace KSPVE.Pure.Space;

public readonly struct DiskState
{
    public readonly double Presence;
    public readonly double Inner, Outer;
    public readonly double Thickness;
    public readonly double ShapeInner, ShapeOuter;
    public readonly double MaskInner, MaskOuter, MaskSoftness;
    public readonly double Brightness;
    public readonly double Jets, JetLength;
    public readonly double Spiral;
    public readonly double JetHeads, LaunchFlash;

    public DiskState(double presence, double inner, double outer, double thickness, double brightness, double jets, double jetLength,
        double spiral = 0.0, double jetHeads = 0.0, double launchFlash = 0.0,
        double shapeInner = double.NaN, double shapeOuter = double.NaN, double maskInner = 0.0, double maskOuter = 0.0, double maskSoftness = 0.0)
    {
        Presence = presence;
        Inner = inner;
        Outer = outer;
        Thickness = thickness;
        ShapeInner = double.IsNaN(shapeInner) ? inner : shapeInner;
        ShapeOuter = double.IsNaN(shapeOuter) ? outer : shapeOuter;
        MaskInner = maskInner;
        MaskOuter = maskOuter;
        MaskSoftness = maskSoftness;
        Brightness = brightness;
        Jets = jets;
        JetLength = jetLength;
        Spiral = spiral;
        JetHeads = jetHeads;
        LaunchFlash = launchFlash;
    }

    public static DiskState Configured(double inner, double outer, double thickness, double jets, double jetLength) =>
        new DiskState(1.0, inner, outer, thickness, 1.0, jets, jetLength);
}
