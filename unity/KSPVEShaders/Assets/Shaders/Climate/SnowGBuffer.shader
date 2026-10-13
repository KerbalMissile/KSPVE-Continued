// Snow on the local terrain, written into the deferred G-buffer before the lighting pass (so KSP's lights, shadows and
// reflections treat it as the ground's own colour). Copies of the three G-buffers are read; the three are written whole.
// Where the snow goes: above the snow line (a map, now), not on slopes steeper than snow lies on, broken up by noise;
// glaciers are bluer and smoother. Craft are left alone: the terrain's own depth, rendered by a hidden camera that sees
// only the terrain layer, must agree with the scene's.
Shader "Hidden/KSPVE/Climate/SnowGBuffer"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertFullscreen
            #pragma fragment frag
            #include "../Include/KSPVECommon.cginc"

            sampler2D _KSPVE_SnowG0;               // copies of the G-buffers: rgb albedo a occlusion
            sampler2D _KSPVE_SnowG1;               // rgb specular a smoothness
            sampler2D _KSPVE_SnowG2;               // rgb world normal (0..1)
            sampler2D_float _KSPVE_SnowSceneDepth; // the camera's depth
            sampler2D_float _KSPVE_TerrainDepth;   // eye depth of the terrain alone
            sampler2D _KSPVE_SnowMaps;             // R snow cover, G perennial ice (coarse maps)
            sampler2D _KSPVE_SnowAux;              // R terrain height (m), G snow line (m)
            float4 _KSPVE_SnowRight;               // the camera's axes in world space; right and up scaled by the tangent of the half field of view
            float4 _KSPVE_SnowUp;
            float4 _KSPVE_SnowForward;
            float4 _KSPVE_SnowBody;                // xyz camera position from the body's centre (m), w the radius (m)
            float4 _KSPVE_SnowAxisX;               // the body's turning frame in world space: x at latitude 0 longitude 0
            float4 _KSPVE_SnowAxisY;               // north
            float4 _KSPVE_SnowAxisZ;               // longitude 90 east
            float4 _KSPVE_SnowLook;                // x snow albedo, y sparkle, z keep craft clear (0/1), w master strength
            float4 _KSPVE_SnowLook2;               // x ice brightness, y snow smoothness, z noise scale (m), w slope limit (cos)

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0)), n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1)), n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            struct Out
            {
                float4 g0 : SV_Target0;
                float4 g1 : SV_Target1;
                float4 g2 : SV_Target2;
            };

            Out frag(KSPVEFullscreenV2F i)
            {
                float4 g0 = tex2D(_KSPVE_SnowG0, i.uv);
                float4 g1 = tex2D(_KSPVE_SnowG1, i.uv);
                float4 g2 = tex2D(_KSPVE_SnowG2, i.uv);
                Out o;
                o.g0 = g0; o.g1 = g1; o.g2 = g2;

                float raw = SAMPLE_DEPTH_TEXTURE(_KSPVE_SnowSceneDepth, i.uv);
#if defined(UNITY_REVERSED_Z)
                if (raw <= 1e-7) return o;
#else
                if (raw >= 1.0 - 1e-7) return o;
#endif
                float eye = LinearEyeDepth(raw);
                if (_KSPVE_SnowLook.z > 0.5)
                {
                    float terrain = tex2D(_KSPVE_TerrainDepth, i.uv).r;
                    if (abs(terrain - eye) > 0.004 * eye + 0.06) return o;
                }

                float2 ndc = float2(i.uv.x * 2.0 - 1.0, i.uv.y * 2.0 - 1.0);
#if UNITY_UV_STARTS_AT_TOP
                ndc.y = -ndc.y;
#endif
                float3 offset = (_KSPVE_SnowForward.xyz + _KSPVE_SnowRight.xyz * ndc.x + _KSPVE_SnowUp.xyz * ndc.y) * eye;   // from the camera
                float4 world = float4(_WorldSpaceCameraPos + offset, 1.0);
                float3 p = offset + _KSPVE_SnowBody.xyz;   // from the body's centre
                float r = length(p);
                float3 up = p / r;
                float altitude = r - _KSPVE_SnowBody.w;
                float latitude = asin(clamp(dot(up, _KSPVE_SnowAxisY.xyz), -1.0, 1.0));
                float longitude = atan2(dot(up, _KSPVE_SnowAxisZ.xyz), dot(up, _KSPVE_SnowAxisX.xyz));
                float2 mapUv = float2(longitude / 6.28318531 + 0.5, latitude / 3.14159265 + 0.5);
                float4 maps = tex2Dlod(_KSPVE_SnowMaps, float4(mapUv, 0, 0));
                float2 aux = tex2Dlod(_KSPVE_SnowAux, float4(mapUv, 0, 0)).rg;

                // Snow lies above the snow line; right at the line it is patchy.
                float cover = smoothstep(aux.y - 150.0, aux.y + 250.0, altitude);
                float3 n = normalize(g2.rgb * 2.0 - 1.0);
                float slope = dot(n, up);
                float retention = smoothstep(_KSPVE_SnowLook2.w - 0.2, _KSPVE_SnowLook2.w + 0.2, slope);
                float3 q = p / _KSPVE_SnowLook2.z;
                float noise = 0.55 * ValueNoise3(q) + 0.3 * ValueNoise3(q * 4.1 + 7.0) + 0.15 * ValueNoise3(q * 17.3 + 3.0);
                float amount = saturate((cover * retention - 0.5) / 0.35 + 0.5 + (noise - 0.5) * 1.1) * _KSPVE_SnowLook.w;
                float glacier = saturate(maps.y) * smoothstep(0.0, 0.6, cover) * retention * _KSPVE_SnowLook.w;
                float w = max(amount, glacier);
                if (w < 0.002) return o;

                float3 snowColor = _KSPVE_SnowLook.x * float3(0.985, 0.99, 1.0);
                float3 iceColor = _KSPVE_SnowLook2.x * float3(0.62, 0.74, 0.82);
                float3 snowAlbedo = lerp(snowColor, iceColor, saturate(glacier / max(w, 1e-4)) * 0.8);
                float smoothness = lerp(_KSPVE_SnowLook2.y, 0.55, saturate(glacier / max(w, 1e-4)));

                // Display setting: glints, random facets a couple of centimetres across that catch the sun.
                float3 normal = n;
                if (_KSPVE_SnowLook.y > 0.0)
                {
                    float3 cell = floor(world.xyz * 40.0);
                    float h = Hash31(cell);
                    float glint = step(0.97, h) * w;
                    float3 dir = normalize(Hash33(cell) * 2.0 - 1.0);
                    normal = normalize(n + dir * glint * _KSPVE_SnowLook.y * 1.2);
                    smoothness = lerp(smoothness, 0.92, glint * saturate(_KSPVE_SnowLook.y));
                }

                o.g0 = float4(lerp(g0.rgb, snowAlbedo, w), lerp(g0.a, 1.0, 0.5 * w));
                o.g1 = float4(lerp(g1.rgb, float3(0.04, 0.04, 0.04), w), lerp(g1.a, smoothness, w));
                o.g2 = float4(lerp(n, normal, w) * 0.5 + 0.5, g2.a);
                return o;
            }
            ENDCG
        }
    }
}
