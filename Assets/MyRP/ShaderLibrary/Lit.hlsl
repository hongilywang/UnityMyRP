#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame) 
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
CBUFFER_END

#define MAX_VISIBLE_LIGHT 4

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHT];
CBUFFER_END

float3 DiffuseLight (int index, float3 normal, float3 worldPos)
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
    diffuse *= spotFade * rangeFade / distanceSqr;

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
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput 
{
    float4 clipPos : SV_POSITION;
    float3 normal : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
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
    return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    input.normal = normalize(input.normal);
    float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

    float3 diffuseLight = 0;
    for (int i = 0; i < MAX_VISIBLE_LIGHT; i++)
    {
        diffuseLight += DiffuseLight(i, input.normal, input.worldPos);
    }

    float3 color = diffuseLight * albedo;
    return float4(color, 1);
}

#endif // MY_LIT_INCLUDED