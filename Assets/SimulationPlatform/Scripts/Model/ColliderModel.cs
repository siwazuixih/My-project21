using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[Serializable]
public class ColliderModel
{
    [XmlAttribute("Id")]
    public string Id { get; set; }

    [XmlAttribute("SceneId")]
    public string SceneId { get; set; }

    [XmlAttribute("Name")]
    public string Name { get; set; }

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