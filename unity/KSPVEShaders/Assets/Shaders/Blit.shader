Shader "Hidden/KSPVE/Blit"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex KSPVEVertBlit
            #pragma fragment frag
            #include "Include/KSPVECommon.cginc"

            sampler2D _MainTex;
            float _KSPVE_Scale = 1;

            float4 frag(KSPVEFullscreenV2F i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _KSPVE_Scale;
            }
            ENDCG
        }
    }
    Fallback Off
}
