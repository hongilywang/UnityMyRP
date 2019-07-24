using UnityEngine;
using UnityEngine.Rendering;

using Conditional = System.Diagnostics.ConditionalAttribute;

namespace MyRP
{
    public class MyPipeline : RenderPipeline
    {
        //创建command buffer
        const string commandBufferName = "MyRP Render Camera";
        CommandBuffer cameraBuffer = new CommandBuffer { name = commandBufferName };
        CullingResults culling;
        ScriptableCullingParameters cullingParameters;
        ShaderTagId shaderTagId = new ShaderTagId("SRPDefaultUnlit");

        //错误shader材质
        Material errorMaterial;

        //是否开启动态Batch
        bool enableDynamicBatching;
        //是否开启gup instance
        bool enableGPUInstancing;

        //将方向光的颜色和方向传入shader
        const int maxVisibleLights = 4;
        static int visibleLightColorsId = Shader.PropertyToID("_VisibleLightColors");
        static int visibleLightDirectionsId = Shader.PropertyToID("_VisibleLightDirections");
        Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
        Vector4[] visibleLightDirections = new Vector4[maxVisibleLights];

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

            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //根据相机设置来清理RT
            CameraClearFlags clearFlags = camera.clearFlags;
            cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

            //获取可见的方向光
            ConfigureLights();

            cameraBuffer.BeginSample(commandBufferName);

            //将方向光的颜色和方向传入shader
            cameraBuffer.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
            cameraBuffer.SetGlobalVectorArray(visibleLightDirectionsId, visibleLightDirections);

            context.ExecuteCommandBuffer(cameraBuffer);
            cameraBuffer.Clear();

            //Drawing
            SortingSettings sortingSettings = new SortingSettings(camera) {criteria = SortingCriteria.CommonOpaque };
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
            drawingSettings.enableDynamicBatching = enableDynamicBatching;
            drawingSettings.enableInstancing = enableGPUInstancing;
            FilteringSettings filteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            context.DrawSkybox(camera);

            //透明的渲染需要在skybox后面
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            filteringSettings.renderQueueRange = RenderQueueRange.transparent;
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            //
            DrawDefaultPipeline(context, camera);

            cameraBuffer.EndSample(commandBufferName);
            context.ExecuteCommandBuffer(cameraBuffer);
            cameraBuffer.Clear();

            context.Submit();
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
                VisibleLight light = culling.visibleLights[i];
                visibleLightColors[i] = light.finalColor;
                Vector4 v = light.localToWorldMatrix.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visibleLightDirections[i] = v;
            }
        }
    }
}
