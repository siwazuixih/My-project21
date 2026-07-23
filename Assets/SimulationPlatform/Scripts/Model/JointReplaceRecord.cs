using System;
using System.Xml.Serialization;
using UnityEngine;

[Serializable]
public class JointReplaceRecord
{
    [XmlAttribute("JointId")]
    public string JointId;

    [XmlAttribute("ReplacedObjectName")]
    public string ReplacedObjectName;

    [XmlAttribute("RelativePath")]
    public string RelativePath;

    [XmlAttribute("HierarchyIndices")]
    public string HierarchyIndices;

    [XmlElement("Position")]
    public Vector3D Position;

    [XmlElement("Rotation")]
    public QuaternionD Rotation;

    [XmlElement("WorldPosition")]
    public Vector3D WorldPosition;

    public JointReplaceRecord()
    {
    }

    public JointReplaceRecord(string jointId, string replacedObjectName, string relativePath, string hierarchyIndices, Vector3 position, Quaternion rotation, Vector3 worldPosition)
    {
        JointId = jointId;
        ReplacedObjectName = replacedObjectName;
        RelativePath = relativePath;
        HierarchyIndices = hierarchyIndices;
        Position = new Vector3D(position);
        Rotation = new QuaternionD(rotation);
        WorldPosition = new Vector3D(worldPosition);
    }

    public GameObject FindReplacedObject(GameObject root)
    {
        if (root == null || string.IsNullOrEmpty(HierarchyIndices))
        {
            return null;
        }

        string[] indices = HierarchyIndices.Split('/');
        Transform current = root.transform;

        foreach (string indexStr in indices)
        {
            if (!int.TryParse(indexStr, out int index))
            {
                return null;
            }

            if (index < 0 || index >= current.childCount)
            {
                return null;
            }

            current = current.GetChild(index);
        }

        return current.gameObject;
    }

    public static string CalculateRelativePath(Transform obj, Transform root)
    {
        if (obj == null || root == null)
        {
            return string.Empty;
        }

        if (obj == root)
        {
            return string.Empty;
        }

        System.Text.StringBuilder pathBuilder = new System.Text.StringBuilder();
        Transform current = obj;

        while (current != null && current != root)
        {
            if (pathBuilder.Length > 0)
            {
                pathBuilder.Insert(0, "/");
            }
            pathBuilder.Insert(0, current.name);
            current = current.parent;
        }

        if (current != root)
        {
            return string.Empty;
        }

        return pathBuilder.ToString();
    }

    public static string CalculateHierarchyIndices(Transform obj, Transform root)
    {
        if (obj == null || root == null)
        {
            return string.Empty;
        }

        if (obj == root)
        {
            return string.Empty;
        }

        System.Text.StringBuilder indicesBuilder = new System.Text.StringBuilder();
        Transform current = obj;

        while (current != null && current != root)
        {
            if (indicesBuilder.Length > 0)
            {
                indicesBuilder.Insert(0, "/");
            }

            if (current.parent != null)
            {
                int index = current.GetSiblingIndex();
                indicesBuilder.Insert(0, index.ToString());
            }

            current = current.parent;
        }

        if (current != root)
        {
            return string.Empty;
        }

        return indicesBuilder.ToString();
    }
}
