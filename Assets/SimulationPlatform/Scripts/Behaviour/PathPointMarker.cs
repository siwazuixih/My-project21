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

        transform.position = _target.position;
        this.transform.LookAt(
            this.transform.position + _cam.transform.rotation * Vector3.forward,
            _cam.transform.rotation * Vector3.up
        );
    }

    public void SetIndex(int index)
    {
        numberText.text = index.ToString();
    }
}
