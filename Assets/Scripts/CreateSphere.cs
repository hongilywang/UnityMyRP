using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CreateSphere : MonoBehaviour
{
    public int sphereNumber;
    public int rowNumber;
    public float sphereRadius;

    private GameObject sphereRoot;

    [ContextMenu("Create")]
    void Create()
    {
        if (sphereRoot != null)
        {
            DestroyImmediate(sphereRoot);
        }

        sphereRoot = new GameObject("sphereRoot");
        sphereRoot.transform.position = Vector3.zero;
        sphereRoot.transform.localScale = Vector3.one;


        GameObject sphereObj = Resources.Load<GameObject>("Sphere");
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
            Vector3 pos = new Vector3(row * (sphereRadius + 2), 0, column * (sphereRadius + 2));
            GameObject sphere = Instantiate<GameObject>(sphereObj, sphereRoot.transform);
            sphere.name = sphereName;
            sphere.transform.position = pos;
            sphere.transform.localScale = Vector3.one * sphereRadius;
        }
    }
}
