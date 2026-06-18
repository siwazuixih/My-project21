using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

public static class XmlConfigTool
{    /// <summary>
     /// 序列化：将C#对象写入XML文件
     /// </summary>
     /// <typeparam name="T">对象类型</typeparam>
     /// <param name="obj">要序列化的对象</param>
     /// <param name="xmlFileName">XML文件名（含后缀，如"PlayerConfig.xml"）</param>
     /// <returns>是否写入成功</returns>
    public static bool SerializeToXml<T>(T obj, string dir, string xmlFileName)
    {
        if (obj == null)
        {
            Debug.LogError("XML序列化失败：要序列化的对象为null！");
            return false;
        }

        try
        {
            // 1. 获取XML文件的完整路径（Unity可读写路径：persistentDataPath）
            string xmlFullPath = Path.Combine(PathTool.GetExecutableDirPath(), "Data", dir, xmlFileName);

            // 2. 确保目标文件夹存在
            string xmlDir = Path.GetDirectoryName(xmlFullPath);
            if (!Directory.Exists(xmlDir))
            {
                Directory.CreateDirectory(xmlDir);
                Debug.Log($"创建了目标文件夹: {xmlDir}");
            }

            // 3. 创建XmlSerializer（指定要序列化的对象类型）
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            // 4. 写入文件（使用StreamWriter，自动处理编码）
            using (StreamWriter writer = new StreamWriter(xmlFullPath, false, System.Text.Encoding.UTF8))
            {
                serializer.Serialize(writer, obj);
            }

            Debug.Log($"XML序列化成功！文件路径：{xmlFullPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"XML序列化失败：{e.Message}\n{e.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// 反序列化：从XML文件读取数据并转为C#对象
    /// </summary>
    /// <typeparam name="T">要转换的对象类型</typeparam>
    /// <param name="xmlFileName">XML文件名（含后缀，如"PlayerConfig.xml"）</param>
    /// <returns>转换后的C#对象（失败返回null）</returns>
    public static T DeserializeFromXml<T>(string dir, string xmlFileName) where T : class
    {
        try
        {
            // 1. 获取XML文件的完整路径
            string xmlFullPath = Path.Combine(PathTool.GetExecutableDirPath(), "Data", dir, xmlFileName);

            // 2. 检查文件是否存在
            if (!File.Exists(xmlFullPath))
            {
                Debug.LogError($"XML反序列化失败：文件不存在！路径：{xmlFullPath}");
                return null;
            }

            // 3. 创建XmlSerializer
            XmlSerializer serializer = new XmlSerializer(typeof(T));

            // 4. 读取文件并反序列化
            using (StreamReader reader = new StreamReader(xmlFullPath, System.Text.Encoding.UTF8))
            {
                T result = serializer.Deserialize(reader) as T;
                Debug.Log("XML反序列化成功！");
                return result;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"XML反序列化失败：{e.Message}\n{e.StackTrace}");
            return null;
        }
    }
}
