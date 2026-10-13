// Used as a replacement shader by a hidden copy of the local camera that sees only the terrain layer: the eye depth (m) of
// the terrain in each pixel, so the snow pass can tell the ground from craft standing on it. Cleared to a large depth.
Shader "Hidden/KSPVE/Climate/TerrainDepth"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct v2f { float4 pos : SV_POSITION; float depth : TEXCOORD0; };

            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(vertex);
                o.depth = -UnityObjectToViewPos(vertex).z;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return float4(i.depth, i.depth, i.depth, 1);
            }
            ENDCG
        }
    }
}
