using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;

/// <summary>
/// 仿 Unity 编辑器风格 XYZ 数值控件
/// 支持：鼠标拖动标签调整 + 直接输入数字 + 全局事件通知
/// </summary>
public class EditorStyleVector3Control : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    [Header("拖动设置")]
    public float dragSensitivity = 0.1f;    // 拖动灵敏度
    public float acceleration = 2f;        // 加速
    public int decimalDigits = 2;           // 小数位数

    [Header("绑定UI")]
    public RectTransform labelX;
    public RectTransform labelY;
    public RectTransform labelZ;
    public TMP_InputField inputX;
    public TMP_InputField inputY;
    public TMP_InputField inputZ;

    // 全局输出数值
    public Vector3 Value { get; private set; }

    // 全局事件：任何修改都会触发（所有脚本可监听）
    public delegate void ValueChanged(Vector3 value);
    public event ValueChanged OnValueChanged;

    // 内部状态
    private bool _isDragging;
    private RectTransform _currentDragLabel;
    private Vector2 _startMousePos;
    private float _startValue;
    private float _dragSpeed;

    void Awake()
    {
        // 输入框改变时同步
        inputX.onValueChanged.AddListener(v => OnInputChanged(0, v));
        inputY.onValueChanged.AddListener(v => OnInputChanged(1, v));
        inputZ.onValueChanged.AddListener(v => OnInputChanged(2, v));
    }

    /// <summary>
    /// 外部设置值（比如从存档/角色同步）
    /// </summary>
    public void SetValue(Vector3 value, bool notify = true)
    {
        Value = value;
        RefreshInputFields();
        if (notify) OnValueChanged?.Invoke(Value);
    }

    /// <summary>
    /// 刷新显示文本
    /// </summary>
    void RefreshInputFields()
    {
        inputX.text = Value.x.ToString("F" + decimalDigits);
        inputY.text = Value.y.ToString("F" + decimalDigits);
        inputZ.text = Value.z.ToString("F" + decimalDigits);
    }

    /// <summary>
    /// 输入框手动输入时
    /// </summary>
    void OnInputChanged(int axis, string text)
    {
        if (float.TryParse(text, out float f))
        {
            Value = SetAxis(Value, axis, f);
            OnValueChanged?.Invoke(Value);
        }
    }

    /// <summary>
    /// 鼠标按下标签
    /// </summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        var rt = eventData.pointerCurrentRaycast.gameObject.GetComponent<RectTransform>();
        if (rt == labelX || rt == labelY || rt == labelZ)
        {
            _isDragging = true;
            _currentDragLabel = rt;
            _startMousePos = eventData.position;
            _dragSpeed = 0;

            if (rt == labelX) _startValue = Value.x;
            else if (rt == labelY) _startValue = Value.y;
            else _startValue = Value.z;
        }
    }

    /// <summary>
    /// 拖动
    /// </summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!_isDragging) return;

        float delta = (eventData.position - _startMousePos).x;
        _dragSpeed = Mathf.Lerp(_dragSpeed, delta * dragSensitivity, Time.deltaTime * acceleration);

        float newValue = _startValue + _dragSpeed;
        int axis = GetCurrentAxis();
        Value = SetAxis(Value, axis, newValue);

        RefreshInputFields();
        OnValueChanged?.Invoke(Value);
    }

    public void OnPointerUp(PointerEventData eventData) => _isDragging = false;

    int GetCurrentAxis()
    {
        if (_currentDragLabel == labelX) return 0;
        if (_currentDragLabel == labelY) return 1;
        return 2;
    }

    Vector3 SetAxis(Vector3 v, int axis, float f)
    {
        if (axis == 0) v.x = f;
        else if (axis == 1) v.y = f;
        else v.z = f;
        return v;
    }
}