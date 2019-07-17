using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRP
{
    [CreateAssetMenu(fileName = "MyPipelineAsset", menuName = "MyRP/Create RP Asset")]
    public class MyPipelineAsset : RenderPipelineAsset
    {
        protected override RenderPipeline CreatePipeline()
        {
            return new MyPipeline();
        }

    }
}
