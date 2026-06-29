using System;
using System.IO;
using UnityEngine;


public static class PathTool
{
    /// <summary>
    /// 获取打包后可执行文件的同目录路径（跨平台兼容）
    /// </summary>
    /// <returns>可执行文件同目录路径（末尾带路径分隔符）</returns>
    public static string GetExecutableDirPath()
    {
        string exeDirPath;

#if UNITY_EDITOR
        // 编辑器中使用 Unity 项目根目录，避免误写到 Unity Editor 安装目录。
        exeDirPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
        string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
        exeDirPath = Path.GetDirectoryName(exePath);
#elif UNITY_STANDALONE_OSX
        // Mac 打包后是 .app 包，返回 .app 所在目录。
        exeDirPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
#else
        exeDirPath = Application.persistentDataPath;
#endif

        if (!exeDirPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            exeDirPath += Path.DirectorySeparatorChar;
        }

        if (!Directory.Exists(exeDirPath))
        {
            Directory.CreateDirectory(exeDirPath);
        }

        Debug.Log($"可执行文件同目录路径：{exeDirPath}");
        return exeDirPath;
    }

    /// <summary>
    /// 将相对路径、绝对路径或 file URI 转换为可供 System.IO 使用的物理路径。
    /// </summary>
    public static string ResolvePhysicalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmedPath = path.Trim().Trim('"');
        if (Uri.TryCreate(trimmedPath, UriKind.Absolute, out Uri uri) && uri.IsFile)
        {
            return Path.GetFullPath(uri.LocalPath);
        }

        if (Path.IsPathRooted(trimmedPath))
        {
            return Path.GetFullPath(trimmedPath);
        }

        return Path.GetFullPath(Path.Combine(GetExecutableDirPath(), trimmedPath));
    }

    /// <summary>
    /// 将本地模型路径转换为 glTFast 可读取的 file URI。
    /// </summary>
    public static string ToFileUri(string path)
    {
        string physicalPath = ResolvePhysicalPath(path);
        return string.IsNullOrEmpty(physicalPath) ? string.Empty : new Uri(physicalPath).AbsoluteUri;
    }

    /// <summary>
    /// 返回相对于应用根目录的路径，供 XML 配置持久化使用。
    /// </summary>
    public static string GetRelativePathFromExecutableDir(string path)
    {
        string rootPath = GetExecutableDirPath();
        string physicalPath = ResolvePhysicalPath(path);
        return Path.GetRelativePath(rootPath, physicalPath);
    }
}
