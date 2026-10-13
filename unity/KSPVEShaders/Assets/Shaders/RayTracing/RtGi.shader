// KSPVE ray-traced global illumination (DirectX 11, world space). Pass 0 traces one cosine-weighted bounce ray from each
// low-resolution pixel's surface (G-buffer depth and normal) through the scene's BVHs (RtTrace.cginc); where it lands it
// gathers the sun (with a shadow ray through the same scene, jittered over the sun's real disc), the sky, glowing
// surfaces and, past the loaded scene, the planet itself; a ray that escapes sees the sky. Pass 7 keeps the trace
// resolution's depth and normal; passes 1 and 6 accumulate the light and its luminance moments over time (reprojected
// with the camera's motion vectors, rejected where depth or normal disagree); pass 4 turns those into the filter's input
// with a variance; pass 2 is one edge-aware a-trous step (depth, normal and variance-scaled luminance stopping), run
// several times; pass 3 upsamples the result (depth- and normal-aware) and adds it, times the surface's albedo and
// occlusion, to the lit scene. Pass 5 shows the light alone (debug). Every pass is a plain blit.
//
// Units follow the scene's own: a surface lit by the sun is albedo x sun colour x cos, the sky is what the reflection
// probe saw, so the bounce light is what the game would show if it could follow light around.
Shader "Hidden/KSPVE/RayTracing/Gi"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        #include "../Include/KSPVECommon.cginc"
        #include "RtTrace.cginc"
        #pragma target 5.0

        #define PI 3.14159265

        UNITY_DECLARE_TEX2DARRAY(_RtAtlas);
        samplerCUBE _RtSky;
        sampler2D _RtPlanetAlbedo;
        sampler2D_float _CameraDepthTexture;
        sampler2D _CameraGBufferTexture0;   // diffuse albedo, occlusion
        sampler2D _CameraGBufferTexture2;   // world normal * 0.5 + 0.5
        sampler2D_half _CameraMotionVectorsTexture;

        float4 _RtSunDir;      // xyz towards the sun (world), w: its angular radius (rad)
        float4 _RtSunColor;    // rgb: colour x intensity as the scene lights with it; a: shadow rays on
        float4 _RtSkyInfo;     // x: has a sky cube, y: mip for directions, z: intensity, w: mip for irradiance
        float4 _RtSkyColor;    // the sky's light when there is no cube
        float4 _RtPlanet;      // xyz: the body's centre (camera-relative), w: its surface radius (m)
        float4 _RtPlanetInfo;  // x: on, y: rays reach it only past this distance (m), z: map u offset, w: has a map
        float4 _RtPlanetX, _RtPlanetY, _RtPlanetZ;   // the body's axes: latitude 0 longitude 0, north, longitude 90 E
        float4 _RtRight, _RtUp, _RtForward;          // view: camera-relative world directions, forward at z = 1
        float4 _RtLowRes;      // xy: trace size, zw: 1 / size
        float4 _RtParams;      // x: history frames at most, y: a-trous step (texels), z: luminance stopping, w: strength
        float4 _RtParams2;     // x: history valid, y: flip the view vertically

        // ---------------------------------------------------------------- helpers

        float3 RtLinear(float3 c) { return _RtInfo.w > 0.5 ? GammaToLinearSpace(c) : c; }
        float3 Unpack(uint c) { return float3(c & 255u, (c >> 8) & 255u, (c >> 16) & 255u) / 255.0; }

        float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

        // Two well-spread random numbers for a pixel and frame (R2 sequence over frames, shifted per pixel).
        float2 Random2(float2 pixel, float frame, float salt)
        {
            float2 r2 = frac(float2(0.7548776662, 0.5698402910) * (frame + salt * 17.0));
            float2 shift = float2(Hash(pixel + salt), Hash(pixel.yx + 3.7 + salt));
            return frac(r2 + shift);
        }

        // A direction about n with probability cos(theta) / pi.
        float3 CosineSample(float3 n, float2 xi)
        {
            float phi = 2.0 * PI * xi.x;
            float r = sqrt(xi.y);
            float3 t = abs(n.y) < 0.999 ? normalize(cross(float3(0, 1, 0), n)) : float3(1, 0, 0);
            float3 b = cross(n, t);
            return normalize(t * (r * cos(phi)) + b * (r * sin(phi)) + n * sqrt(max(1.0 - xi.y, 0.0)));
        }

        // A direction within a cone of half-angle a about d (the sun's disc).
        float3 ConeSample(float3 d, float a, float2 xi)
        {
            float cosA = cos(a);
            float z = 1.0 - xi.x * (1.0 - cosA);
            float s = sqrt(max(1.0 - z * z, 0.0));
            float phi = 2.0 * PI * xi.y;
            float3 t = abs(d.y) < 0.999 ? normalize(cross(float3(0, 1, 0), d)) : float3(1, 0, 0);
            float3 b = cross(d, t);
            return normalize(t * (s * cos(phi)) + b * (s * sin(phi)) + d * z);
        }

        float3 Sky(float3 d, float mip)
        {
            if (_RtSkyInfo.x > 0.5) return texCUBElod(_RtSky, float4(d, mip)).rgb * _RtSkyInfo.z;
            return _RtSkyColor.rgb;
        }

        // Where o + t d meets a sphere first ahead of o (or -1).
        float SphereHit(float3 o, float3 d, float3 centre, float radius)
        {
            float3 oc = o - centre;
            float b = dot(oc, d), c = dot(oc, oc) - radius * radius, disc = b * b - c;
            if (disc < 0.0) return -1.0;
            float s = sqrt(disc);
            float t = -b - s;
            if (t <= 0.0) t = -b + s;
            return t > 0.0 ? t : -1.0;
        }

        // The planet's surface as seen from far: its colour map, lit by the sun and the sky.
        float3 PlanetLight(float3 p)
        {
            float3 n = normalize(p - _RtPlanet.xyz);
            float3 b = float3(dot(n, _RtPlanetX.xyz), dot(n, _RtPlanetY.xyz), dot(n, _RtPlanetZ.xyz));
            float2 uv = float2(frac(0.25 - atan2(b.z, b.x) / (2.0 * PI) + _RtPlanetInfo.z), 0.5 + asin(clamp(b.y, -1.0, 1.0)) / PI);   // as KSP's planet textures
            float3 albedo = _RtPlanetInfo.w > 0.5 ? RtLinear(tex2Dlod(_RtPlanetAlbedo, float4(uv, 0, 0)).rgb) : float3(0.3, 0.3, 0.3);
            return albedo * (_RtSunColor.rgb * saturate(dot(n, _RtSunDir.xyz)) + Sky(n, _RtSkyInfo.w));
        }

        // The surface a ray hit: its normal (facing the ray), albedo and emission.
        void Surface(RtHit hit, float3 d, out float3 n, out float3 albedo, out float3 emission)
        {
            RtInstance inst = _RtInstances[hit.instance];
            RtTri tri = _RtTriangles[inst.triOffset + hit.tri];
            RtTriData td = _RtTriangleData[inst.dataOffset + (uint)tri.original];
            float3 no = cross(tri.e1, tri.e2);
            n = normalize(inst.w0.xyz * no.x + inst.w1.xyz * no.y + inst.w2.xyz * no.z);
            if (dot(n, d) > 0.0) n = -n;
            float w = 1.0 - hit.u - hit.v;
            float2 uv = td.uv0 * w + td.uv1 * hit.u + td.uv2 * hit.v;
            RtMaterial m = _RtMaterials[inst.materialOffset + td.submesh];
            albedo = m.albedo.rgb;
            if (m.albedo.a >= 0.0) albedo *= UNITY_SAMPLE_TEX2DARRAY_LOD(_RtAtlas, float3(uv, m.albedo.a), 0).rgb;
            if (m.emission.a > 0.5) albedo *= RtLinear(Unpack(td.c0) * w + Unpack(td.c1) * hit.u + Unpack(td.c2) * hit.v);
            emission = m.emission.rgb;
        }

        // The light leaving a hit surface towards the ray: sun (if it sees the sun), sky, its own glow.
        float3 Shade(float3 p, float3 n, float3 albedo, float3 emission, float2 xi)
        {
            float3 light = emission + albedo * Sky(n, _RtSkyInfo.w);
            float cosSun = dot(n, _RtSunDir.xyz);
            if (cosSun > 0.0)
            {
                float visible = 1.0;
                if (_RtSunColor.a > 0.5)
                {
                    float3 toSun = ConeSample(_RtSunDir.xyz, _RtSunDir.w, xi);
                    float3 o = p + n * max(0.02, length(p) * 2e-4);
                    RtHit shadow;
                    if (RtTrace(o, toSun, _RtInfo.y, true, shadow)) visible = 0.0;
                    else if (_RtPlanetInfo.x > 0.5 && SphereHit(o, toSun, _RtPlanet.xyz, _RtPlanet.w) > _RtPlanetInfo.y) visible = 0.0;
                }
                light += albedo * _RtSunColor.rgb * (cosSun * visible);
            }
            return light;
        }

        // The camera-relative position and view ray of a screen position.
        float3 ViewRay(float2 uv)
        {
            float2 ndc = uv * 2.0 - 1.0;
            if (_RtParams2.y > 0.5) ndc.y = -ndc.y;
            return _RtForward.xyz + _RtRight.xyz * ndc.x + _RtUp.xyz * ndc.y;
        }

        float EyeDepth(float2 uv) { return LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv, 0, 0))); }
        bool IsSky(float eye) { return eye > _ProjectionParams.z * 0.99; }
        float3 Normal(float2 uv) { return normalize(tex2Dlod(_CameraGBufferTexture2, float4(uv, 0, 0)).xyz * 2.0 - 1.0); }
        float Lum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

        sampler2D _RtPrevG;

        // Where this pixel was last frame (camera motion vectors), and whether the history there is the same surface:
        // on screen, with a depth within 10% and a normal within ~30 degrees of now.
        bool HistoryValid(float2 uv, out float2 prev)
        {
            prev = uv - tex2D(_CameraMotionVectorsTexture, uv).xy;
            if (_RtParams2.x < 0.5 || any(prev < 0.0) || any(prev > 1.0)) return false;
            float4 g = tex2D(_RtPrevG, prev);
            float eye = EyeDepth(uv);
            return abs(g.x - eye) < 0.1 * eye && dot(g.yzw, Normal(uv)) > 0.85;
        }
        ENDCG

        // 0: trace one bounce per low-resolution pixel. Out: rgb incoming light, a: hit distance (negative: no surface).
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float eye = EyeDepth(i.uv);
                if (IsSky(eye)) return float4(0, 0, 0, -1);
                float3 view = ViewRay(i.uv);
                float3 p = view * eye;
                float3 n = Normal(i.uv);
                float2 pixel = i.uv * _RtLowRes.xy;
                float3 d = CosineSample(n, Random2(pixel, _RtInfo.z, 0.0));
                float3 o = p + n * max(0.02, eye * 2e-4);

                RtHit hit;
                float3 light;
                float distance;
                if (RtTrace(o, d, _RtInfo.y, false, hit))
                {
                    float3 hn, albedo, emission;
                    Surface(hit, d, hn, albedo, emission);
                    light = Shade(o + d * hit.t, hn, albedo, emission, Random2(pixel, _RtInfo.z, 1.0));
                    distance = hit.t;
                }
                else
                {
                    float t = _RtPlanetInfo.x > 0.5 ? SphereHit(o, d, _RtPlanet.xyz, _RtPlanet.w) : -1.0;
                    if (t > _RtPlanetInfo.y) { light = PlanetLight(o + d * t); distance = t; }
                    else { light = Sky(d, _RtSkyInfo.y); distance = _RtInfo.y; }
                }
                return float4(max(light, 0.0), distance);
            }
            ENDCG
        }

        // 1: temporal accumulation. Out: rgb accumulated light, a: frames in it.
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtCurrent, _RtHistory;

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float4 current = tex2D(_RtCurrent, i.uv);
                if (current.a < 0.0) return 0;
                float2 prev;
                bool valid = HistoryValid(i.uv, prev);
                float4 history = valid ? tex2D(_RtHistory, prev) : 0;
                float frames = min((valid ? history.a : 0.0) + 1.0, _RtParams.x);
                return float4(lerp(history.rgb, current.rgb, 1.0 / frames), frames);
            }
            ENDCG
        }

        // 2: one a-trous step (3x3 taps _RtParams.y texels apart), stopping at depth and normal edges and at luminance
        // differences large against the noise. In and out: rgb light, a: its variance.
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtFilterIn, _RtG;

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float4 c = tex2D(_RtFilterIn, i.uv);
                float4 gc = tex2D(_RtG, i.uv);
                if (gc.x <= 0.0 || IsSky(gc.x)) return c;
                float lc = Lum(c.rgb);
                float sigma = _RtParams.z * sqrt(max(c.a, 0.0)) + 1e-4;
                float4 sum = 0;
                float weights = 0;
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float2 uv = i.uv + float2(x, y) * _RtParams.y * _RtLowRes.zw;
                        float4 s = tex2Dlod(_RtFilterIn, float4(uv, 0, 0));
                        float4 g = tex2Dlod(_RtG, float4(uv, 0, 0));
                        float k = (x == 0 ? 0.5 : 0.25) * (y == 0 ? 0.5 : 0.25);
                        float wz = exp(-abs(g.x - gc.x) / (0.03 * gc.x * _RtParams.y + 1e-3));
                        float wn = pow(saturate(dot(g.yzw, gc.yzw)), 64.0);
                        float wl = exp(-abs(Lum(s.rgb) - lc) / sigma);
                        float w = k * wz * wn * wl;
                        sum += float4(s.rgb * w, s.a * w * w);
                        weights += w;
                    }
                return weights > 1e-6 ? float4(sum.rgb / weights, sum.a / (weights * weights)) : c;
            }
            ENDCG
        }

        // 3: composite: the filtered light upsampled to this pixel (bilinear, weighted by depth and normal agreement),
        // times the surface's albedo and occlusion, added to the lit scene.
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtFiltered, _RtG;

            float3 Upsampled(float2 uv, float eye, float3 n)
            {
                float2 base = uv * _RtLowRes.xy - 0.5;
                float2 f = frac(base);
                float2 corner = (floor(base) + 0.5) * _RtLowRes.zw;
                float3 sum = 0;
                float total = 0;
                [unroll] for (int y = 0; y <= 1; y++)
                    [unroll] for (int x = 0; x <= 1; x++)
                    {
                        float2 t = corner + float2(x, y) * _RtLowRes.zw;
                        float4 g = tex2Dlod(_RtG, float4(t, 0, 0));
                        float w = (x == 0 ? 1.0 - f.x : f.x) * (y == 0 ? 1.0 - f.y : f.y);
                        w *= exp(-abs(g.x - eye) / (0.05 * eye + 1e-3)) * pow(saturate(dot(g.yzw, n)), 8.0) + 1e-5;
                        sum += tex2Dlod(_RtFiltered, float4(t, 0, 0)).rgb * w;
                        total += w;
                    }
                return sum / total;
            }

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float eye = EyeDepth(i.uv);
                if (IsSky(eye)) return 0;
                float4 surface = tex2D(_CameraGBufferTexture0, i.uv);
                float3 gi = Upsampled(i.uv, eye, Normal(i.uv));
                return float4(gi * surface.rgb * surface.a * _RtParams.w, 0);
            }
            ENDCG
        }

        // 4: the filter's input: accumulated light with its variance (from the moments; while the history is short, a
        // generous guess so the first frames are smoothed more).
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtAccumulated, _RtMoments;

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float4 a = tex2D(_RtAccumulated, i.uv);
                float2 m = tex2D(_RtMoments, i.uv).xy;
                float variance = max(m.y - m.x * m.x, 0.0);
                if (a.a < 4.0) variance = max(variance, m.y * 4.0 / max(a.a, 1.0));
                return float4(a.rgb, variance);
            }
            ENDCG
        }

        // 5: debug: the bounce light alone, over everything.
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtFiltered;

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float eye = EyeDepth(i.uv);
                if (IsSky(eye)) return float4(0, 0, 0, 1);
                return float4(tex2D(_RtFiltered, i.uv).rgb * _RtParams.w, 1);
            }
            ENDCG
        }

        // 6: luminance moments, accumulated as pass 1 accumulates the light. Out: x mean luminance, y mean square.
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag
            sampler2D _RtCurrent, _RtHistory, _RtMomentsHistory;

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float4 current = tex2D(_RtCurrent, i.uv);
                if (current.a < 0.0) return 0;
                float2 prev;
                bool valid = HistoryValid(i.uv, prev);
                float frames = min((valid ? tex2D(_RtHistory, prev).a : 0.0) + 1.0, _RtParams.x);
                float2 moments = valid ? tex2D(_RtMomentsHistory, prev).xy : 0;
                float l = Lum(current.rgb);
                return float4(lerp(moments, float2(l, l * l), 1.0 / frames), 0, 0);
            }
            ENDCG
        }

        // 7: this frame's eye depth and normal at the trace's resolution (for the filters and next frame's check).
        Pass
        {
            CGPROGRAM
            #pragma vertex KSPVEVertBlit
            #pragma fragment Frag

            float4 Frag(KSPVEFullscreenV2F i) : SV_Target
            {
                float eye = EyeDepth(i.uv);
                return float4(IsSky(eye) ? 1e9 : eye, Normal(i.uv));
            }
            ENDCG
        }
    }
}
