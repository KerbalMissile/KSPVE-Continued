#ifndef KSPVE_COLOR_INCLUDED
#define KSPVE_COLOR_INCLUDED

// Colour maths shared by KSPVE post-processing. Every constant mirrors the verified C# reference in
// src/KSPVE.Pure/ColorScience (Tonemappers.cs, Transfer.cs), which unit tests check against sources.

static const float3 KSPVE_LUMA = float3(0.2126, 0.7152, 0.0722);

float KSPVELuminance(float3 c) { return dot(c, KSPVE_LUMA); }

// KSP renders in Gamma colour space, so scene buffers hold sRGB-encoded values (HDR cameras may exceed 1).
// Exact IEC 61966-2-1 transfer, extended to values above 1 with the same power segment.
float3 KSPVESrgbToLinear(float3 c)
{
    c = max(c, 0);
    float3 lo = c / 12.92;
    float3 hi = pow((c + 0.055) / 1.055, 2.4);
    return lerp(hi, lo, step(c, 0.04045));
}

float3 KSPVELinearToSrgb(float3 c)
{
    c = max(c, 0);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return lerp(hi, lo, step(c, 0.0031308));
}

// ---------------------------------------------------------------------------------------------
// Tonemappers: linear scene-referred in, display-linear [0,1] out.

// Khronos PBR Neutral (KhronosGroup/ToneMapping PBR_Neutral/pbrNeutral.glsl).
float3 KSPVETonemapNeutral(float3 color)
{
    const float startCompression = 0.8 - 0.04;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;
    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression) return color;
    const float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / peak;
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return lerp(color, newPeak.xxx, g);
}

// Minimal AgX (iolite engine, after Troy Sobotka / Blender). Row-major matrices used with mul(M, v).
static const float3x3 KSPVE_AGX_IN = float3x3(
    0.842479062253094, 0.0784335999999992, 0.0792237451477643,
    0.0423282422610123, 0.878468636469772, 0.0791661274605434,
    0.0423756549057051, 0.0784336, 0.879142973793104);
static const float3x3 KSPVE_AGX_OUT = float3x3(
    1.19687900512017, -0.0980208811401368, -0.0990297440797205,
    -0.0528968517574562, 1.15190312990417, -0.0989611768448433,
    -0.0529716355144438, -0.0980434501171241, 1.15107367264116);

float3 KSPVEAgxContrast(float3 x)
{
    float3 x2 = x * x;
    float3 x4 = x2 * x2;
    return 15.5 * x4 * x2 - 40.14 * x4 * x + 31.96 * x4 - 6.868 * x2 * x + 0.4298 * x2 + 0.1191 * x - 0.00232;
}

float3 KSPVETonemapAgX(float3 color)
{
    const float minEv = -12.47393;
    const float maxEv = 4.026069;
    float3 v = mul(KSPVE_AGX_IN, color);
    v = clamp(log2(max(v, 1e-10)), minEv, maxEv);
    v = (v - minEv) / (maxEv - minEv);
    v = KSPVEAgxContrast(v);
    v = mul(KSPVE_AGX_OUT, v);
    return pow(saturate(v), 2.2);
}

// ACES fitted by Stephen Hill (TheRealMJP/BakingLab ACES.hlsl).
static const float3x3 KSPVE_ACES_IN = float3x3(
    0.59719, 0.35458, 0.04823,
    0.07600, 0.90834, 0.01566,
    0.02840, 0.13383, 0.83777);
static const float3x3 KSPVE_ACES_OUT = float3x3(
    1.60475, -0.53108, -0.07367,
    -0.10208, 1.10813, -0.00605,
    -0.00327, -0.07276, 1.07602);

float3 KSPVERrtAndOdtFit(float3 v)
{
    float3 a = v * (v + 0.0245786) - 0.000090537;
    float3 b = v * (0.983729 * v + 0.4329510) + 0.238081;
    return a / b;
}

float3 KSPVETonemapAces(float3 color)
{
    float3 v = mul(KSPVE_ACES_IN, color);
    v = KSPVERrtAndOdtFit(v);
    return saturate(mul(KSPVE_ACES_OUT, v));
}

// mode: 0 none (clamp), 1 Neutral, 2 AgX, 3 ACES
float3 KSPVETonemap(float3 color, float mode)
{
    color = max(color, 0);
    if (mode < 0.5) return saturate(color);
    if (mode < 1.5) return KSPVETonemapNeutral(color);
    if (mode < 2.5) return KSPVETonemapAgX(color);
    return KSPVETonemapAces(color);
}

// ---------------------------------------------------------------------------------------------
// Noise

// Interleaved gradient noise (Jimenez 2014), in [0,1).
float KSPVEInterleavedGradientNoise(float2 pixel, float frame)
{
    pixel += frame * 5.588238;
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

#endif
