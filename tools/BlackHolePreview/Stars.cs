using System;
using System.Numerics;
using System.Threading.Tasks;

/// <summary>
/// Preview only: stars as the planet packs' own star bodies show them (KSPVE draws its objects round them, not the stars),
/// so a render of a system can have its star. Limb darkening, granulation, starspots, a giant's huge convection cells, a
/// brown dwarf's cloud bands, a white dwarf's glare, or a dark star's blue rim; and a corona round it.
///   star &lt;style&gt; &lt;kelvin&gt; out.png width height [size] [time]
/// style: sun, reddwarf, giant, browndwarf, whitedwarf, dark. Size is the star's radius as a share of the frame's height.
/// </summary>
internal static partial class Program
{
    const int StarSun = 0, StarRedDwarf = 1, StarGiant = 2, StarBrownDwarf = 3, StarWhiteDwarf = 4, StarDark = 5;

    static int StyleOf(string name) => name.ToLowerInvariant() switch
    {
        "sun" => StarSun, "reddwarf" => StarRedDwarf, "giant" => StarGiant, "browndwarf" => StarBrownDwarf,
        "whitedwarf" => StarWhiteDwarf, "dark" => StarDark, _ => throw new ArgumentException(name)
    };

    /// <summary>A star's colour as the eye sees it on screen: its blackbody colour, a little deeper (STAR_SAT).</summary>
    static Vector3 StarColor(float kelvin)
    {
        Vector3 c = Saturate(Blackbody(kelvin), EnvFloat("STAR_SAT", 1.5f));
        return c / MathF.Max(0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z, 1e-4f);
    }

    /// <summary>A star's surface at unit normal n seen along view, in linear light (1 = its mean brightness).</summary>
    static Vector3 StarSurface(Vector3 n, Vector3 view, float kelvin, int style, float time)
    {
        float mu = MathF.Max(Vector3.Dot(n, -view), 0f);
        float edge = 1f - mu;
        Vector3 c = StarColor(kelvin);
        switch (style)
        {
            case StarDark:
            {
                // Light only at its edge, inverted: black within, a deep blue rim where its "light" leaks round it.
                float rim = MathF.Pow(edge, 3f);
                float swirl = Fbm(n * 3f + new Vector3(time * 0.02f));
                return new Vector3(0.05f, 0.12f, 0.5f) * (rim * (0.6f + 0.8f * swirl)) + new Vector3(0.004f, 0.006f, 0.02f) * swirl;
            }
            case StarBrownDwarf:
            {
                // Cloud bands of a failed star, deep red and dim, turbulent between them.
                float lat = n.Y;
                float bands = Fbm(new Vector3(lat * 9f, 0.6f * Fbm(n * 4f), 2.2f) + new Vector3(0f, time * 0.003f, 0f));
                float storms = SmoothStep(0.62f, 0.8f, Fbm(n * 7f + new Vector3(5.5f)));
                float limb = 1f - 0.75f * edge;
                Vector3 deep = new Vector3(0.45f, 0.07f, 0.06f), warm = new Vector3(1f, 0.32f, 0.12f);
                return Vector3.Lerp(deep, warm, Math.Clamp(bands * 1.4f - 0.2f + 0.4f * storms, 0f, 1f)) * (0.55f * limb);
            }
            case StarWhiteDwarf:
                return c * (2.2f * (1f - 0.35f * edge));
            default:
            {
                // Limb darkening (quadratic) and granulation; a giant's cells are huge, a red dwarf has spots.
                float limb = 1f - 0.6f * edge - 0.2f * edge * edge;
                float scale = style == StarGiant ? 4.5f : style == StarRedDwarf ? 14f : 22f;
                float contrast = style == StarGiant ? 0.45f : 0.16f;
                float cells = Fbm(n * scale + new Vector3(time * 0.01f, 0f, 0f));
                float granules = 1f - contrast + 2f * contrast * cells;
                float spots = 1f;
                if (style == StarRedDwarf || style == StarSun)
                {
                    float s = Fbm(n * 3.2f + new Vector3(8.8f));
                    float amount = style == StarRedDwarf ? 1f : 0.35f;
                    spots = 1f - amount * 0.75f * SmoothStep(0.66f, 0.74f, s);
                    // Bright faculae round the spots, near the limb.
                    spots += amount * 0.25f * SmoothStep(0.58f, 0.66f, s) * (1f - SmoothStep(0.66f, 0.72f, s)) * edge;
                }
                Vector3 hot = Vector3.Lerp(c, StarColor(kelvin * 0.8f), edge * 0.6f);   // the limb is cooler, redder
                return hot * (limb * granules * spots);
            }
        }
    }

    /// <summary>The corona and glow round a star, d star radii from its centre (d &gt; 1), seen past its limb.</summary>
    static Vector3 StarCorona(float d, Vector3 dirAround, float kelvin, int style, float time)
    {
        float h = d - 1f;
        switch (style)
        {
            case StarDark:
                return new Vector3(0.03f, 0.07f, 0.35f) * (0.8f * MathF.Exp(-h / 0.08f) + 0.05f * MathF.Exp(-h / 0.6f));
            case StarBrownDwarf:
                return new Vector3(0.6f, 0.12f, 0.1f) * (0.12f * MathF.Exp(-h / 0.06f));
            case StarWhiteDwarf:
                return StarColor(kelvin) * (1.4f * MathF.Exp(-h / 0.4f) + 0.5f / (1f + h * h * 0.5f) * 0.3f);
            default:
            {
                // Streamers round the limb, and a soft glow.
                float angle = MathF.Atan2(dirAround.Y, dirAround.X);
                float streamers = 0.6f + 0.8f * Noise3(new Vector3(MathF.Cos(angle) * 4f, MathF.Sin(angle) * 4f, time * 0.02f));
                float reach = style == StarGiant ? 0.35f : 0.18f;
                return StarColor(kelvin) * (0.45f * MathF.Exp(-h / (reach * 0.4f)) + 0.2f * streamers * MathF.Exp(-h / reach) + 0.012f * MathF.Exp(-h / (reach * 4f)));
            }
        }
    }

    static int RenderStar(string[] args)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        int style = StyleOf(args[1]);
        float kelvin = float.Parse(args[2], ci);
        string path = args[3];
        int w = int.Parse(args[4]), h = int.Parse(args[5]);
        float size = args.Length > 6 ? float.Parse(args[6], ci) : 0.32f;
        float time = args.Length > 7 ? float.Parse(args[7], ci) : 0f;
        float exposure = EnvFloat("STAR_EXPOSURE", style == StarWhiteDwarf || style == StarDark || style == StarBrownDwarf ? 1.6f : 0.8f);
        bool sky = Environment.GetEnvironmentVariable("OBJ_SKY") == "1";
        // A camera looking at the star from 3 radii / size, tilted a little so its poles and bands show.
        float tanY = MathF.Tan(30f * MathF.PI / 180f);
        float distance = 1f / (size * tanY);
        Vector3 camPos = new Vector3(0f, MathF.Sin(0.25f), -MathF.Cos(0.25f)) * distance;
        Vector3 forward = Vector3.Normalize(-camPos);
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        Vector3 up = Vector3.Cross(forward, right);
        float tanX = tanY * w / h;
        var rgb = new byte[w * h * 3];
        Parallel.For(0, h, py =>
        {
            for (int px = 0; px < w; px++)
            {
                float u = (px + 0.5f) / w, vv = 1f - (py + 0.5f) / h;
                Vector3 dir = Vector3.Normalize(forward + right * (u * 2 - 1) * tanX + up * (vv * 2 - 1) * tanY);
                float b = Vector3.Dot(camPos, dir), c = Vector3.Dot(camPos, camPos) - 1f, disc = b * b - c;
                Vector3 color;
                if (disc > 0f && -b - MathF.Sqrt(disc) > 0f)
                {
                    Vector3 n = Vector3.Normalize(camPos + dir * (-b - MathF.Sqrt(disc)));
                    color = StarSurface(n, dir, kelvin, style, time);
                }
                else
                {
                    // Closest approach to the centre: the corona there.
                    Vector3 closest = camPos - dir * b;
                    float d = closest.Length();
                    color = StarCorona(MathF.Max(d, 1f), new Vector3(Vector3.Dot(closest, right), Vector3.Dot(closest, up), 0f), kelvin, style, time);
                    if (sky) color += StarSky(dir) / exposure;
                }
                Vector3 outc = Tonemap(color * exposure);
                int i = (py * w + px) * 3;
                rgb[i] = ToByte(LinearToSrgb(outc.X)); rgb[i + 1] = ToByte(LinearToSrgb(outc.Y)); rgb[i + 2] = ToByte(LinearToSrgb(outc.Z));
            }
        });
        WritePng(path, w, h, rgb);
        Console.WriteLine($"wrote {path}: {args[1]} star, {kelvin} K");
        return 0;
    }
}
