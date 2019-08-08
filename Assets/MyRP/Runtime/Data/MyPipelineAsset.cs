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
        bool dynamicBatching = false;

        //gpu instance开关
        [SerializeField]
        bool instancing = false;

        //阴影距离
        [SerializeField]
        float shadowDistance = 100f;

        public enum ShadowMapSize
        {
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096
        }
        //阴影贴图大小
        [SerializeField]
        ShadowMapSize shadowMapSize = ShadowMapSize._1024;

        protected override RenderPipeline CreatePipeline()
        {
            return new MyPipeline(dynamicBatching, instancing, (int)shadowMapSize, shadowDistance);
        }

    }
}
