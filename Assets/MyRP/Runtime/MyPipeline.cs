using System.Collections;
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
            //将相机的属性（比如相机的视口矩阵）出入shader
            context.SetupCameraProperties(camera);

            //创建command buffer
            var commandBuffer = new CommandBuffer();
            commandBuffer.name = "CommandBufferBeforeSkybox";
            //清除RT的深度值，保留颜色
            commandBuffer.ClearRenderTarget(true, false, Color.clear);
            context.ExecuteCommandBuffer(commandBuffer);
            commandBuffer.Release();

            context.DrawSkybox(camera);
            context.Submit();
        }
    }
}
