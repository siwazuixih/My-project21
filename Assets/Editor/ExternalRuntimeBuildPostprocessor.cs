using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class ExternalRuntimeBuildPostprocessor :
    IPostprocessBuildWithReport
{
    public int callbackOrder => 100;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneLinux64)
        {
            Debug.LogWarning(
                "[Build] External Python runtime files are only prepared "
                + "for Linux standalone builds."
            );
            return;
        }

        string projectRoot =
            Directory.GetParent(Application.dataPath)?.FullName;
        string buildRoot =
            Path.GetDirectoryName(report.summary.outputPath);
        if (
            string.IsNullOrEmpty(projectRoot)
            || string.IsNullOrEmpty(buildRoot)
        )
        {
            throw new BuildFailedException(
                "无法确定 Unity 项目目录或软件输出目录。"
            );
        }

        string sourceExternalDirectory =
            Path.Combine(projectRoot, "ExternalCode");
        string targetExternalDirectory =
            Path.Combine(buildRoot, "ExternalCode");
        if (!Directory.Exists(sourceExternalDirectory))
        {
            throw new BuildFailedException(
                "缺少 ExternalCode，无法打包相机和拧紧程序。"
            );
        }

        Directory.CreateDirectory(targetExternalDirectory);
        string[] pythonFiles =
            Directory.GetFiles(sourceExternalDirectory, "*.py");
        if (pythonFiles.Length == 0)
        {
            throw new BuildFailedException(
                "ExternalCode 中没有可打包的 Python 程序。"
            );
        }

        foreach (string sourceFile in pythonFiles)
        {
            string targetFile = Path.Combine(
                targetExternalDirectory,
                Path.GetFileName(sourceFile)
            );
            File.Copy(sourceFile, targetFile, true);
        }

        int copiedModelCount = CopyOptionalVisionModels(
            sourceExternalDirectory,
            targetExternalDirectory
        );

        string sourceFont =
            Path.Combine(projectRoot, "Assets", "微软雅黑.ttf");
        if (!File.Exists(sourceFont))
        {
            throw new BuildFailedException(
                "缺少 Assets/微软雅黑.ttf，无法打包中文曲线字体。"
            );
        }

        string targetFontDirectory =
            Path.Combine(buildRoot, "Assets");
        Directory.CreateDirectory(targetFontDirectory);
        File.Copy(
            sourceFont,
            Path.Combine(targetFontDirectory, "微软雅黑.ttf"),
            true
        );

        Debug.Log(
            "[Build] 已复制 ExternalCode Python 程序、"
            + copiedModelCount
            + " 个视觉模型文件和中文曲线字体到: "
            + buildRoot
        );
    }

    private static int CopyOptionalVisionModels(
        string sourceExternalDirectory,
        string targetExternalDirectory
    )
    {
        string sourceModelDirectory =
            Path.Combine(sourceExternalDirectory, "models");
        if (!Directory.Exists(sourceModelDirectory))
        {
            Debug.LogWarning(
                "[Build] ExternalCode/models 不存在；视觉服务将以原图降级模式运行。"
            );
            return 0;
        }

        string targetModelDirectory =
            Path.Combine(targetExternalDirectory, "models");
        string[] modelFiles = Directory.GetFiles(
            sourceModelDirectory,
            "*.pt",
            SearchOption.AllDirectories
        );
        foreach (string sourceFile in modelFiles)
        {
            string relativePath = sourceFile
                .Substring(sourceModelDirectory.Length)
                .TrimStart(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                );
            string targetFile = Path.Combine(
                targetModelDirectory,
                relativePath
            );
            string targetDirectory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            File.Copy(sourceFile, targetFile, true);
        }

        if (modelFiles.Length == 0)
        {
            Debug.LogWarning(
                "[Build] ExternalCode/models 中没有 .pt 文件；"
                + "视觉服务将以原图降级模式运行。"
            );
        }
        return modelFiles.Length;
    }
}
