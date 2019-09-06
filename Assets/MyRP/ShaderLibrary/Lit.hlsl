#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Common.hlsl"
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

CBUFFER_START(UnityPerFrame) 
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
    float4 unity_LightData;     //获取有效的灯光数量.y
    float4 unity_LightIndices[2];
CBUFFER_END

CBUFFER_START(UnityPerCamera)
    float3 _WorldSpaceCameraPos;
CBUFFER_END

#define MAX_VISIBLE_LIGHT 16

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHT];
CBUFFER_END

CBUFFER_START(_ShadowBuffer)
    float4x4 _WorldToShadowMatrixs[MAX_VISIBLE_LIGHT];
    float4x4 _WorldToShadowCascadeMatrices[5];
    float4 _CascadeCullingSpheres[4];
    float4 _ShadowData[MAX_VISIBLE_LIGHT];
    float4 _ShadowMapSize;
    float4 _CascadedShadowMapSize;
    float4 _GlobalShadowData;
    float _CascadedShadowStrength;
CBUFFER_END

CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    float _Cutoff;
CBUFFER_END

TEXTURE2D_SHADOW(_ShadowMap);
SAMPLER_CMP(sampler_ShadowMap);

TEXTURE2D_SHADOW(_CascadeShadowMap);
SAMPLER_CMP(sampler_CascadeShadowMap);

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

float InsideCasecadeCullingSphere(int index, float3 worldPos)
{
    float4 s = _CascadeCullingSpheres[index];
    return dot(worldPos - s.xyz, worldPos - s.xyz) < s.w;
}

float DistanceToCameraSqr(float3 worldPos)
{
    float3 cameraToFragment = worldPos - _WorldSpaceCameraPos;
    return dot(cameraToFragment, cameraToFragment);
}

float HardShadowAttenuation(float4 shadowPos, bool cascade = false)
{
    if (cascade)
    {
        return SAMPLE_TEXTURE2D_SHADOW(_CascadeShadowMap, sampler_CascadeShadowMap, shadowPos.xyz);
    }
    else
    {
        return SAMPLE_TEXTURE2D_SHADOW(_ShadowMap, sampler_ShadowMap, shadowPos.xyz);
    }
}

float SoftShadowAttenuation(float4 shadowPos, bool cascade = false)
{
    real tentWeights[9];
    real2 tentUVs[9];
    float4 size = cascade ? _CascadedShadowMapSize : _ShadowMapSize;
    SampleShadow_ComputeSamples_Tent_5x5(size, shadowPos.xy, tentWeights, tentUVs);
    float attenuation = 0;
    for (int i = 0; i < 9; i++)
    {
        attenuation += tentWeights[i] * HardShadowAttenuation(float4(tentUVs[i].xy, shadowPos.z, 0), cascade);
    }
    return attenuation;
}

float CascadedShadowAttenuation(float3 worldPos)
{
    #if !defined(_CASCADED_SHADOWS_HARD) && !defined(_CASCADED_SHADOWS_SOFT)
        return 1.0;
    #endif

    float4 cascadeFlags = float4(
        InsideCasecadeCullingSphere(0, worldPos),
        InsideCasecadeCullingSphere(1, worldPos),
        InsideCasecadeCullingSphere(2, worldPos),
        InsideCasecadeCullingSphere(3, worldPos)
    );
    //可以可视化的看到级联阴影的范围
    //return dot(cascadeFlags, 0.25);
    //(1,1,1,1) -> (1,0,0,0) -> 0
    //(0,1,1,1) -> (0,1,0,0) -> 1
    //(0,0,1,1) -> (0,0,1,0) -> 2
    //(0,0,0,1) -> (0,0,0,1) -> 3
    //(0,0,0,0) -> (0,0,0,0) -> 0
    cascadeFlags.yzw = saturate(cascadeFlags.yzw - cascadeFlags.xyz);
    float cascadeIndex = 4 - dot(cascadeFlags, float4(4, 3, 2, 1));
    float4 shadowPos = mul(_WorldToShadowCascadeMatrices[cascadeIndex], float4(worldPos, 1.0));
    float attenuation;
    #if defined(_CASCADED_SHADOWS_HARD)
        attenuation = HardShadowAttenuation(shadowPos, true);
    #else
        attenuation = SoftShadowAttenuation(shadowPos, true);
    #endif

    return lerp(1, attenuation, _CascadedShadowStrength);
}

float ShadowAttenuation(int index, float3 worldPos)
{
    #if !defined(_SHADOWS_HARD) && !defined(_SHADOWS_SOFT)
        return 1.0;
    #endif

    if (DistanceToCameraSqr(worldPos) > _GlobalShadowData.y)
        return 1.0;

    float4 shadowPos = mul(_WorldToShadowMatrixs[index], float4(worldPos, 1.0));
    shadowPos.xyz /= shadowPos.w;
    shadowPos.xy = saturate(shadowPos.xy);
    shadowPos.xy = shadowPos.xy * _GlobalShadowData.x + _ShadowData[index].zw;
    float attenuation;
    
    #if defined(_SHADOWS_HARD)
        #if defined(_SHADOWS_SOFT)
            if (_ShadowData[index].y == 0)
            {
                attenuation = HardShadowAttenuation(shadowPos);
            }
            else
            {
                attenuation = SoftShadowAttenuation(shadowPos);
            }
        #else
            attenuation = HardShadowAttenuation(shadowPos);
        #endif
    #else
        attenuation = SoftShadowAttenuation(shadowPos);
    #endif

    return lerp(1, attenuation, _ShadowData[index].x);
}

//参考LWRP的 计算对应灯光的index///////////
int GetPerObjectLightIndex(int index, int lightIndicesIndex)
{
    // The following code is more optimal than indexing unity_4LightIndices0.
    // Conditional moves are branch free even on mali-400
    half2 lightIndex2 = (index < 2.0h) ? unity_LightIndices[lightIndicesIndex].xy : unity_LightIndices[lightIndicesIndex].zw;
    half i_rem = (index < 2.0h) ? index : index - 2.0h;
    return (i_rem < 1.0h) ? lightIndex2.x : lightIndex2.y;
}

float3 MainLight(float3 normal, float3 worldPos)
{
    float shadowAttenuation = CascadedShadowAttenuation(worldPos);
    float3 lightColor = _VisibleLightColors[0].rgb;
    float3 lightDirection = _VisibleLightDirectionsOrPositions[0].xyz;
    float diffuse = saturate(dot(normal, lightDirection));
    diffuse *= shadowAttenuation;
    return diffuse * lightColor;
}
////////////////////////////////////////

float3 DiffuseLight (int index, float3 normal, float3 worldPos, float shadowAttenuation)
{
    float3 lightColor = _VisibleLightColors[index].rgb;
    float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
    float4 lightAttenuation = _VisibleLightAttenuations[index];
    float3 spotDirection = _VisibleLightSpotDirections[index].xyz;

    //同时处理了点光源和方向光的方向计算
    float3 lightVector = lightPositionOrDirection.xyz - worldPos * lightPositionOrDirection.w;
    float3 lightDirection = normalize(lightVector);
    float diffuse = saturate(dot(normal, lightDirection));

    float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
    rangeFade = saturate(1.0 - rangeFade * rangeFade);
    rangeFade *= rangeFade;

    float spotFade = dot(spotDirection, lightDirection);
    spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
    spotFade *= spotFade;

    float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
    diffuse *= shadowAttenuation * spotFade * rangeFade / distanceSqr;

    return diffuse * lightColor;
}

#define UNITY_MATRIX_M unity_ObjectToWorld
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/UnityInstancing.hlsl"


// CBUFFER_START(UnityPerMaterial)
//     float4 _Color;
// CBUFFER_END

//相同材质，不同的颜色属性也可以使用GPU Instance一次绘制
UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput 
{
    float4 pos : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput 
{
    float4 clipPos : SV_POSITION;
    float3 normal : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    float3 vertexLighting : TEXCOORD2;
    float2 uv : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

VertexOutput LitPassVertex (VertexInput input)
{
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
    output.worldPos = worldPos.xyz;

    //将不重要的灯光计算放到顶点计算中，减少pxiel的计算
    output.vertexLighting = 0;
    for (int i = 4; i < min(unity_LightData.y, 8); i++)
    {
        int lightIndex = GetPerObjectLightIndex(i, 1);
        output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos, 1);
    }

    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    return output;
}

float4 LitPassFragment (VertexOutput input, FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    input.normal = normalize(input.normal);
    input.normal = IS_FRONT_VFACE(isFrontFace, input.normal, -input.normal);

    float4 albedoAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    albedoAlpha *= UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color);
    #if defined(_CLIPPING_ON)
        clip(albedoAlpha.a - _Cutoff);
    #endif

    float3 diffuseLight = input.vertexLighting;
    #if defined(_CASCADED_SHADOWS_HARD) || defined(_CASCADED_SHADOWS_SOFT)
        diffuseLight += MainLight(input.normal, input.worldPos);
    #endif

    for (int i = 0; i < min(unity_LightData.y, 4); i++)
    {
        int lightIndex = GetPerObjectLightIndex(i, 0);
        float shadowAttenuation = ShadowAttenuation(lightIndex, input.worldPos);
        diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos, shadowAttenuation);
    }

    float3 color = diffuseLight * albedoAlpha.rgb;
    return float4(color, albedoAlpha.a);
}

#endif // MY_LIT_INCLUDED