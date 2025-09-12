Shader "Hidden/FullScreenTint"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A{ float4 pos:POSITION; float2 uv:TEXCOORD0; };
            struct V{ float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(A i){ V o; o.pos=TransformObjectToHClip(i.pos.xyz); o.uv=i.uv; return o; }
            half4 Frag(V i):SV_Target { return half4(1,0,0,1); } // vermelho
            ENDHLSL
        }
    }
}
