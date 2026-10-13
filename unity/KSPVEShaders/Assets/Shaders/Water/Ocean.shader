Shader "Hidden/KSPVE/Water/Ocean"
{
    // KSPVE's sea. One shader for two meshes, told apart by _KSPVE_Grid:
    //  1 = the finely displaced polar mesh round the camera (OceanGrid): choppy Tessendorf waves from the FFT cascades,
    //  0 = KSP's own ocean quads (their material is swapped for this one): flat, with the same waves' normals and foam, and
    //      clipped inside the near mesh so the two do not overlap.
    // Shading (Water/KSPVEWater.cginc): Fresnel reflection of the sky, sun glitter with Cox and Munk's roughness for the waves too
    // small to resolve, the colour of the water from its absorption and scattering, the sea floor seen through it (refracted),
    // whitecaps and shoreline foam from the scene's depth, waves growing and breaking in shallow water.
    Properties
    {
        _KSPVE_Grid ("Near mesh (1) or KSP quads (0)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-100" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        GrabPass { "_KSPVEWaterGrab" }
        Pass
        {
            Name "OCEAN"
            Tags { "LightMode" = "Always" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            CGPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #include "KSPVEWater.cginc"

            #define KSPVE_GRID_RINGS 288.0   // OceanSystem.BuildGrid
            sampler2D _KSPVEWaterGrab;
            float _KSPVE_Grid;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;     // displaced
                float3 flat : TEXCOORD1;      // on the mean sea level
                float4 screen : TEXCOORD2;
                float shoal : TEXCOORD3;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 flat;
                float spacing = 1e6;
                float rim = 1.0;
                if (_KSPVE_Grid > 0.5)
                {
                    // Polar mesh: v.uv.x is 0 at the centre and 1 at the rim (log spaced), v.uv.y the angle in turns.
                    float rMax = max(_KSPVE_GridCenter.w, 1.0);
                    float r = 0.25 * pow(rMax / 0.25, v.uv.x);
                    float a = v.uv.y * 6.2831853;
                    flat = _KSPVE_GridCenter.xyz + (_KSPVE_GridE.xyz * cos(a) + _KSPVE_GridN.xyz * sin(a)) * r
                           - _KSPVE_GridUp.xyz * (r * r / (2.0 * _KSPVE_GridUp.w));
                    // The rings are KSPVE_GRID_RINGS apart in log r: a ring's spacing is r times (ratio - 1); the spokes are closer.
                    spacing = (pow(rMax / 0.25, 1.0 / KSPVE_GRID_RINGS) - 1.0) * r;
                    rim = 1.0 - smoothstep(0.55 * rMax, rMax, r);
                }
                else
                {
                    flat = mul(unity_ObjectToWorld, v.vertex).xyz;
                }
                float3 up = KSPVEOceanUp(flat);
                float3 world = flat;
                float shoal = 1.0;
                if (_KSPVE_Grid > 0.5)
                {
                    // The depth below this vertex (from the scene's depth), for waves shoaling and breaking.
                    float4 sp = ComputeScreenPos(mul(UNITY_MATRIX_VP, float4(flat, 1.0)));
                    float4 behind = KSPVESceneBehind(sp, flat);
                    float depth = behind.w > 0.5 ? max(dot(flat - behind.xyz, up), 0.0) : 1000.0;
                    shoal = KSPVEWaveScale(depth);
                    float3 d = KSPVEDisplacement(flat, spacing) * shoal;
                    // Fade to the mean level at the rim, where KSP's own quads take over.
                    d *= rim;
                    float3 e = _KSPVE_OceanE.xyz, n = _KSPVE_OceanN.xyz;
                    world = flat + e * d.x + n * d.z + up * d.y;
                }
                o.world = world;
                o.flat = flat;
                o.shoal = shoal;
                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float4 frag(v2f i, float facing : VFACE) : SV_Target
            {
                float3 up = KSPVEOceanUp(i.flat);
                if (_KSPVE_Grid < 0.5 && _KSPVE_GridCenter.w > 0.0)
                {
                    float3 delta = i.flat - _KSPVE_GridCenter.xyz;
                    delta -= _KSPVE_GridUp.xyz * dot(delta, _KSPVE_GridUp.xyz);
                    if (dot(delta, delta) < _KSPVE_GridCenter.w * _KSPVE_GridCenter.w * 0.994) discard;
                }
                float3 toEye = _WorldSpaceCameraPos - i.world;
                float dist = length(toEye);
                float3 V = toEye / max(dist, 1e-4);
                float3 sun = _KSPVE_SunDir.xyz;
                float refractiveIndex = _KSPVE_Deep.a;

                float4 behind = KSPVESceneBehind(i.screen, i.world);
                float depth = behind.w > 0.5 ? max(dot(i.world - behind.xyz, up), 0.0) : 1000.0;
                float waveScale = KSPVEWaveScale(depth);
                float footprint = max(length(ddx(i.world)), length(ddy(i.world)));
                KSPVESea sea = KSPVESampleSea(i.flat, up, footprint, waveScale);
                float3 n = sea.normal;

                bool below = facing < 0.0 || dot(V, up) < 0.0;
                if (below) n = -n;
                float cosV = max(dot(n, V), 0.02);
                float fresnel = KSPVEFresnel(cosV, refractiveIndex);
                float sunUp = max(dot(sun, up), 0.02);
                float sunFresnel = KSPVEFresnel(sunUp, refractiveIndex);
                float3 irradiance = _KSPVE_SunRadiance.rgb * sunUp * (1.0 - sunFresnel) + _KSPVE_SkyAmbient.rgb;
                float n2 = refractiveIndex * refractiveIndex;

                float3 body, transmit;
                KSPVEWaterColumn(depth, sunUp, cosV, irradiance, refractiveIndex, body, transmit);
                // Troughs are a little darker, crests lighter.
                body *= max(1.0 + 0.25 * sea.height, 0.5);

                // What is under the water, refracted: the lookup moves with the surface's tilt, but never to something in front of the water.
                float2 uv = i.screen.xy / i.screen.w;
                float3 bottom = tex2Dlod(_KSPVEWaterGrab, float4(uv, 0, 0)).rgb;
                if (_KSPVE_SeaLook.z > 0.5 && depth < 900.0)
                {
                    float3 nView = mul((float3x3)UNITY_MATRIX_V, n);
                    float2 shift = nView.xy * 0.06 * saturate(depth / 6.0) / max(dist * 0.02, 1.0);
                    float2 uv2 = uv + shift;
                    float4 sp2 = float4(uv2 * i.screen.w, 0, i.screen.w);
                    float raw2 = SAMPLE_DEPTH_TEXTURE_LOD(_CameraDepthTexture, float4(uv2, 0, 0));
                    if (LinearEyeDepth(raw2) > i.screen.w - 0.05)
                        bottom = tex2Dlod(_KSPVEWaterGrab, float4(uv2, 0, 0)).rgb;
                }

                float3 col;
                float3 reflectDir = reflect(-V, n);
                if (below)
                {
                    // From underneath: Snell's window onto the sky, total internal reflection outside it.
                    float3 refracted = refract(-V, n, refractiveIndex);
                    float3 window = dot(refracted, refracted) > 0.0 ? KSPVESky(normalize(refracted), up, sun) * (1.0 - fresnel) : body;
                    col = lerp(body, window, saturate(dot(refracted, refracted) * 4.0));
                }
                else
                {
                    if (dot(reflectDir, up) < 0.01) reflectDir = normalize(float3(reflectDir) - up * (dot(reflectDir, up) - 0.01) * 0.7);
                    float3 skyRefl = KSPVESky(reflectDir, up, sun) * fresnel;
                    float3 glint = KSPVEGlitter(n, V, sun, sea.slopeVar * 0.5, refractiveIndex);
                    float refraction = (1.0 - fresnel) / n2;
                    // The bottom was lit as if in air; going down and up through the surface costs (1 - F) each and the radiance a factor 1/n^2.
                    float3 underwater = bottom * transmit * (1.0 - sunFresnel) * refraction + body * refraction;
                    col = skyRefl + glint + underwater * _KSPVE_SeaLook.w;

                    // Foam: whitecaps (the cascades' Jacobian kept fading), waves breaking as the water shoals, and the shore's edge.
                    if (_KSPVE_SeaLook.y > 0.0)
                    {
                        float2 fp = KSPVEWaveCoords(i.flat);
                        float breaking = saturate((1.8 * _KSPVE_Sea.z / max(_KSPVE_SeaLook.x * depth, 1e-3) - 0.5) / 0.5);
                        float crest = smoothstep(0.15 * _KSPVE_Sea.z, 0.45 * _KSPVE_Sea.z, sea.height);
                        float shore = (1.0 - smoothstep(0.0, 0.8, depth)) * (0.6 + 0.4 * sin(_Time.y * 1.3 + fp.x * 0.7 + fp.y * 0.4));
                        float mask = saturate(max(sea.foam * _KSPVE_SeaLook.y, max(breaking * crest, shore) * (depth < 900.0 ? 1.0 : 0.0)));
                        float foam = KSPVEFoamShape(mask, fp * 0.9) * mask;
                        float3 foamColor = 0.85 * (irradiance * (0.35 + 0.65 * saturate(dot(n, sun))));
                        col = lerp(col, foamColor, saturate(foam));
                    }
                    // Sea ice (the climate's SnowCover.SeaIceOf, when something supplies it): floes with ragged edges, calming the waves.
                    if (_KSPVE_SeaIce.y > 0.0)
                    {
                        float2 fp = KSPVEWaveCoords(i.flat);
                        float floe = smoothstep(_KSPVE_SeaIce.y - 0.12, _KSPVE_SeaIce.y + 0.12, 1.0 - KSPVEFbm(fp * 0.01));
                        float3 ice = 0.65 * (_KSPVE_SunRadiance.rgb * saturate(dot(up, sun)) + _KSPVE_SkyAmbient.rgb) * (0.85 + 0.15 * KSPVENoise(fp * 0.3));
                        col = lerp(col, ice, floe * saturate(_KSPVE_SeaIce.y * 4.0));
                    }
                }
                return float4(max(col, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
