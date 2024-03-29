#ifndef MYRP_SHADOWCASTER_INCLUDED
#define MYRP_SHADOWCASTER_INCLUDED

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame) 
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float _Cutoff;
CBUFFER_END

CBUFFER_START(_ShadowCasterBuffer)
    float _ShadowBias;
CBUFFER_END

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/UnityInstancing.hlsl"

UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput 
{
    float4 pos : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput 
{
    float4 clipPos : SV_POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput ShadowCasterPassVertex (VertexInput input)
{
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    #if UNITY_REVERSED_Z
        output.clipPos.z -= _ShadowBias;
	    output.clipPos.z = min(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
    #else
        output.clipPos.z += _ShadowBias;
	    output.clipPos.z = max(output.clipPos.z, output.clipPos.w * UNITY_NEAR_CLIP_VALUE);
    #endif
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    return output;
}

float4 ShadowCasterPassFragment (VertexOutput input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    #if !defined(_CLIPPING_OFF)
        float alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
        alpha *= UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).a;
        clip(alpha - _Cutoff);
    #endif
    return 0;
}

#endif // MYRP_SHADOWCASTER_INCLUDED