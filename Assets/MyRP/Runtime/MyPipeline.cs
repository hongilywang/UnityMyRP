﻿using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace MyRP
{
    public class MyPipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
        {
            for (int i = 0; i < cameras.Length; ++i)
                Render(renderContext, cameras[i]);
        }

        void Render(ScriptableRenderContext context, Camera camera)
        {
            //culling
            ScriptableCullingParameters cullingParameters;
            if (!camera.TryGetCullingParameters(out cullingParameters))
                return;
            CullingResults culling = context.Cull(ref cullingParameters);

            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //创建command buffer
            var commandBuffer = new CommandBuffer{ name = "CommandBufferBeforeSkybox" };
            //根据相机设置来清理RT
            CameraClearFlags clearFlags = camera.clearFlags;
            commandBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();

            //Drawing
            SortingSettings sortingSettings = new SortingSettings(camera);
            DrawingSettings drawingSettings = new DrawingSettings(new ShaderTagId("Unlit/Transparent"), sortingSettings);
            FilteringSettings filteringSettings = new FilteringSettings();
            context.DrawRenderers(culling, ref drawingSettings, ref filteringSettings);

            context.DrawSkybox(camera);
            context.Submit();
        }
    }
}
