using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

internal static class ModelTool
{
    public static void DisableModelCameras(GameObject model)
    {
        if (model == null) return;

        // 查找模型及其所有子物体中的Camera组件（包括未激活的）
        Camera[] camerasInModel = model.GetComponentsInChildren<Camera>(includeInactive: true);

        foreach (Camera cam in camerasInModel)
        {
            // 禁用相机（使其不影响场景视角）
            cam.gameObject.SetActive(false);
            Debug.Log($"已禁用FBX中的相机：{cam.gameObject.name}");
        }
    }
    public static void AddMeshCollidersToModel(GameObject model, bool highlight = true)
    {
        if (model == null) return;

        // 查找模型及其所有子物体中的MeshFilter组件
        MeshFilter[] meshFilters = model.GetComponentsInChildren<MeshFilter>();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            // 为每个带有MeshFilter的物体添加MeshCollider
            MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            //meshCollider.convex = true; // 设置为凸碰撞体，确保碰撞检测正常工作
            AddRigidbodyToModel(meshFilter.gameObject, highlight);
        }

        Debug.Log($"为模型 {model.name} 添加了 {meshFilters.Length} 个MeshCollider");
    }

    /// <summary>
    /// 为模型添加Rigidbody，并设置为仅碰撞检测模式
    /// </summary>
    public static void AddRigidbodyToModel(GameObject model, bool highlight)
    {
        if (model == null) return;

        Rigidbody rigidbody = model.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true; // 设置为非运动学刚体，以便触发碰撞事件
        rigidbody.useGravity = false; // 不使用重力
        //rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // 使用连续碰撞检测，提高准确性
        rigidbody.constraints = RigidbodyConstraints.FreezeAll; // 冻结所有运动，相当于运动学刚体但能触发碰撞事件
        if (highlight)
        {
            model.AddComponent<ModelCollisionHighlighter>();
        }

        Debug.Log($"为模型 {model.name} 添加了Rigidbody");
    }
}
