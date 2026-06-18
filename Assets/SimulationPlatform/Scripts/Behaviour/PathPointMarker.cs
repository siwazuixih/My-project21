using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PathPointMarker : MonoBehaviour
{
    [Header("UI")]
    public Image background;
    public TextMeshProUGUI numberText;

    [Header("设置")]
    public float pixelSize = 48f;

    private Transform _target;
    private Camera _cam;
    private RectTransform _rectTransform;
    private Transform _canvasTransform;

    void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
        _canvasTransform = GetComponentInParent<Canvas>()?.transform;
        
        // 如果没有父 Canvas，添加一个独立的 Canvas
        if (_canvasTransform == null)
        {
            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            
            Camera mainCamera = CameraTool.GetActiveCamera();
            if (mainCamera != null)
            {
                canvas.worldCamera = mainCamera;
            }
            
            canvas.sortingOrder = 10;
            
            _canvasTransform = canvas.transform;
            
            // 设置 RectTransform 尺寸
            _rectTransform.sizeDelta = new Vector2(pixelSize, pixelSize);
            transform.localScale = Vector3.one;
        }
    }

    void LateUpdate()
    {
        if (_target == null) return;

        _cam = CameraTool.GetActiveCamera();
        if (_cam == null) return;

        UpdatePosition();
    }

    public void Init(Transform target, int index)
    {
        _target = target;
        numberText.text = index.ToString();

        Debug.Log("position: " + _target.position);
        Debug.Log("localPosition: " + _target.localPosition);
        //ApplyPixelSize();
        
        _cam = CameraTool.GetActiveCamera();
        if (_canvasTransform == null)
            _canvasTransform = GetComponentInParent<Canvas>()?.transform;
        
        if (_cam != null && _target != null)
        {
            UpdatePosition();
        }
    }

    private void UpdatePosition()
    {
        if (_target == null) return;

        // 直接设置世界坐标
        transform.position = _target.position;
        this.transform.LookAt(
            this.transform.position + _cam.transform.rotation * Vector3.forward,
            _cam.transform.rotation * Vector3.up
        );
    }

    private void ApplyPixelSize()
    {
        if (_rectTransform == null) return;

        Canvas parentCanvas = GetComponentInParent<Canvas>();
        
        if (parentCanvas != null && parentCanvas.renderMode == RenderMode.WorldSpace)
        {
            CanvasScaler scaler = parentCanvas.GetComponent<CanvasScaler>();
            float pixelsPerUnit = scaler != null ? scaler.referencePixelsPerUnit : 100f;
            
            Vector3 parentScale = parentCanvas.transform.lossyScale;
            float scaleFactor = parentScale.x > 0.0001f ? 1f / parentScale.x : 1f;
            
            float worldSize = (pixelSize / pixelsPerUnit) * scaleFactor;
            worldSize = Mathf.Clamp(worldSize, 0.1f, 2f);
            _rectTransform.sizeDelta = new Vector2(worldSize, worldSize);
            
            // 使用缩放来确保在 World Space Canvas 中正确显示
            transform.localScale = new Vector3(scaleFactor, scaleFactor, scaleFactor);
        }
        else
        {
            _rectTransform.sizeDelta = new Vector2(pixelSize, pixelSize);
            transform.localScale = Vector3.one;
        }
    }

    public void SetIndex(int index)
    {
        numberText.text = index.ToString();
    }
}
