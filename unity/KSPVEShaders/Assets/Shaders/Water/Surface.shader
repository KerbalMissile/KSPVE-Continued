Shader "Hidden/KSPVE/Water/Surface"
{
    // Rivers and lakes (KSPVE.Pure.Water.Hydrology meshes): ribbons that follow a river's bed down the terrain and flat sheets at a
    // lake's spill level. Not a copy of the ocean's shading: fresh water (tinted by dissolved organic matter), flow along the
    // channel from a flow map (two ripple layers sliding at the real speed, blended as each resets), rapids and waterfalls as
    // white water, wind ripples on lakes, shores from the scene's depth.
    // Mesh data: uv0 = (u across 0..1, v along in metres), uv1 = (depth m, speed m/s, rapids 0..1, fall 0..1),
    // uv2 = (flow east, flow north, width m); for a lake uv0 is its east/north position and the speed is 0.
    Properties
    {
        _KSPVE_Ripples ("Ripple normals", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-90" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        Pass
        {
            Name "SURFACE"
            Tags { "LightMode" = "Always" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend One SrcAlpha

            CGPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #include "KSPVEWater.cginc"

            sampler2D _KSPVE_Ripples;       // (normal x, normal y) * 0.5 + 0.5 in rg, tileable
            float4 _KSPVE_Fresh;            // rgb: deep reflectance of the fresh water; a: refractive index
            float4 _KSPVE_FreshAbsorb;      // rgb: absorption (1/m)
            float4 _KSPVE_FreshBackscatter; // rgb: backscattering (1/m)
            float4 _KSPVE_Flow;             // x: flow animation speed multiplier; y: foam on; z: lake wind (m/s); w: brightness
            float4 _KSPVE_LakeWind;         // xy: world direction the wind blows (east, north axes of the lake sheet); z, w: unused
            float4 _KSPVE_SurfaceUp;        // xyz: up (world), w: 1 once the mesh is placed

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 attr : TEXCOORD1;
                float3 flow : TEXCOORD2;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float4 attr : TEXCOORD2;
                float3 flow : TEXCOORD3;     // world flow direction, w: width in z of the original
                float4 screen : TEXCOORD4;
                float width : TEXCOORD5;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(o.world, 1.0));
                o.uv = v.uv;
                o.attr = v.attr;
                o.flow = mul((float3x3)unity_ObjectToWorld, float3(v.flow.x, 0.0, v.flow.y));
                o.width = v.flow.z;
                o.screen = ComputeScreenPos(o.pos);
                return o;
            }

            float3 Ripple(float2 p, float strength)
            {
                float2 n = tex2D(_KSPVE_Ripples, p).rg * 2.0 - 1.0;
                return float3(n * strength, 0.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 up = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                float3 toEye = _WorldSpaceCameraPos - i.world;
                float dist = length(toEye);
                float3 V = toEye / max(dist, 1e-4);
                float3 sun = _KSPVE_SunDir.xyz;
                float n1 = _KSPVE_Fresh.a;

                bool lake = i.attr.y <= 0.0 && dot(i.flow, i.flow) < 1e-6;
                float speed = i.attr.y * _KSPVE_Flow.x;
                float rapids = i.attr.z, fall = i.attr.w;

                // How deep the water is: a lake's from the scene (the real shore of the real terrain), a river's from its cross-section.
                float4 behind = KSPVESceneBehind(i.screen, i.world);
                float depth = i.attr.x;
                if (lake && behind.w > 0.5) depth = max(dot(i.world - behind.xyz, up), 0.0);
                float bank = lake ? 1.0 : smoothstep(0.0, 0.05, depth);

                // Surface normal: flow-map ripples in a channel, wind ripples on a lake.
                float3 n;
                if (lake)
                {
                    float2 wd = _KSPVE_LakeWind.xy;
                    float t = _Time.y * 0.35;
                    float2 pa = i.uv / 3.0 + wd * t * 0.5, pb = i.uv / 1.3 - wd * t * 0.8 + 0.37;
                    float ripple = saturate(_KSPVE_Flow.z / 6.0);
                    float2 s = (Ripple(pa, 0.25).xy + Ripple(pb, 0.2).xy * 0.7) * (0.04 + 0.5 * ripple);
                    float3 east = normalize(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0)));
                    float3 north = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 0, 1)));
                    n = normalize(up - s.x * east - s.y * north);
                }
                else
                {
                    float3 flowDir = dot(i.flow, i.flow) > 1e-8 ? normalize(i.flow - up * dot(i.flow, up)) : float3(1, 0, 0);
                    float3 side = cross(up, flowDir);
                    float across = (i.uv.x - 0.5) * i.width;
                    // Two layers sliding downstream, each fading out as it resets and the other fading in.
                    float phase = frac(_Time.y / 3.0);
                    float wA = 1.0 - abs(2.0 * phase - 1.0);
                    float2 pA = float2(across, i.uv.y - speed * phase * 3.0) / 1.6;
                    float phaseB = frac(phase + 0.5);
                    float2 pB = float2(across + 3.1, i.uv.y - speed * phaseB * 3.0 + 7.7) / 1.6;
                    float strength = 0.05 + 0.2 * saturate(speed / 3.0) + 0.5 * rapids;
                    float2 s = Ripple(pA, strength).xy * wA + Ripple(pB, strength).xy * (1.0 - wA);
                    n = normalize(up - s.x * side - s.y * flowDir);
                }
                float cosV = max(dot(n, V), 0.02);
                float fresnel = KSPVEFresnel(cosV, n1);
                float sunUp = max(dot(sun, up), 0.02);
                float sunFresnel = KSPVEFresnel(sunUp, n1);
                float3 irradiance = _KSPVE_SunRadiance.rgb * sunUp * (1.0 - sunFresnel) + _KSPVE_SkyAmbient.rgb;

                // The water's own colour and what the light from the bed has left.
                float cosSunW = sqrt(max(1.0 - (1.0 - sunUp * sunUp) / (n1 * n1), 0.0));
                float cosViewW = sqrt(max(1.0 - (1.0 - cosV * cosV) / (n1 * n1), 0.0));
                float3 k = (_KSPVE_FreshAbsorb.rgb + _KSPVE_FreshBackscatter.rgb) * (1.0 / max(cosSunW, 0.05) + 1.04 / max(cosViewW, 0.05));
                float3 transmit = exp(-k * depth);
                float3 body = _KSPVE_Fresh.rgb * irradiance * (1.0 - transmit);

                float3 reflectDir = reflect(-V, n);
                if (dot(reflectDir, up) < 0.01) reflectDir = normalize(reflectDir - up * (dot(reflectDir, up) - 0.01) * 0.7);
                float3 skyRefl = KSPVESky(reflectDir, up, sun) * fresnel;
                float slopeVar = lake ? 0.002 + 0.004 * saturate(_KSPVE_Flow.z / 8.0) : 0.002 + 0.02 * rapids;
                float3 glint = KSPVEGlitter(n, V, sun, slopeVar, n1);
                float refraction = (1.0 - fresnel) / (n1 * n1);
                float3 col = skyRefl + glint + body * refraction * _KSPVE_Flow.w;
                float opacity = saturate(dot(transmit, 1.0 / 3.0)) * (1.0 - fresnel);

                // White water: rapids stream along the flow, falls drop straight down in streaks.
                float foam = 0.0;
                if (_KSPVE_Flow.y > 0.5 && !lake)
                {
                    float across = (i.uv.x - 0.5) * i.width;
                    float2 fp = float2(across * 0.7, i.uv.y * 0.35 - _Time.y * speed * 0.35);
                    float streaks = KSPVEFbm(fp) * 0.7 + KSPVEFbm(float2(across * 2.3, i.uv.y * 0.9 - _Time.y * speed * 0.9)) * 0.3;
                    foam = saturate((rapids * 1.4 + streaks - 0.85) * 3.0) * bank;
                    float curtain = KSPVEFbm(float2(across * 1.4, i.uv.y * 0.22 - _Time.y * speed * 0.6));
                    foam = max(foam, fall * saturate(curtain * 2.2 + 0.2) * bank);
                }
                if (_KSPVE_Flow.y > 0.5)
                {
                    // The shore of a lake or the edge of a river's water: a thin line of foam where it is shallow.
                    float edge = (1.0 - smoothstep(0.0, lake ? 0.35 : 0.06, depth)) * (lake ? 0.5 : 0.35);
                    float2 sp = lake ? i.uv / 2.5 : float2((i.uv.x - 0.5) * i.width, i.uv.y) * 0.8;
                    foam = max(foam, edge * saturate(KSPVEFbm(sp + _Time.y * 0.15) * 1.6));
                }
                float3 foamColor = 0.85 * irradiance * (0.35 + 0.65 * saturate(dot(n, sun)));
                col = lerp(col, foamColor, saturate(foam));
                opacity *= 1.0 - saturate(foam);

                // Fade where there is no depth: the bank.
                col *= bank;
                opacity = lerp(1.0, opacity, bank);
                return float4(max(col, 0.0), opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
