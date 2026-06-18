using UnityEngine;

public class CameraTool
{
    public static Camera GetActiveCamera()
    {
        if (Camera.current != null)
        {
            //Debug.Log($"Active camera: {Camera.current.name}");
            return Camera.current;
        }
        if (Camera.main != null)
        {
            //Debug.Log($"Active camera: {Camera.main.name}");
            return Camera.main;
        }
        Camera[] cameras = Camera.allCameras;
        foreach (Camera cam in cameras)
        {
            if (cam.enabled)
            {
                //Debug.Log($"Active camera: {cam.name}");
                return cam;
            }
        }
        return null;
    }
}