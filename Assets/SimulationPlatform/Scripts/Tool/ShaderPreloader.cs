using UnityEngine;

public static class ShaderPreloader
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Preload()
    {
        // 强制让Unity打包这些Shader，不被裁剪
        Shader.Find("glTF/PbrMetallicRoughness");
        Shader.Find("glTF/Unlit");
        Shader.Find("glTF/Transmission");
    }
}