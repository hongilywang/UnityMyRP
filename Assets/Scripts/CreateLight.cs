using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateLight : MonoBehaviour
{
    public int sphereNumber;
    public int rowNumber;
    public float sphereRadius;
    public float height;
    public string lightRes;

    private GameObject lightRoot;

    [ContextMenu("Create")]
    void Create()
    {
        if (lightRoot != null)
        {
            DestroyImmediate(lightRoot);
        }

        lightRoot = new GameObject("lightRoot");
        lightRoot.transform.position = Vector3.zero;
        lightRoot.transform.localScale = Vector3.one;


        GameObject sphereObj = Resources.Load<GameObject>(lightRes);
        if (sphereObj == null)
        {
            Debug.LogError("创建Sphere失败！");
            return;
        }

        for (int i = 0; i < sphereNumber; ++i)
        {
            var row = i % rowNumber;
            var column = i / rowNumber;

            string sphereName = i.ToString();
            Vector3 pos = new Vector3(row * (sphereRadius + 2) + 10, height, column * (sphereRadius + 2) + 10);
            GameObject sphere = Instantiate<GameObject>(sphereObj, lightRoot.transform);
            sphere.name = sphereName;
            sphere.transform.position = pos;
            sphere.transform.localScale = Vector3.one;
        }
    }
}
