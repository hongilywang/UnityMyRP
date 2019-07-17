using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace MyRP
{
    public class MyPipeline : RenderPipeline
    {
        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            for (int i = 0; i < cameras.Length; ++i)
                Render(context, cameras[i]);
        }

        void Render(ScriptableRenderContext renderContext, Camera camera)
        {
            //将相机的属性（比如相机的视口矩阵）出入shader
            renderContext.SetupCameraProperties(camera);
            renderContext.DrawSkybox(camera);
            renderContext.Submit();
        }
    }
}
