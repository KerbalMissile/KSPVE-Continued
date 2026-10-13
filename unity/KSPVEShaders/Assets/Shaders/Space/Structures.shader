Shader "Hidden/KSPVE/Space/Structures"
{
    // Clouds of separate particles whose places come from orbital mechanics (KSPVE.Pure.Space.Particles): the tails of an
    // evaporating planet, an impact's debris, a synestia, a super-puff's haze, a moon torn into a ring, a rubble pile
    // breaking apart. One instanced billboard per particle, read from a structured buffer the CPU fills back to front
    // (so thick clouds hide what is behind them), blended "over" with premultiplied colour: thin gas only adds light.
    //
    // A particle is a disc (softness 0: a clump, a boulder) or a gaussian puff (softness 1: gas, haze). It is never drawn
    // smaller than _KSPVE_St_Look.y pixels; a smaller one is dimmed by the share of its true area that is left, so the
    // light is conserved. Its colour is the starlight it scatters (its reflectance times the star's colour times the
    // Henyey-Greenstein phase function at the scattering angle) plus its own glow, both from the CPU.
    CGINCLUDE
        #include "UnityCG.cginc"

        struct Particle
        {
            float3 pos;       // from the body's centre (scaled units), world axes
            float radius;     // scaled units
            float3 reflect;   // share of the starlight scattered, per colour (after self-shadowing)
            float soft;       // 0 disc ... 1 gaussian puff
            float3 emit;      // own glow, relative to a sunlit white surface
            float alpha;      // how much it hides what is behind it
        };

        StructuredBuffer<Particle> _KSPVE_St_Particles;
        float4 _KSPVE_St_Center;      // xyz: the body's centre relative to the camera (scaled units)
        float4 _KSPVE_St_Sun;         // xyz: unit vector from the body towards the star (world); w: Henyey-Greenstein g
        float4 _KSPVE_St_StarColor;   // rgb: the star's colour (luminance 1); a: overall brightness (brightness times visibility boost)
        float4 _KSPVE_St_Look;        // x: radians per pixel, y: smallest particle (pixels), z: count

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;       // -1..1 across the quad
            float4 color : TEXCOORD1;    // premultiplied radiance, alpha
            float soft : TEXCOORD2;
            float extent : TEXCOORD3;    // quad half-size over the particle's drawn radius
        };

        static const float2 Corners[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

        v2f vert(uint id : SV_VertexID)
        {
            v2f o;
            uint index = id / 6u;
            float2 corner = Corners[id % 6u];
            Particle p = _KSPVE_St_Particles[index];
            float3 rel = _KSPVE_St_Center.xyz + p.pos;            // from the camera, world axes
            float3 view = mul((float3x3)UNITY_MATRIX_V, rel);
            float z = max(-view.z, 1e-6);
            float angle = p.radius / z;
            float minAngle = _KSPVE_St_Look.y * 0.5 * _KSPVE_St_Look.x;
            float drawAngle = max(angle, minAngle);
            float energy = angle < minAngle ? (angle / minAngle) * (angle / minAngle) : 1.0;
            float extent = lerp(1.15, 2.3, p.soft);
            // Behind the camera, or nothing to draw: collapse the quad.
            bool hidden = view.z >= 0.0 || p.radius <= 0.0;
            float half = drawAngle * z * extent;
            view.xy += corner * half;

            // The starlight it scatters (Henyey-Greenstein, normalised to 1 for g = 0), and its own glow.
            float g = _KSPVE_St_Sun.w;
            float cosScatter = dot(_KSPVE_St_Sun.xyz, normalize(rel));
            float hg = (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosScatter, 1e-4), 1.5);
            float3 radiance = (p.reflect * _KSPVE_St_StarColor.rgb * hg + p.emit) * (_KSPVE_St_StarColor.a * energy);

            o.pos = hidden ? float4(2.0, 2.0, 2.0, 1.0) : mul(UNITY_MATRIX_P, float4(view, 1.0));
            o.uv = corner * extent;
            o.color = float4(radiance, min(p.alpha, 1.0) * 0.9 * energy);
            o.soft = p.soft;
            o.extent = extent;
            return o;
        }

        float4 frag(v2f i) : SV_Target
        {
            float q = length(i.uv);
            float hard = 1.0 - smoothstep(0.75, 1.0, q);
            float soft = exp(-2.5 * q * q);
            float profile = lerp(hard, soft, i.soft);
            // Premultiplied "over": rgb already carries the profile, alpha is the cover.
            return float4(i.color.rgb * profile, i.color.a * profile);
        }
    ENDCG

    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        // Pass 0: the particles, in the order the buffer holds them (far to near).
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
    }
    Fallback Off
}
