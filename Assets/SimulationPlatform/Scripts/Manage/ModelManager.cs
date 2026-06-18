using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ModelManager
{
    public static XmlModel XmlModel;
    
    /// <summary>
    /// 加载模型配置
    /// </summary>
    public static void Load()
    {
        try
        {
            XmlModel = XmlConfigTool.DeserializeFromXml<XmlModel>("Configs", "models.xml");
            if (XmlModel == null)
            {
                XmlModel = new XmlModel();
                Debug.Log("创建了新的XmlModel对象");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            XmlModel = new XmlModel();
        }
    }
    
    /// <summary>
    /// 保存模型配置
    /// </summary>
    public static void Save()
    {
        try
        {
            XmlConfigTool.SerializeToXml<XmlModel>(XmlModel, "Configs", "models.xml");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
    
    /// <summary>
    /// 添加关节模型
    /// </summary>
    /// <param name="jointModel">关节模型</param>
    public static void AddJoint(JointModel jointModel)
    {
        if (XmlModel == null)
        {
            XmlModel = new XmlModel();
        }
        
        XmlModel.Joints.Add(jointModel);
    }
    
    /// <summary>
    /// 添加场景模型
    /// </summary>
    /// <param name="sceneModel">场景模型</param>
    public static void AddScene(SceneModel sceneModel)
    {
        if (XmlModel == null)
        {
            XmlModel = new XmlModel();
        }
        
        XmlModel.Scenes.Add(sceneModel);
    }
    
    /// <summary>
    /// 通过ID删除关节模型
    /// </summary>
    /// <param name="id">关节模型ID</param>
    /// <returns>是否删除成功</returns>
    public static bool RemoveJoint(string id)
    {
        if (XmlModel == null)
        {
            return false;
        }
        
        int count = XmlModel.Joints.RemoveAll(joint => joint.Id == id);
        return count > 0;
    }
    
    /// <summary>
    /// 通过ID删除场景模型
    /// </summary>
    /// <param name="id">场景模型ID</param>
    /// <returns>是否删除成功</returns>
    public static bool RemoveScene(string id)
    {
        if (XmlModel == null)
        {
            return false;
        }
        
        int count = XmlModel.Scenes.RemoveAll(scene => scene.Id == id);
        return count > 0;
    }
    
    /// <summary>
    /// 通过ID获取关节模型
    /// </summary>
    /// <param name="id">关节模型ID</param>
    /// <returns>关节模型</returns>
    public static JointModel GetJoint(string id)
    {
        if (XmlModel == null)
        {
            return null;
        }
        
        return XmlModel.Joints.Find(joint => joint.Id == id);
    }
    
    /// <summary>
    /// 通过ID获取场景模型
    /// </summary>
    /// <param name="id">场景模型ID</param>
    /// <returns>场景模型</returns>
    public static SceneModel GetScene(string id)
    {
        if (XmlModel == null)
        {
            return null;
        }
        
        return XmlModel.Scenes.Find(scene => scene.Id == id);
    }
}