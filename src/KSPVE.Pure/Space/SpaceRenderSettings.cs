using KSPVE.Pure.Config;

namespace KSPVE.Pure.Space;

public sealed class SpaceRenderSettings
{
    private static BoolSetting BlackHolesSetting;
    private static DoubleSetting BrightnessSetting;
    private static DoubleSetting ResolutionSetting;
    private static BoolSetting OrbitPathsSetting;
    public static readonly SettingsSchema Schema = BuildSchema();

    public bool BlackHoles = true;
    public double BlackHoleBrightness = 1.0;
    public double BlackHoleResolution = 0.5;
    public bool OrbitPaths;

    private static SettingsSchema BuildSchema()
    {
        var schema = new SettingsSchema("KSPVE_SPACE");
        BlackHolesSetting = schema.Add(new BoolSetting("blackHoles", true, "Draw configured space objects."));
        BrightnessSetting = schema.Add(new DoubleSetting("blackHoleBrightness", 1.0, 0.0, 20.0, "Brightness of space objects."));
        ResolutionSetting = schema.Add(new DoubleSetting("blackHoleResolution", 0.5, 0.05, 4.0, "Render scale for space objects."));
        OrbitPathsSetting = schema.Add(new BoolSetting("orbitPaths", false, "Draw orbit paths on the map."));
        return schema;
    }

    public void Apply(SettingsValues values)
    {
        BlackHoles = values.Get(BlackHolesSetting);
        BlackHoleBrightness = values.Get(BrightnessSetting);
        BlackHoleResolution = values.Get(ResolutionSetting);
        OrbitPaths = values.Get(OrbitPathsSetting);
    }

    public CfgNode ToCfg()
    {
        var node = new CfgNode(Schema.NodeName);
        node.Add("blackHoles", BlackHoles ? "True" : "False");
        node.Add("blackHoleBrightness", BlackHoleBrightness.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        node.Add("blackHoleResolution", BlackHoleResolution.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        node.Add("orbitPaths", OrbitPaths ? "True" : "False");
        return node;
    }
}
