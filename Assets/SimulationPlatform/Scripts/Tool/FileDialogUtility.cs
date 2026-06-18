using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

public static class FileDialogUtility
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct OpenFileName
    {
        public int structSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string filter;
        public string customFilter;
        public int maxCustFilter;
        public int filterIndex;
        public IntPtr file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defExt;
        public IntPtr custData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPtr;
        public int reservedInt;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool GetOpenFileName(ref OpenFileName ofn);

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    private static string selectPath;

    public static string OpenFileDialog(string title, string filter)
    {
        if (Application.platform == RuntimePlatform.WindowsPlayer || 
            Application.platform == RuntimePlatform.WindowsEditor)
        {
            return OpenFileDialogWindows(title, filter);
        }
        else if (Application.platform == RuntimePlatform.LinuxPlayer ||
                 Application.platform == RuntimePlatform.LinuxEditor)
        {
            return OpenFileDialogLinux(title, filter);
        }
        else
        {
            UnityEngine.Debug.LogError($"不支持的平台: {Application.platform}");
            return null;
        }
    }

    private static string OpenFileDialogWindows(string title, string filter)
    {
        OpenFileName ofn = new OpenFileName();
        ofn.structSize = Marshal.SizeOf(ofn);
        ofn.hwndOwner = GetActiveWindow();
        ofn.filter = filter;

        int bufferSize = 4096;
        ofn.file = Marshal.AllocHGlobal(bufferSize);
        ofn.maxFile = bufferSize;

        ofn.fileTitle = new string(new char[512]);
        ofn.maxFileTitle = ofn.fileTitle.Length;

        string originalDir = Directory.GetCurrentDirectory();
        ofn.initialDir = selectPath ?? originalDir;
        ofn.title = title;

        ofn.flags = 0x00080000  // OFN_EXPLORER
                  | 0x00000800  // OFN_NOCHANGEDIR
                  | 0x00000200  // OFN_HIDEREADONLY
                  | 0x00000001; // OFN_FILEMUSTEXIST
        //Directory.SetCurrentDirectory(selectPath??originalDir);

        bool success = GetOpenFileName(ref ofn);
        Directory.SetCurrentDirectory(ofn.initialDir);
        Directory.SetCurrentDirectory(originalDir);
        if (!success)
        {
            int errorCode = Marshal.GetLastWin32Error();
            UnityEngine.Debug.LogError($"GetOpenFileName{errorCode}");
            return null;
        }
        var file = Marshal.PtrToStringAuto(ofn.file);
        Marshal.FreeHGlobal(ofn.file);
        selectPath = Path.GetDirectoryName(file);
        return file;
    }

    private static string OpenFileDialogLinux(string title, string filter)
    {
        string zenityPath = FindExecutable("zenity");
        string kdialogPath = FindExecutable("kdialog");

        if (!string.IsNullOrEmpty(zenityPath))
        {
            return OpenFileDialogWithZenity(title, filter, zenityPath);
        }
        else if (!string.IsNullOrEmpty(kdialogPath))
        {
            return OpenFileDialogWithKDialog(title, filter, kdialogPath);
        }
        else
        {
            UnityEngine.Debug.LogError("Linux下未找到zenity或kdialog，请确保系统已安装图形化文件选择工具");
            return null;
        }
    }

    private static string FindExecutable(string name)
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("which", name);
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    return process.StandardOutput.ReadToEnd().Trim();
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"查找{name}失败: {ex.Message}");
        }
        return null;
    }

    private static string OpenFileDialogWithZenity(string title, string filter, string zenityPath)
    {
        try
        {
            string fileFilter = ParseFilterForZenity(filter);
            string initialDir = selectPath ?? Directory.GetCurrentDirectory();

            ProcessStartInfo psi = new ProcessStartInfo(zenityPath)
            {
                Arguments = $"--file-selection --title=\"{EscapeZenityString(title)}\" --file-filter=\"{fileFilter}\" --filename=\"{initialDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(result))
                    {
                        selectPath = Path.GetDirectoryName(result);
                        return result;
                    }
                }
                else
                {
                    string error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(error) && !error.Contains("No"))
                    {
                        UnityEngine.Debug.LogError($"zenity错误: {error}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"zenity调用失败: {ex.Message}");
        }
        return null;
    }

    private static string OpenFileDialogWithKDialog(string title, string filter, string kdialogPath)
    {
        try
        {
            string fileFilter = ParseFilterForKDialog(filter);
            string initialDir = selectPath ?? Directory.GetCurrentDirectory();

            ProcessStartInfo psi = new ProcessStartInfo(kdialogPath)
            {
                Arguments = $"--getopenfilename \"{EscapeKDialogString(title)}\" \"{initialDir}\" \"{fileFilter}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(psi))
            {
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    string result = process.StandardOutput.ReadToEnd().Trim();
                    if (!string.IsNullOrEmpty(result))
                    {
                        selectPath = Path.GetDirectoryName(result);
                        return result;
                    }
                }
                else
                {
                    string error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(error) && !error.Contains("cancelled"))
                    {
                        UnityEngine.Debug.LogError($"kdialog错误: {error}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError($"kdialog调用失败: {ex.Message}");
        }
        return null;
    }

    private static string ParseFilterForZenity(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return "*.*";
        }

        string[] filterParts = filter.Split(new[] { "\0" }, StringSplitOptions.RemoveEmptyEntries);
        if (filterParts.Length >= 2)
        {
            return filterParts[1].Replace(";", " ");
        }
        return "*.*";
    }

    private static string ParseFilterForKDialog(string filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return "*.*|所有文件";
        }

        string[] filterParts = filter.Split(new[] { "\0" }, StringSplitOptions.RemoveEmptyEntries);
        if (filterParts.Length >= 2)
        {
            return $"{filterParts[1]}|{filterParts[0].Split('(')[0].Trim()}";
        }
        return "*.*|所有文件";
    }

    private static string EscapeZenityString(string str)
    {
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");
    }

    private static string EscapeKDialogString(string str)
    {
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}