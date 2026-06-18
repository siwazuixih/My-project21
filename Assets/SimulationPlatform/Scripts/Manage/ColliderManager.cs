using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

public static class ColliderManager
{
    private const string ColliderDataFileExtension = ".collider.xml";

    /// <summary>
    /// 根据模型路径获取碰撞体数据文件路径
    /// </summary>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>碰撞体数据文件完整路径</returns>
    private static string GetColliderDataFilePath(string modelRelativePath)
    {
        if (string.IsNullOrEmpty(modelRelativePath))
        {
            return null;
        }

        // 获取完整的模型文件路径
        string executableDir = PathTool.GetExecutableDirPath();
        string fullModelPath = Path.Combine(executableDir, modelRelativePath);

        if (!File.Exists(fullModelPath))
        {
            Debug.LogError($"模型文件不存在: {fullModelPath}");
            return null;
        }

        // 将模型文件的扩展名替换为 .collider.xml
        string colliderFileName = Path.ChangeExtension(Path.GetFileName(fullModelPath), ColliderDataFileExtension);
        string colliderFilePath = Path.Combine(Path.GetDirectoryName(fullModelPath), colliderFileName);

        return colliderFilePath;
    }

    /// <summary>
    /// 保存碰撞体数据到模型所在目录
    /// </summary>
    /// <param name="colliderModel">碰撞体模型</param>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>是否保存成功</returns>
    public static bool SaveColliderData(ColliderModel colliderModel, string modelRelativePath)
    {
        try
        {
            string colliderFilePath = GetColliderDataFilePath(modelRelativePath);
            if (string.IsNullOrEmpty(colliderFilePath))
            {
                return false;
            }

            Debug.Log($"正在保存碰撞体数据到: {colliderFilePath}");

            // 创建 XmlSerializer
            XmlSerializer serializer = new XmlSerializer(typeof(ColliderModel));

            // 写入文件
            using (StreamWriter writer = new StreamWriter(colliderFilePath, false, System.Text.Encoding.UTF8))
            {
                serializer.Serialize(writer, colliderModel);
            }

            Debug.Log($"碰撞体数据保存成功: {colliderFilePath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return false;
        }
    }

    /// <summary>
    /// 从模型所在目录加载碰撞体数据
    /// </summary>
    /// <param name="modelRelativePath">模型相对路径</param>
    /// <returns>碰撞体模型，加载失败返回null</returns>
    public static ColliderModel LoadColliderData(string modelRelativePath)
    {
        try
        {
            string colliderFilePath = GetColliderDataFilePath(modelRelativePath);
            if (string.IsNullOrEmpty(colliderFilePath) || !File.Exists(colliderFilePath))
            {
                Debug.Log($"碰撞体数据文件不存在: {colliderFilePath}");
                return null;
            }

            Debug.Log($"正在加载碰撞体数据: {colliderFilePath}");

            // 创建 XmlSerializer
            XmlSerializer serializer = new XmlSerializer(typeof(ColliderModel));

            // 读取文件
            using (StreamReader reader = new StreamReader(colliderFilePath, System.Text.Encoding.UTF8))
            {
                ColliderModel colliderModel = serializer.Deserialize(reader) as ColliderModel;
                int totalMeshes = 0;
                if (colliderModel.MjRoots != null)
                {
                    foreach (var mjRoot in colliderModel.MjRoots)
                    {
                        totalMeshes += mjRoot.Meshes != null ? mjRoot.Meshes.Count : 0;
                    }
                }
                Debug.Log($"碰撞体数据加载成功，包含 {colliderModel.MjRoots?.Count ?? 0} 个 MjRoot，共 {totalMeshes} 个网格");
                return colliderModel;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            return null;
        }
    }

    /// <summary>
    /// 将Mesh转换为ColliderMeshData
    /// </summary>
    public static ColliderMeshData MeshToColliderMeshData(Mesh mesh, bool isVHACD = false)
    {
        var data = new ColliderMeshData
        {
            Name = mesh.name,
            IsVHACD = isVHACD
        };

        // 序列化顶点
        var vertices = mesh.vertices;
        var vertexList = new List<float>();
        foreach (var v in vertices)
        {
            vertexList.Add(v.x);
            vertexList.Add(v.y);
            vertexList.Add(v.z);
        }
        data.Vertices = string.Join(",", vertexList.ConvertAll(v => v.ToString()).ToArray());

        // 序列化三角形
        var triangles = mesh.triangles;
        data.Triangles = string.Join(",", Array.ConvertAll(triangles, t => t.ToString()));

        // 序列化法线
        var normals = mesh.normals;
        var normalList = new List<float>();
        foreach (var n in normals)
        {
            normalList.Add(n.x);
            normalList.Add(n.y);
            normalList.Add(n.z);
        }
        data.Normals = string.Join(",", normalList.ConvertAll(n => n.ToString()).ToArray());

        // 序列化UV
        var uvs = mesh.uv;
        var uvList = new List<float>();
        foreach (var u in uvs)
        {
            uvList.Add(u.x);
            uvList.Add(u.y);
        }
        data.UVs = string.Join(",", uvList.ConvertAll(u => u.ToString()).ToArray());

        return data;
    }

    /// <summary>
    /// 将ColliderMeshData转换为Mesh
    /// </summary>
    public static Mesh ColliderMeshDataToMesh(ColliderMeshData data)
    {
        var mesh = new Mesh
        {
            name = data.Name
        };

        // 反序列化顶点
        var vertexStrings = data.Vertices.Split(',');
        var vertices = new Vector3[vertexStrings.Length / 3];
        for (int i = 0; i < vertexStrings.Length; i += 3)
        {
            vertices[i / 3] = new Vector3(
                float.Parse(vertexStrings[i]),
                float.Parse(vertexStrings[i + 1]),
                float.Parse(vertexStrings[i + 2])
            );
        }
        mesh.vertices = vertices;

        // 反序列化三角形
        var triangleStrings = data.Triangles.Split(',');
        var triangles = Array.ConvertAll(triangleStrings, t => int.Parse(t));
        mesh.triangles = triangles;

        // 反序列化法线
        if (!string.IsNullOrEmpty(data.Normals))
        {
            var normalStrings = data.Normals.Split(',');
            var normals = new Vector3[normalStrings.Length / 3];
            for (int i = 0; i < normalStrings.Length; i += 3)
            {
                normals[i / 3] = new Vector3(
                    float.Parse(normalStrings[i]),
                    float.Parse(normalStrings[i + 1]),
                    float.Parse(normalStrings[i + 2])
                );
            }
            mesh.normals = normals;
        }

        // 反序列化UV
        if (!string.IsNullOrEmpty(data.UVs))
        {
            var uvStrings = data.UVs.Split(',');
            var uvs = new Vector2[uvStrings.Length / 2];
            for (int i = 0; i < uvStrings.Length; i += 2)
            {
                uvs[i / 2] = new Vector2(
                    float.Parse(uvStrings[i]),
                    float.Parse(uvStrings[i + 1])
                );
            }
            mesh.uv = uvs;
        }

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        return mesh;
    }
}
