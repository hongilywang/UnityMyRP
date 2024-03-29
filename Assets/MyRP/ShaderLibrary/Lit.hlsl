#ifndef MYRP_LIT_INCLUDED
#define MYRP_LIT_INCLUDED

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Common.hlsl"
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/ImageBasedLighting.hlsl"
#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/EntityLighting.hlsl"
#include "Lighting.hlsl"

CBUFFER_START(UnityPerFrame) 
    float4x4 unity_MatrixVP;
CBUFFER_END

CBUFFER_START(UnityPerDraw)
    float4x4 unity_ObjectToWorld, unity_WorldToObject;
    float4 unity_LightData;     //获取有效的灯光数量.y
    float4 unity_LightIndices[2];
    float4 unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax;
    float4 unity_SpecCube0_ProbePosition, unity_SpecCube0_HDR;
    float4 unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax;
    float4 unity_SpecCube1_ProbePosition, unity_SpecCube1_HDR;
    float4 unity_LightmapST, unity_DynamicLightmapST;
    float4 unity_SHAr, unity_SHAg, unity_SHAb;
    float4 unity_SHBr, unity_SHBg, unity_SHBb;
    float4 unity_SHC;
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

CBUFFER_START(UnityProbeVolume)
    float4 unity_ProbeVolumeParams;
    float4x4 unity_ProbeVolumeWorldToObject;
    float3 unity_ProbeVolumeSizeInv;
    float3 unity_ProbeVolumeMin;
CBUFFER_END

TEXTURE2D_SHADOW(_ShadowMap);
SAMPLER_CMP(sampler_ShadowMap);

TEXTURE2D_SHADOW(_CascadeShadowMap);
SAMPLER_CMP(sampler_CascadeShadowMap);

TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);

TEXTURE2D(unity_Lightmap);
SAMPLER(samplerunity_Lightmap);

TEXTURE3D_FLOAT(unity_ProbeVolumeSH);
SAMPLER(samplerunity_ProbeVolumeSH);

TEXTURE2D(unity_DynamicLightmap);
SAMPLER(samplerunity_DynamicLightmap);

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
    #if !defined(_RECEIVE_SHADOWS)
        return 1.0;
    #elif !defined(_CASCADED_SHADOWS_HARD) && !defined(_CASCADED_SHADOWS_SOFT)
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
    #if !defined(_RECEIVE_SHADOWS)
        return 1.0;
    #elif !defined(_SHADOWS_HARD) && !defined(_SHADOWS_SOFT)
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

float3 MainLight(LitSurface s)
{
    float shadowAttenuation = CascadedShadowAttenuation(s.position);
    float3 lightColor = _VisibleLightColors[0].rgb;
    float3 lightDirection = _VisibleLightDirectionsOrPositions[0].xyz;
    float3 color = LightSurface(s, lightDirection);
    color *= shadowAttenuation;
    return color * lightColor;
}
////////////////////////////////////////

float3 GenericLight (int index, LitSurface s, float shadowAttenuation)
{
    float3 lightColor = _VisibleLightColors[index].rgb;
    float4 lightPositionOrDirection = _VisibleLightDirectionsOrPositions[index];
    float4 lightAttenuation = _VisibleLightAttenuations[index];
    float3 spotDirection = _VisibleLightSpotDirections[index].xyz;

    //同时处理了点光源和方向光的方向计算
    float3 lightVector = lightPositionOrDirection.xyz - s.position * lightPositionOrDirection.w;
    float3 lightDirection = normalize(lightVector);
    float3 color = LightSurface(s, lightDirection);

    float rangeFade = dot(lightVector, lightVector) * lightAttenuation.x;
    rangeFade = saturate(1.0 - rangeFade * rangeFade);
    rangeFade *= rangeFade;

    float spotFade = dot(spotDirection, lightDirection);
    spotFade = saturate(spotFade * lightAttenuation.z + lightAttenuation.w);
    spotFade *= spotFade;

    float distanceSqr = max(dot(lightVector, lightVector), 0.00001);
    color *= shadowAttenuation * spotFade * rangeFade / distanceSqr;

    return color * lightColor;
}

float3 BoxProjection(float3 direction, float3 position, float4 cubemapPosition, float4 boxMin, float4 boxMax)
{
    UNITY_BRANCH
    if (cubemapPosition.w > 0)
    {
        float3 factors = ((direction > 0 ? boxMax.xyz : boxMin.xyz) - position) / direction;
        float scalar = min(min(factors.x, factors.y), factors.z);
        direction = direction * scalar + (position - cubemapPosition.xyz);
    }
    return direction;
}

float3 SampleEnvironment(LitSurface s)
{
    float3 reflectVector = reflect(-s.viewDir, s.normal);
    float mip = PerceptualRoughnessToMipmapLevel(s.perceptualRoughness);
    float3 uvw = BoxProjection(reflectVector, s.position, unity_SpecCube0_ProbePosition, unity_SpecCube0_BoxMin, unity_SpecCube0_BoxMax);
    float4 sampleCube = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, uvw, mip);

    float3 color = DecodeHDREnvironment(sampleCube, unity_SpecCube0_HDR);

    float blend = unity_SpecCube0_BoxMin.w;
    if (blend < 0.99999)
    {
        uvw = BoxProjection(reflectVector, s.position, unity_SpecCube1_ProbePosition, unity_SpecCube1_BoxMin, unity_SpecCube1_BoxMax);
        sampleCube = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube1, samplerunity_SpecCube0, uvw, mip);
        color = lerp(DecodeHDREnvironment(sampleCube, unity_SpecCube1_HDR), color, blend);
    }

    return color;
}

#define UNITY_MATRIX_M unity_ObjectToWorld
#define UNITY_MATRIX_I_M unity_WorldToObject

#include "com.unity.render-pipelines.core@6.9.1/ShaderLibrary/UnityInstancing.hlsl"

// CBUFFER_START(UnityPerMaterial)
//     float4 _Color;
// CBUFFER_END

//相同材质，不同的颜色属性也可以使用GPU Instance一次绘制
UNITY_INSTANCING_BUFFER_START(PerInstance)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
    UNITY_DEFINE_INSTANCED_PROP(float, _Metallic)
    UNITY_DEFINE_INSTANCED_PROP(float, _Smoothness)
    UNITY_DEFINE_INSTANCED_PROP(float4, _EmissionColor)
UNITY_INSTANCING_BUFFER_END(PerInstance)

struct VertexInput 
{
    float4 pos : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertexOutput 
{
    float4 clipPos : SV_POSITION;
    float3 normal : TEXCOORD0;
    float3 worldPos : TEXCOORD1;
    float3 vertexLighting : TEXCOORD2;
    float2 uv : TEXCOORD3;
    #if defined(LIGHTMAP_ON)
        float2 lightmapUV : TEXCOORD4;
    #endif
    #if defined(DYNAMICLIGHTMAP_ON)
        float2 dynamicLightmapUV : TEXCOORD5;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float3 SampleDynamicLightmap(float2 uv)
{
    return SampleSingleLightmap(TEXTURE2D_ARGS(unity_DynamicLightmap, samplerunity_DynamicLightmap), uv, float4(1, 1, 0, 0), false, float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0));
}

float3 SampleLightmap(float2 uv)
{
    return SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap), uv, float4(1, 1, 0, 0), 
    #if defined(UNITY_LIGHTMAP_FULL_HDR)
        false,
    #else
        true,
    #endif
    float4(LIGHTMAP_HDR_MULTIPLIER, LIGHTMAP_HDR_EXPONENT, 0.0, 0.0)
    );
}

float3 SampleLightProbes(LitSurface s)
{
    if (unity_ProbeVolumeParams.x)
    {
        return SampleProbeVolumeSH4(TEXTURE3D_ARGS(unity_ProbeVolumeSH, samplerunity_ProbeVolumeSH), s.position, s.normal, unity_ProbeVolumeWorldToObject, unity_ProbeVolumeParams.y, unity_ProbeVolumeParams.z, unity_ProbeVolumeMin, unity_ProbeVolumeSizeInv);
    }
    else
    {
        float4 coefficients[7];
        coefficients[0] = unity_SHAr;
        coefficients[1] = unity_SHAg;
        coefficients[2] = unity_SHAb;
        coefficients[3] = unity_SHBr;
        coefficients[4] = unity_SHBg;
        coefficients[5] = unity_SHBb;
        coefficients[6] = unity_SHC;
        return max(0.0, SampleSH9(coefficients, s.normal));
    }
}

float3 GlobalIllumination(VertexOutput input, LitSurface surface)
{
    #if defined(LIGHTMAP_ON)
        float3 gi = SampleLightmap(input.lightmapUV);
        #if defined(DYNAMICLIGHTMAP_ON)
            gi += SampleDynamicLightmap(input.dynamicLightmapUV);
        #endif
        return gi;
    #elif defined(DYNAMICLIGHTMAP_ON)
        return SampleDynamicLightmap(input.dynamicLightmapUV);
    #else
        //开启GUP Instance会导致错误
        return SampleLightProbes(surface);
        //return float3(0, 0, 0);
    #endif
}

VertexOutput LitPassVertex (VertexInput input)
{
    VertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    float4 worldPos = mul(UNITY_MATRIX_M, float4(input.pos.xyz, 1.0));
    output.clipPos = mul(unity_MatrixVP, worldPos);
    //法线的处理
    //#if defined(UNITY_ASSUME_UNIFORM_SCALING)
    //    output.normal = mul((float3x3)UNITY_MATRIX_M, input.normal);
    //#else
        output.normal = normalize(mul(input.normal, (float3x3)UNITY_MATRIX_I_M));
    //#endif
    output.worldPos = worldPos.xyz;

    //将不重要的灯光计算放到顶点计算中，减少pxiel的计算
    LitSurface  surface = GetLitSurfaceVertex(output.normal, output.worldPos);
    output.vertexLighting = 0;
    for (int i = 4; i < min(unity_LightData.y, 8); i++)
    {
        int lightIndex = GetPerObjectLightIndex(i, 1);
        output.vertexLighting += GenericLight(lightIndex, surface, 1);
    }

    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    #if defined(LIGHTMAP_ON)
        output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
    #endif

    #if defined(DYNAMICLIGHTMAP_ON)
        output.dynamicLightmapUV = input.dynamicLightmapUV * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
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

    float3 viewDir = normalize(_WorldSpaceCameraPos - input.worldPos.xyz);
    LitSurface surface = GetLitSurface(input.normal, input.worldPos, viewDir, albedoAlpha.rgb,
    UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Metallic), UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Smoothness));
    #if defined(_PREMULTIPLY_ALPHA)
        PremultiplyAlpha(surface, albedoAlpha.a)
    #endif

    float3 color = input.vertexLighting * surface.diffuse;
    #if defined(_CASCADED_SHADOWS_HARD) || defined(_CASCADED_SHADOWS_SOFT)
        color += MainLight(surface);
    #endif

    for (int i = 0; i < min(unity_LightData.y, 4); i++)
    {
        int lightIndex = GetPerObjectLightIndex(i, 0);
        float shadowAttenuation = ShadowAttenuation(lightIndex, input.worldPos);
        color += GenericLight(lightIndex, surface, shadowAttenuation);
    }

    color = color * albedoAlpha.rgb;
    color += ReflectEnvironment(surface, SampleEnvironment(surface));
    color += GlobalIllumination(input, surface) * surface.diffuse;
    color += UNITY_ACCESS_INSTANCED_PROP(PerInstance, _EmissionColor).rgb;
    return float4(color, albedoAlpha.a);
}

#endif // MY_LIT_INCLUDED