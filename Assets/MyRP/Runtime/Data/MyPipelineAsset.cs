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

        public enum ShadowCascades
        {
            Zero = 0,
            Two = 2,
            Four = 4
        }

        [SerializeField]
        ShadowCascades shadowCascades = ShadowCascades.Four;

        [SerializeField, HideInInspector]
        float twoCascadesSplit = 0.25f;

        [SerializeField, HideInInspector]
        Vector3 fourCascadesSplit = new Vector3(0.067f, 0.2f, 0.467f);

        protected override RenderPipeline CreatePipeline()
        {
            Vector3 shadowCascadeSplit = shadowCascades == ShadowCascades.Four ? fourCascadesSplit : new Vector3(twoCascadesSplit, 0f);
            return new MyPipeline(dynamicBatching, instancing, (int)shadowMapSize, shadowDistance, (int)shadowCascades, shadowCascadeSplit);
        }

    }
}
