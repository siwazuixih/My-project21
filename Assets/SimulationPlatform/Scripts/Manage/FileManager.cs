using System;
using System.IO;
using UnityEngine;

public static class FileManager
{
    /// <summary>
    /// 将文件复制到项目打包后的Files文件夹下的指定子文件夹中
    /// </summary>
    /// <param name="folderName">目标子文件夹名称</param>
    /// <param name="sourceFilePath">源文件路径</param>
    /// <returns>复制后的文件路径，失败返回空字符串</returns>
    public static string CopyFileToProjectFiles(string folderName, string sourceFilePath)
    {
        try
        {
            sourceFilePath = PathTool.ResolvePhysicalPath(sourceFilePath);

            // 1. 验证参数
            if (string.IsNullOrEmpty(folderName))
            {
                Debug.LogError("文件夹名称不能为空");
                return string.Empty;
            }
            
            if (string.IsNullOrEmpty(sourceFilePath))
            {
                Debug.LogError("源文件路径不能为空");
                return string.Empty;
            }
            
            if (!File.Exists(sourceFilePath))
            {
                Debug.LogError($"源文件不存在: {sourceFilePath}");
                return string.Empty;
            }
            
            // 2. 构建目标文件路径
            string executableDir = PathTool.GetExecutableDirPath();
            string filesDir = Path.Combine(executableDir, "Files");
            string targetFolder = Path.Combine(filesDir, folderName);
            
            // 生成带时间戳的文件名
            string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
            string extension = Path.GetExtension(sourceFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            string newFileName = $"{fileName}_{timestamp}{extension}";
            
            string targetFilePath = Path.Combine(targetFolder, newFileName);
            
            // 3. 创建目标目录
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
                Debug.Log($"创建了目标文件夹: {targetFolder}");
            }
            
            // 4. 复制文件
            File.Copy(sourceFilePath, targetFilePath, true);
            Debug.Log($"文件复制成功: 从 {sourceFilePath} 到 {targetFilePath}");
            
            return targetFilePath;
        }
        catch (Exception e)
        {
            Debug.LogError($"文件复制失败: {e.Message}\n{e.StackTrace}");
            return string.Empty;
        }
    }
    
    /// <summary>
    /// 获取项目Files文件夹的路径
    /// </summary>
    /// <returns>Files文件夹路径</returns>
    public static string GetFilesDirectoryPath()
    {
        string executableDir = PathTool.GetExecutableDirPath();
        string filesDir = Path.Combine(executableDir, "Files");
        
        // 确保Files目录存在
        if (!Directory.Exists(filesDir))
        {
            Directory.CreateDirectory(filesDir);
            Debug.Log($"创建了Files文件夹: {filesDir}");
        }
        
        return filesDir;
    }
    
    /// <summary>
    /// 获取指定子文件夹的路径
    /// </summary>
    /// <param name="folderName">子文件夹名称</param>
    /// <returns>子文件夹路径</returns>
    public static string GetSubDirectoryPath(string folderName)
    {
        if (string.IsNullOrEmpty(folderName))
        {
            Debug.LogError("文件夹名称不能为空");
            return string.Empty;
        }
        
        string filesDir = GetFilesDirectoryPath();
        string subDir = Path.Combine(filesDir, folderName);
        
        // 确保子目录存在
        if (!Directory.Exists(subDir))
        {
            Directory.CreateDirectory(subDir);
            Debug.Log($"创建了子文件夹: {subDir}");
        }
        
        return subDir;
    }
}
