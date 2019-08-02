using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

using Conditional = System.Diagnostics.ConditionalAttribute;

namespace MyRP
{
    public class MyPipeline : RenderPipeline
    {
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

        //将方向光的颜色和方向传入shader
        const int maxVisibleLights = 16;
        static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        static int visibleLightDirectionsOrPositionsId = Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
        static int visibleLightAttenuationsId = Shader.PropertyToID("_VisibleLightAttenuations");
        static int visibleLightSpotDirectionsId = Shader.PropertyToID("_VisibleLightSpotDirections");
        static int unity_LightDataId = Shader.PropertyToID("unity_LightData");

        Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
        Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
        Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
        Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

        public MyPipeline(bool dynamicBatching, bool instancing)
        {
            //light的强度值使用线性空间
            GraphicsSettings.lightsUseLinearIntensity = true;
            enableDynamicBatching = dynamicBatching;
            enableGPUInstancing = instancing;
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

#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

            culling = context.Cull(ref cullingParameters);

            RenderShadows(context);

            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //根据相机设置来清理RT
            CameraClearFlags clearFlags = camera.clearFlags;
            cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

            //获取可见的方向光
            if (culling.visibleLights.Length > 0)
                ConfigureLights();
            else
                Shader.SetGlobalVector(unity_LightDataId, Vector4.zero);

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
            for (int i = 0; i < culling.visibleLights.Length; ++i)
            {
                if (i == maxVisibleLights)
                    break;

                Vector4 attenuation = Vector4.zero;
                attenuation.w = 1;

                VisibleLight light = culling.visibleLights[i];
                visibleLightColors[i] = light.finalColor;
                if (light.lightType == LightType.Directional)
                {
                    Vector4 v = light.localToWorldMatrix.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightDirectionsOrPositions[i] = v;
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

                    }
                }
                visibleLightAttenuations[i] = attenuation;
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
            //设置shadowMap贴图的
            shadowMap = RenderTexture.GetTemporary(512, 152, 16, RenderTextureFormat.Shadowmap);
            shadowMap.filterMode = FilterMode.Bilinear;
            shadowMap.wrapMode = TextureWrapMode.Clamp;

            //告诉GPU设置rt
            CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);
            shadowBuffer.BeginSample(commandShadowBufferName);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();

            //获取spot等的相关矩阵
            Matrix4x4 viewMatrix, projectionMatrix;
            ShadowSplitData splitData;
            culling.ComputeSpotShadowMatricesAndCullingPrimitives(0, out viewMatrix, out projectionMatrix, out splitData);
            shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();

            //正式draw
            var shadowSetting = new ShadowDrawingSettings(culling, 0);
            context.DrawShadows(ref shadowSetting);

            shadowBuffer.EndSample(commandShadowBufferName);
            context.ExecuteCommandBuffer(shadowBuffer);
            shadowBuffer.Clear();
        }
    }
}
