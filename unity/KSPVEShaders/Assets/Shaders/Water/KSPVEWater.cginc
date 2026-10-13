#ifndef KSPVE_WATER_INCLUDED
#define KSPVE_WATER_INCLUDED
// Shared by the ocean, river/lake and orbital glint shaders. The maths (spectrum, Fresnel, absorption, Cox and Munk,
// shoaling, Monahan) is KSPVE.Pure.Water's; tools/BlackHolePreview (water-ocean) is a CPU copy of this shading.
//
// Units: the sun's light is _KSPVE_SunRadiance = E / pi per scene unit (what a white Lambert surface facing the sun reflects),
// the convention KSP's own lighting uses; the sky likewise. A reflectance R therefore leaves R * (E / pi).

#include "UnityCG.cginc"

#define KSPVE_PI 3.14159265

// ---- the light
float4 _KSPVE_SunDir;           // xyz: towards the sun (world)
float4 _KSPVE_SunRadiance;      // rgb: sunlight at the surface (E / pi)
float4 _KSPVE_SkyZenith;        // rgb: sky radiance overhead
float4 _KSPVE_SkyHorizon;       // rgb: sky radiance at the horizon
float4 _KSPVE_SkyAmbient;       // rgb: the sky's irradiance on the water (/ pi)

// ---- the waves
UNITY_DECLARE_TEX2DARRAY(_KSPVE_Disp);    // (Dx, height, Dz, jacobian) of each cascade
UNITY_DECLARE_TEX2DARRAY(_KSPVE_Deriv);   // (slope x, slope z, foam, 0)
sampler2D _KSPVE_RoughLut;      // 64 x 1: slope variance per axis the cascades do not hold, by log2 of the pixel footprint
float4 _KSPVE_Lengths;          // side (m) of each cascade
float4 _KSPVE_MinWavelength;    // shortest wavelength (m) each cascade holds
float4 _KSPVE_OceanFrame;       // xy: the camera's wave coordinates (m, mod the largest cascade); z: number of cascades
float4 _KSPVE_OceanE;           // xyz: world axis of the waves' first coordinate
float4 _KSPVE_OceanN;           // xyz: world axis of the second
float4 _KSPVE_GridCenter;       // xyz: the ocean point under the camera (world); w: radius (m) of the finely displaced mesh (0 = none)
float4 _KSPVE_GridUp;           // xyz: local up there; w: the ocean's radius (m)
float4 _KSPVE_GridE;            // xyz: tangent axes at the grid centre
float4 _KSPVE_GridN;
float4 _KSPVE_Sea;              // x: wind (m/s); y: peak angular frequency (rad/s); z: significant height (m); w: gravity (m/s2)
float4 _KSPVE_SeaLook;          // x: breaker index; y: foam strength; z: refraction on; w: brightness

// ---- the water
float4 _KSPVE_Deep;             // rgb: deep-water irradiance reflectance (Morel and Prieur); a: refractive index
float4 _KSPVE_Absorb;           // rgb: absorption a (1/m)
float4 _KSPVE_Backscatter;      // rgb: backscattering b_b (1/m)
float4 _KSPVE_SeaIce;           // x: 1 when a sea-ice map is bound; y: its strength
sampler2D _KSPVE_SeaIceMap;     // equirectangular coverage 0..1 (hook for the climate's SnowCover.SeaIceOf)

sampler2D_float _CameraDepthTexture;

float KSPVEHash(float2 p)
{
    p = frac(p * float2(0.1031, 0.1030));
    p += dot(p, p.yx + 33.33);
    return frac((p.x + p.y) * p.x);
}

float KSPVENoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(KSPVEHash(i), KSPVEHash(i + float2(1, 0)), f.x), lerp(KSPVEHash(i + float2(0, 1)), KSPVEHash(i + 1.0), f.x), f.y);
}

float KSPVEFbm(float2 p)
{
    float a = 0.5, s = 0.0;
    for (int i = 0; i < 4; i++) { s += a * KSPVENoise(p); p = p * 2.03 + 11.7; a *= 0.5; }
    return s / 0.9375;
}

// ---- optics

// The exact Fresnel equations for an unpolarised ray into a medium of index n.
float KSPVEFresnel(float cosI, float n)
{
    cosI = saturate(cosI);
    float sin2t = (1.0 - cosI * cosI) / (n * n);
    float ct = sqrt(max(1.0 - sin2t, 0.0));
    float rs = (cosI - n * ct) / (cosI + n * ct);
    float rp = (n * cosI - ct) / (n * cosI + ct);
    return 0.5 * (rs * rs + rp * rp);
}

// The sky seen along a direction; up is the local vertical. No sun disc (the glitter is the sun's reflection).
float3 KSPVESky(float3 dir, float3 up, float3 sun)
{
    float y = saturate(dot(dir, up));
    float3 c = lerp(_KSPVE_SkyHorizon.rgb, _KSPVE_SkyZenith.rgb, pow(y, 0.55));
    float cs = saturate(dot(dir, sun));
    c += _KSPVE_SunRadiance.rgb * (0.04 * pow(cs, 8.0) + 0.12 * pow(cs, 90.0));
    return c;
}

// Linear wave theory's group velocity ratio and wavenumber (Hunt's approximation of the dispersion relation).
float KSPVEShoalFactor(float depth, float omega, float g)
{
    float y = omega * omega * depth / g;
    float kh = sqrt(y * y + y / (1.0 + 0.6522 * y + 0.4622 * y * y + 0.0864 * y * y * y * y + 0.0675 * y * y * y * y * y));
    float k = kh / max(depth, 1e-3);
    float n = 0.5 * (1.0 + 2.0 * kh / max(sinh(2.0 * kh), 1e-4));
    float cg = n * omega / k;
    float cgDeep = 0.5 * g / omega;
    return sqrt(cgDeep / max(cg, 1e-3));
}

// How much the waves are scaled at a depth: grow by shoaling, then break at the breaker index and vanish at the shore.
float KSPVEWaveScale(float depth)
{
    float ks = KSPVEShoalFactor(max(depth, 0.02), _KSPVE_Sea.y, _KSPVE_Sea.w);
    float limit = _KSPVE_SeaLook.x * max(depth, 0.0) / max(1.8 * _KSPVE_Sea.z, 1e-3);
    return saturate(min(ks, limit) * 1.0);
}

// ---- the scene

// The world position of the scene behind a pixel and its eye depth; w is 0 where nothing is there (the sky).
float4 KSPVESceneBehind(float4 screenPos, float3 worldPos)
{
    float2 uv = screenPos.xy / screenPos.w;
    float raw = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0));
    float eye = LinearEyeDepth(raw);
    float3 ray = worldPos - _WorldSpaceCameraPos;
    float3 dir = normalize(ray);
    float3 forward = -UNITY_MATRIX_V[2].xyz;
    float3 p = _WorldSpaceCameraPos + dir * (eye / max(dot(dir, forward), 0.05));
#if defined(UNITY_REVERSED_Z)
    bool sky = raw <= 1e-6;
#else
    bool sky = raw >= 1.0 - 1e-6;
#endif
    return float4(p, sky ? 0.0 : 1.0);
}

// ---- the sea's waves at a world position

float2 KSPVEWaveCoords(float3 world)
{
    float3 d = world - _WorldSpaceCameraPos;
    return float2(dot(d, _KSPVE_OceanE.xyz), dot(d, _KSPVE_OceanN.xyz)) + _KSPVE_OceanFrame.xy;
}

// The local up at a point of the ocean (the sphere is large: the nadir's up and the offset from it).
float3 KSPVEOceanUp(float3 world)
{
    return normalize(_KSPVE_GridUp.xyz * _KSPVE_GridUp.w + (world - _KSPVE_GridCenter.xyz));
}

// Displacement (east, up, north in the waves' axes), summed over the cascades each fine enough for a vertex spacing.
float3 KSPVEDisplacement(float3 world, float spacing)
{
    float2 uv = KSPVEWaveCoords(world);
    float3 d = 0;
    int count = (int)_KSPVE_OceanFrame.z;
    for (int c = 0; c < count; c++)
    {
        float wl = _KSPVE_MinWavelength[c];
        float fade = 1.0 - smoothstep(0.5 * wl, 1.2 * wl, spacing);
        if (fade <= 0.0) continue;
        float4 s = UNITY_SAMPLE_TEX2DARRAY_LOD(_KSPVE_Disp, float3(uv / _KSPVE_Lengths[c], c), 0);
        d += fade * s.xyz;
    }
    return d;
}

struct KSPVESea
{
    float3 normal;      // world
    float slopeVar;     // slope variance per axis the waves do not resolve (Cox and Munk)
    float height;       // summed height (m)
    float jacobian;     // of the summed choppy displacement
    float foam;         // the persistent foam of the whitecaps
};

KSPVESea KSPVESampleSea(float3 world, float3 up, float footprint, float waveScale)
{
    KSPVESea o;
    float2 uv = KSPVEWaveCoords(world);
    float2 slope = 0;
    float h = 0, jacobian = 1.0, foam = 0;
    int count = (int)_KSPVE_OceanFrame.z;
    for (int c = 0; c < count; c++)
    {
        float3 coord = float3(uv / _KSPVE_Lengths[c], c);
        float4 d = UNITY_SAMPLE_TEX2DARRAY(_KSPVE_Disp, coord);
        float4 s = UNITY_SAMPLE_TEX2DARRAY(_KSPVE_Deriv, coord);
        slope += s.xy;
        h += d.y;
        // Each cascade's Jacobian is 1 + lambda (Dxx + Dzz) + ...: add up the departures from one.
        jacobian += d.w - 1.0;
        foam = max(foam, s.z);
    }
    slope *= waveScale;
    // Tangent axes at this point: along the waves' first and second coordinates.
    float3 e = normalize(_KSPVE_OceanE.xyz - up * dot(_KSPVE_OceanE.xyz, up));
    float3 f = normalize(_KSPVE_OceanN.xyz - up * dot(_KSPVE_OceanN.xyz, up));
    o.normal = normalize(up - slope.x * e - slope.y * f);
    float lut = saturate(log2(max(footprint, 0.01) / 0.01) / 16.0);
    o.slopeVar = tex2Dlod(_KSPVE_RoughLut, float4(lut, 0.5, 0, 0)).r;
    o.height = h * waveScale;
    o.jacobian = jacobian;
    o.foam = foam * waveScale;
    return o;
}

// ---- the colour of water

struct KSPVEWaterOut
{
    float3 color;       // radiance towards the eye, with the surface reflections
    float3 transmit;    // what is left of the light from below (multiply the view of the bottom by it)
};

// Sun glitter: a Gaussian distribution of facet slopes (variance slopeVar per axis beyond the resolved normal) and the
// Fresnel reflectance of the facet; E_sun = pi * _KSPVE_SunRadiance.
float3 KSPVEGlitter(float3 n, float3 v, float3 sun, float slopeVar, float refractiveIndex)
{
    float3 h = normalize(sun + v);
    float nh = max(dot(n, h), 1e-3);
    float tan2 = (1.0 - nh * nh) / (nh * nh);
    float sigma2 = max(slopeVar, 2e-5);
    float d = exp(-tan2 / (2.0 * sigma2)) / (2.0 * KSPVE_PI * sigma2 * nh * nh * nh * nh);
    float cosV = max(dot(n, v), 0.02);
    float cosL = dot(n, sun);
    float fh = KSPVEFresnel(saturate(dot(sun, h)), refractiveIndex);
    return _KSPVE_SunRadiance.rgb * KSPVE_PI * fh * d / (4.0 * cosV) * step(0.0, cosL);
}

// Light coming out of a column of water of a vertical depth: the colour of the water itself (deep-water reflectance times the
// light that gets into it) and how much of the bottom's light survives the trip (Maritorena, Morel and Gentili 1994).
// sunUp is the sun's cosine from the vertical; viewCos the cosine of the view from the water's normal.
void KSPVEWaterColumn(float depth, float sunUp, float viewCos, float3 irradianceIn, float n, out float3 body, out float3 transmit)
{
    float cosSunW = sqrt(max(1.0 - (1.0 - sunUp * sunUp) / (n * n), 0.0));
    float cosViewW = sqrt(max(1.0 - (1.0 - viewCos * viewCos) / (n * n), 0.0));
    float3 k = (_KSPVE_Absorb.rgb + _KSPVE_Backscatter.rgb) * (1.0 / max(cosSunW, 0.05) + 1.04 / max(cosViewW, 0.05));
    transmit = exp(-k * max(depth, 0.0));
    body = _KSPVE_Deep.rgb * irradianceIn * (1.0 - transmit);
}

// Foam that follows a mask in 0..1: ragged edges from noise, a lacy interior.
float KSPVEFoamShape(float mask, float2 p)
{
    float n = KSPVEFbm(p) * 0.65 + KSPVEFbm(p * 3.7 + 5.0) * 0.35;
    return saturate((mask * 1.3 + (n - 0.5) - 0.35) * 4.0);
}

#endif
