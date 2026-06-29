using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class ProjectManager
{
    public static XmlProject XmlProject;
    
    /// <summary>
    /// 加载项目配置
    /// </summary>
    public static void Load()
    {
        try
        {
            XmlProject = XmlConfigTool.DeserializeFromXml<XmlProject>("Configs", "projects.xml");
            if (XmlProject == null)
            {
                XmlProject = new XmlProject();
                Debug.Log("创建了新的XmlProject对象");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            XmlProject = new XmlProject();
        }
    }
    
    /// <summary>
    /// 保存项目配置
    /// </summary>
    public static void Save()
    {
        try
        {
            XmlConfigTool.SerializeToXml<XmlProject>(XmlProject, "Configs", "projects.xml");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
    
    /// <summary>
    /// 添加项目
    /// </summary>
    /// <param name="project">项目</param>
    public static void AddProject(Project project)
    {
        if (XmlProject == null)
        {
            XmlProject = new XmlProject();
        }
        
        XmlProject.Projects.Add(project);
    }
    
    /// <summary>
    /// 通过ID删除场景项目
    /// </summary>
    /// <param name="id">场景项目ID</param>
    /// <returns>是否删除成功</returns>
    public static bool RemoveProject(string id)
    {
        if (XmlProject == null)
        {
            return false;
        }
        
        int count = XmlProject.Projects.RemoveAll(project => project.Id == id);
        return count > 0;
    }
    
    /// <summary>
    /// 通过ID获取场景项目
    /// </summary>
    /// <param name="id">场景项目ID</param>
    /// <returns>场景项目</returns>
    public static Project GetProject(string id)
    {
        if (XmlProject == null)
        {
            return null;
        }
        
        return XmlProject.Projects.Find(project => project.Id == id);
    }
}