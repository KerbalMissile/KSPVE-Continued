using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KSPVE.Pure.Clouds;
using KSPVE.Pure.Config;

/// <summary>
/// eve-info file...: reads EVE cloud configs (cfg files, or ModuleManager.ConfigCache, whose UrlConfig wrappers are
/// opened) as KSPVE would, and lists the layers by body and every issue found.
/// </summary>
internal static class EveInfo
{
    public static int Run(string[] args)
    {
        var roots = new List<CfgNode>();
        foreach (string path in args.Skip(1))
            foreach (CfgNode node in CfgParser.Parse(File.ReadAllText(path)))
            {
                if (node.Name == "UrlConfig") roots.AddRange(node.Children);
                else roots.Add(node);
            }
        EveCloudsSet set = EveCloudsReader.Read(roots);
        Console.WriteLine($"{set.Layers.Count} layers, {set.Layers.Count(l => l.Volume != null)} volumetric ({set.Layers.Count(l => l.Volume != null && l.Volume.OlderFormat)} in the older format)");
        foreach (IGrouping<string, CloudLayer> body in set.Layers.GroupBy(l => l.Body).OrderBy(g => g.Key))
            Console.WriteLine($"  {body.Key}: " + string.Join(", ", body.Select(l => l.Name + (l.Volume == null ? " (2D)" : l.Volume.OlderFormat ? " (older)" : "")
                + (l.Volume != null ? $" [{l.Volume.CloudTypes.Count} types, {l.Volume.MinAltitude:0}-{l.Volume.MaxAltitude:0} m]" : ""))));
        Console.WriteLine($"quality: {(set.Quality == null ? "none (defaults)" : $"x{set.Quality.TemporalUpscaling}, light volume {set.Quality.HorizontalResolution}x{set.Quality.VerticalResolution}")}");
        Console.WriteLine($"particle fields {set.ParticleFields.Count}, lightning {set.Lightning.Count}, droplets {set.Droplets.Count}, wet surfaces {set.WetSurfaces.Count}, shadows {set.Shadows.Count}, pqs {set.Pqs.Count}");
        Console.WriteLine($"{set.Issues.Count} issues:");
        foreach (ConfigIssue issue in set.Issues) Console.WriteLine("  " + issue);
        return 0;
    }
}
