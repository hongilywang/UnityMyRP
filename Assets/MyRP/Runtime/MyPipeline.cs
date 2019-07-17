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
            context.DrawSkybox(cameras[0]);
            context.Submit();
        }
    }
}
