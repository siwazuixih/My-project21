using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[Serializable]
public class ColliderModel
{
    [XmlAttribute("FormatVersion")]
    // 旧XML没有此属性，因此默认必须是1；新提取流程会显式写成2。
    public int FormatVersion { get; set; } = 1;

    [XmlAttribute("Id")]
    public string Id { get; set; }

    [XmlAttribute("SceneId")]
    public string SceneId { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; }

    [XmlAttribute("ProjectId")]
    public string ProjectId { get; set; }

    [XmlElement("MjRoot")]
    public List<ColliderMjRootData> MjRoots { get; set; } = new List<ColliderMjRootData>();
}

[Serializable]
public class ColliderMjRootData
{
    [XmlAttribute("Name")]
    public string Name { get; set; }

    [XmlAttribute("ParentPath")]
    public string ParentPath { get; set; }

    // 从模型根节点开始记录每一级Transform的SiblingIndex，例如"0/2/5"。
    // GLB中存在大量同名节点，名称路径只能用于日志和兼容旧文件，不能唯一定位。
    [XmlAttribute("ParentIndexPath")]
    public string ParentIndexPath { get; set; }

    [XmlElement("MeshData")]
    public List<ColliderMeshData> Meshes { get; set; } = new List<ColliderMeshData>();
}

[Serializable]
public class ColliderMeshData
{
    [XmlAttribute("Name")]
    public string Name { get; set; }

    [XmlElement("Vertices")]
    public string Vertices { get; set; }

    [XmlElement("Triangles")]
    public string Triangles { get; set; }

    [XmlElement("Normals")]
    public string Normals { get; set; }

    [XmlElement("UVs")]
    public string UVs { get; set; }

    [XmlAttribute("IsVHACD")]
    public bool IsVHACD { get; set; }
}

public sealed class ColliderApplyReport
{
    public int RequestedRootCount { get; set; }
    public int RequestedMeshCount { get; set; }
    public int CreatedRootCount { get; set; }
    public int CreatedMeshCount { get; set; }
    public int BoundMujocoGeomCount { get; set; }
    public int MissingParentCount { get; set; }
    public int AmbiguousLegacyPathCount { get; set; }
    public bool MujocoRebuildObserved { get; set; }
    public bool MujocoRebuildTimedOut { get; set; }

    public bool IsSuccessful =>
        CreatedMeshCount > 0 &&
        CreatedMeshCount == RequestedMeshCount &&
        MissingParentCount == 0 &&
        AmbiguousLegacyPathCount == 0 &&
        MujocoRebuildObserved &&
        BoundMujocoGeomCount == CreatedMeshCount &&
        !MujocoRebuildTimedOut;
}
