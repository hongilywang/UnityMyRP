using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

using Conditional = System.Diagnostics.ConditionalAttribute;

namespace MyRP
{
    public class MyPipeline : RenderPipeline
    {
        //
        const string shadowsHardKeyword = "_SHADOWS_HARD";
        const string shadowsSoftKeyword = "_SHADOWS_SOFT";

        //创建command buffer
        const string commandCameraBufferName = "MyRP Render Camera";
        const string commandShadowBufferName = "MyRP Render Shadows";
        CommandBuffer cameraBuffer = new CommandBuffer { name = commandCameraBufferName };
        CommandBuffer shadowBuffer = new CommandBuffer { name = commandShadowBufferName };

        CullingResults culling;
        ScriptableCullingParameters cullingParameters;
        ShaderTagId shaderTagId = new ShaderTagId("SRPDefaultUnlit");

        //错误shader材质
        Material errorMaterial;

        //是否开启动态Batch
        bool enableDynamicBatching;
        //是否开启gup instance
        bool enableGPUInstancing;

        //shadowMap的阴影贴图
        RenderTexture shadowMap;
        //阴影贴图尺寸
        int shadowMapSize;
        //阴影距离
        float shadowDistance;

        //将方向光的颜色和方向传入shader
        const int maxVisibleLights = 16;
        static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
        static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
        static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
        static int unity_LightDataId = Shader.PropertyToID("unity_LightData");
        static int shadowMapId = Shader.PropertyToID("_ShadowMap");
        static int worldToShadowMatrixsId = Shader.PropertyToID("_WorldToShadowMatrixs");
        static int shadowBiasId = Shader.PropertyToID("_ShadowBias");
        static int shadowMapSizeId = Shader.PropertyToID("_ShadowMapSize");
        static int shadowDataId = Shader.PropertyToID("_ShadowData");

        Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
        Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
        Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
        Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];
        Vector4[] shadowData = new Vector4[maxVisibleLights];
        Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];

        //阴影图的数量
        int shadowTileCount;

        public MyPipeline(bool dynamicBatching, bool instancing, int shadowMapSize, float shadowDistance)
        {
            //light的强度值使用线性空间
            GraphicsSettings.lightsUseLinearIntensity = true;
            enableDynamicBatching = dynamicBatching;
            enableGPUInstancing = instancing;
            this.shadowMapSize = shadowMapSize;
            this.shadowDistance = shadowDistance;
        }

        protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            for (int i = 0; i < cameras.Length; ++i)
                Render(renderContext, cameras[i]);
        }

        void Render(ScriptableRenderContext context, Camera camera)
        {
            //culling
            if (!camera.TryGetCullingParameters(out cullingParameters))
                return;
            cullingParameters.shadowDistance = Mathf.Min(shadowDistance, camera.farClipPlane);

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

            culling = context.Cull(ref cullingParameters);

            //获取可见的方向光
            if (culling.visibleLights.Length > 0)
            {
                ConfigureLights();
                if (shadowTileCount > 0)
                {
                    RenderShadows(context);
                }
                else
                {
                    CoreUtils.SetKeyword(cameraBuffer, shadowsHardKeyword, false);
                    CoreUtils.SetKeyword(cameraBuffer, shadowsSoftKeyword, false);
                }
            }
            else
            {
                Shader.SetGlobalVector(unity_LightDataId, Vector4.zero);
                CoreUtils.SetKeyword(cameraBuffer, shadowsHardKeyword, false);
                CoreUtils.SetKeyword(cameraBuffer, shadowsSoftKeyword, false);
            }
            ConfigureLights();


            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //根据相机设置来清理RT
            CameraClearFlags clearFlags = camera.clearFlags;
            cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

            cameraBuffer.BeginSample(commandCameraBufferName);

            //将方向光的颜色和方向传入shader
            cameraBuffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
            cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions);
            cameraBuffer.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
            cameraBuffer.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);

            context.ExecuteCommandBuffer(cameraBuffer);
            cameraBuffer.Clear();

            //Drawing
            SortingSettings sortingSettings = new SortingSettings(camera) {criteria = SortingCriteria.CommonOpaque };
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings)
            {
                enableDynamicBatching = enableDynamicBatching,
                enableInstancing = enableGPUInstancing,
                perObjectData = PerObjectData.None
            };

            if (culling.visibleLights.Length > 0)
                drawingSettings.perObjectData = PerObjectData.LightIndices | PerObjectData.LightData;

            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            context.DrawSkybox(camera);

            //透明的渲染需要在skybox后面
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            //
            DrawDefaultPipeline(context, camera);

            cameraBuffer.EndSample(commandCameraBufferName);
            context.ExecuteCommandBuffer(cameraBuffer);
            cameraBuffer.Clear();

            context.Submit();
            if (shadowMap)
            {
                RenderTexture.ReleaseTemporary(shadowMap);
                shadowMap = null;
            }
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
        {
            if (errorMaterial == null)
            {
                Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
                errorMaterial = new Material(errorShader) { hideFlags = HideFlags.HideAndDontSave };
            }

            DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("Always"));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("Vertex"));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("VertexLMRGBM"));
            drawingSettings.SetShaderPassName(1, new ShaderTagId("VertexLM"));

            drawingSettings.overrideMaterial = errorMaterial;
            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.all, -1);
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);
        }

        //存入可见的方向光信息
        void ConfigureLights()
        {
            shadowTileCount = 0;
            for (int i = 0; i < culling.visibleLights.Length; ++i)
            {
                if (i == maxVisibleLights)
                    break;

                Vector4 attenuation = Vector4.zero;
                VisibleLight light = culling.visibleLights[i];
                visibleLightColors[i] = light.finalColor;
                attenuation.w = 1;
                Vector4 shadow = Vector4.zero;

                if (light.lightType == LightType.Directional)
                {
                    Vector4 v = light.localToWorldMatrix.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightDirectionsOrPositions[i] = v;
                    shadow = ConfigureShadows(i, light.light);

                    //用z通道为1来表示存储的是方向光的数据
                    shadow.z = 1;
                }
                else
                {
                    visibleLightDirectionsOrPositions[i] = light.localToWorldMatrix.GetColumn(3);
                    attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
                    if (light.lightType == LightType.Spot)
                    {
                        Vector4 v = light.localToWorldMatrix.GetColumn(2);
                        v.x = -v.x;
                        v.y = -v.y;
                        v.z = -v.z;
                        visibleLightSpotDirections[i] = v;

                        //计算lwpl里面的聚光灯的innerCos和outerCos
                        float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                        float outerCos = Mathf.Cos(outerRad);
                        float outerTan = Mathf.Tan(outerRad);
                        float innerCos = Mathf.Cos(Mathf.Atan((46 / 64f) * outerTan));
                        //lwpl的衰减定义是
                        /*
                            (Ds * Dl)a + b
                            a = 1/(cos(ri) - cos(ro)
                            b = -cos(ro)a

                            cos(ri)是innerCos, cos(ro)是outerCos
                            Ds * Dl是聚光灯朝向和灯光方向的点乘
                        */
                        float anleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                        attenuation.z = 1f / anleRange;
                        attenuation.w = -outerCos * attenuation.z;

                        shadow = ConfigureShadows(i, light.light);

                    }
                }
                visibleLightAttenuations[i] = attenuation;
                shadowData[i] = shadow;
            }

            //排除多余的light
            if (culling.visibleLights.Length > maxVisibleLights)
            {
                NativeArray<int> lightIndices = culling.GetLightIndexMap(Allocator.Temp);
                for (int i = maxVisibleLights; i < culling.visibleLights.Length; ++i)
                    lightIndices[i] = -1;

                culling.SetLightIndexMap(lightIndices);
            }
        }

        //渲染shadowmap
        void RenderShadows(ScriptableRenderContext context)
        {
            int split;
            if (shadowTileCount <= 1)
                split = 1;
            else if (shadowTileCount <= 4)
                split = 2;
            else if (shadowTileCount <= 9)
                split = 3;
            else
                split = 4;

            float tileSize = shadowMapSize / split;
            float tileScale = 1f / split;
            Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

            //设置shadowMap贴图的
            shadowMap = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);
            shadowMap.filterMode = FilterMode.Bilinear;
            shadowMap.wrapMode = TextureWrapMode.Clamp;

            //告诉GPU设置rt
            CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);
            shadowBuffer.BeginSample(commandShadowBufferName);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();

            int tileIndex = 0;
            bool hardShadows = false;
            bool softShadows = false;
            for (int i = 0; i < culling.visibleLights.Length; ++i)
            {
                if (i == maxVisibleLights)
                    break;

                //跳过不需要产生阴影的灯光
                if (shadowData[i].x <= 0f)
                    continue;

                //获取spot等的相关矩阵
                //目前这里有报错
                Matrix4x4 viewMatrix, projectionMatrix;
                ShadowSplitData splitData;

                bool validShadows;

                if (shadowData[i].z > 0)
                    validShadows = culling.ComputeDirectionalShadowMatricesAndCullingPrimitives(i, 0, 1, Vector3.right, (int)tileSize, culling.visibleLights[i].light.shadowNearPlane, out viewMatrix, out projectionMatrix, out splitData);
                else
                    validShadows = culling.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData);

                if (!validShadows)
                {
                    shadowData[i].x = 0f;
                    continue;
                }

                //将16个灯光的阴影图渲染到一个rt上，将rt分成16分
                float tileOffsetX = tileIndex % split;
                float tileOffsetY = tileIndex / split;
                tileViewport.x = tileOffsetX * tileSize;
                tileViewport.y = tileOffsetY * tileSize;
                if (split > 1)
                {
                    shadowBuffer.SetViewport(tileViewport);
                    //将16分阴影图用一个间隔隔开，避免采样时的差值错误
                    shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4f, tileViewport.y + 4f, tileSize - 8f, tileSize - 8f));
                }

                shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                shadowBuffer.SetGlobalFloat(shadowBiasId, culling.visibleLights[i].light.shadowBias);
                context.ExecuteCommandBuffer(shadowBuffer);
                shadowBuffer.Clear();

                //正式draw
                var shadowSettings = new ShadowDrawingSettings(culling, i);
                var shadowSettingSplitData = shadowSettings.splitData;
                //对于方向光而言，cullingSphere包含了需要渲染进阴影图的所有物体，减少不必要物体的渲染
                shadowSettingSplitData.cullingSphere = splitData.cullingSphere;
                shadowSettings.splitData = shadowSettingSplitData;

                context.DrawShadows(ref shadowSettings);

                if (SystemInfo.usesReversedZBuffer)
                {
                    projectionMatrix.m20 = -projectionMatrix.m20;
                    projectionMatrix.m21 = -projectionMatrix.m21;
                    projectionMatrix.m22 = -projectionMatrix.m22;
                    projectionMatrix.m23 = -projectionMatrix.m23;
                }
                var scaleOffset = Matrix4x4.TRS(Vector3.one * 0.5f, Quaternion.identity, Vector3.one * 0.5f);
                worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);

                if (split > 1)
                {
                    var tileMatrix = Matrix4x4.identity;
                    tileMatrix.m00 = tileMatrix.m11 = tileScale;
                    tileMatrix.m03 = tileOffsetX * tileScale;
                    tileMatrix.m13 = tileOffsetY * tileScale;
                    worldToShadowMatrices[i] = tileMatrix * worldToShadowMatrices[i];
                }

                tileIndex += 1;

                if (shadowData[i].y <= 0f)
                    hardShadows = true;
                else
                    softShadows = true;
            }

            if (split > 1)
                shadowBuffer.DisableScissorRect();

            CoreUtils.SetKeyword(shadowBuffer, shadowsHardKeyword, hardShadows);
            CoreUtils.SetKeyword(shadowBuffer, shadowsSoftKeyword, softShadows);

            shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);
            shadowBuffer.SetGlobalMatrixArray(worldToShadowMatrixsId, worldToShadowMatrices);
            shadowBuffer.SetGlobalVectorArray(shadowDataId, shadowData);
            float invShadowMapSize = 1f / shadowMapSize;
            shadowBuffer.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, invShadowMapSize, invShadowMapSize));

            shadowBuffer.EndSample(commandShadowBufferName);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();
        }

        Vector4 ConfigureShadows(int lightIndex, Light shadowLight)
        {
            Vector4 shadow = Vector4.zero;
            Bounds shadowBounds;
            if (shadowLight.shadows != LightShadows.None && culling.GetShadowCasterBounds(lightIndex, out shadowBounds))
            {
                shadowTileCount += 1;
                shadow.x = shadowLight.shadowStrength;
                shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
            }
            return shadow;
        }
    }
}
