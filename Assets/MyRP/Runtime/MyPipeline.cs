using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

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
            culling = context.Cull(ref cullingParameters);

            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //根据相机设置来清理RT
            CameraClearFlags clearFlags = camera.clearFlags;
            cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);
            cameraBuffer.BeginSample(commandBufferName);
            context.ExecuteCommandBuffer(cameraBuffer);
            cameraBuffer.Clear();

            //Drawing
            SortingSettings sortingSettings = new SortingSettings(camera) {criteria = SortingCriteria.CommonOpaque };
            DrawingSettings drawingSettings = new DrawingSettings(shaderTagId, sortingSettings);
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
    }
}
