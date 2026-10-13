Shader "Hidden/KSPVE/Water/OceanGlint"
{
    // The ocean seen from space (Camera ScaledSpace): the sun's glint and the grazing reflection of the sky off a sphere at sea level,
    // only where the ocean is (a map of the body's sea, sampled from its terrain) and as rough as the wind there makes it
    // (Cox and Munk: slope variance 0.003 + 0.00512 U). Added to the planet's own picture.
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" }
        Pass
        {
            Name "GLINT"
            Tags { "LightMode" = "Always" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend One One

            CGPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            #include "KSPVEWater.cginc"

            sampler2D _KSPVE_OceanMask;      // equirectangular in the body's turning frame: r ocean (0 land, 1 sea), g wind speed / 40 m/s, b sea-ice cover
            float4 _KSPVE_BodyX;             // the body's turning frame as world axes (CloudWorld.Basis): x at latitude 0 longitude 0
            float4 _KSPVE_BodyY;             // north
            float4 _KSPVE_BodyZ;             // longitude 90 east
            float4 _KSPVE_GlintLook;         // x: strength; y: brightness of the sky's grazing reflection; z: atmosphere transmittance of the sun's path (0..1); w: view path

            struct appdata { float4 vertex : POSITION; };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 world : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                o.pos = UnityWorldToClipPos(o.world);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 V = normalize(_WorldSpaceCameraPos - i.world);
                float3 d = float3(dot(n, _KSPVE_BodyX.xyz), dot(n, _KSPVE_BodyY.xyz), dot(n, _KSPVE_BodyZ.xyz));
                float lat = asin(clamp(d.y, -1.0, 1.0)), lon = atan2(d.z, d.x);
                float2 uv = float2(lon / (2.0 * KSPVE_PI) + 0.5, lat / KSPVE_PI + 0.5);
                float3 mask = tex2D(_KSPVE_OceanMask, uv).rgb;
                if (mask.r <= 0.01) discard;
                float wind = mask.g * 40.0;
                float ice = mask.b;
                float3 sun = _KSPVE_SunDir.xyz;
                float n1 = _KSPVE_Deep.a;
                float cosV = saturate(dot(n, V));
                float fresnel = KSPVEFresnel(cosV, n1);
                // Cox and Munk's isotropic mean slope variance per axis.
                float slopeVar = 0.5 * (0.003 + 0.00512 * wind);
                float3 glint = KSPVEGlitter(n, V, sun, slopeVar, n1) * _KSPVE_GlintLook.x * _KSPVE_GlintLook.z;
                // The sky seen off the sea near the limb, where the Fresnel reflectance grows.
                float3 reflectDir = reflect(-V, n);
                float3 sky = KSPVESky(reflectDir, n, sun) * fresnel * _KSPVE_GlintLook.y;
                float3 col = (glint + sky) * mask.r * (1.0 - ice);
                return float4(max(col, 0.0), 0.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
