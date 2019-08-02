#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Common.hlsl"

CBUFFER_START(UnityPerFrame) 
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld;
    float4 unity_LightData;     //获取有效的灯光数量.y
    float4 unity_LightIndices[2];
CBUFFER_END

#define MAX_VISIBLE_LIGHT 16

CBUFFER_START(_LightBuffer)
    float4 _VisibleLightColors[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightDirectionsOrPositions[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightAttenuations[MAX_VISIBLE_LIGHT];
    float4 _VisibleLightSpotDirections[MAX_VISIBLE_LIGHT];
CBUFFER_END

//参考LWRP的 计算对应灯光的index///////////
int GetPerObjectLightIndex(int index, int lightIndicesIndex)
{
    // The following code is more optimal than indexing unity_4LightIndices0.
    // Conditional moves are branch free even on mali-400
    half2 lightIndex2 = (index < 2.0h) ? unity_LightIndices[lightIndicesIndex].xy : unity_LightIndices[lightIndicesIndex].zw;
    half i_rem = (index < 2.0h) ? index : index - 2.0h;
    return (i_rem < 1.0h) ? lightIndex2.x : lightIndex2.y;
}
////////////////////////////////////////

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
    float3 vertexLighting : TEXCOORD2;
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
        output.vertexLighting += DiffuseLight(lightIndex, output.normal, output.worldPos);
    }

    return output;
}

float4 LitPassFragment (VertexOutput input) : SV_TARGET
{
    UNITY_SETUP_INSTANCE_ID(input);
    input.normal = normalize(input.normal);
    float3 albedo = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Color).rgb;

    float3 diffuseLight = 0;
    for (int i = 0; i < min(unity_LightData.y, 4); i++)
    {
        int lightIndex = GetPerObjectLightIndex(i, 0);
        diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos);
    }

    // for (int i = 0; i < min(unity_LightData.y, 8); i++)
    // {
    //     int lightIndex = GetPerObjectLightIndex(i, 1);
    //     diffuseLight += DiffuseLight(lightIndex, input.normal, input.worldPos);
    // }

    float3 color = diffuseLight * albedo;
    return float4(color, 1);
}

#endif // MY_LIT_INCLUDED