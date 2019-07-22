using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MyRP
{
    [CreateAssetMenu(fileName = "MyPipelineAsset", menuName = "MyRP/Create RP Asset")]
    public class MyPipelineAsset : RenderPipelineAsset
    {
        //动态Batch开关
        [SerializeField]
        bool dynamicBatching;

        //gpu instance开关
        [SerializeField]
        bool instancing;

        protected override RenderPipeline CreatePipeline()
        {
            return new MyPipeline(dynamicBatching, instancing);
        }

    }
}
