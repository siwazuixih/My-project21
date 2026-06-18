using UnityEngine;
using Mujoco;
using static Mujoco.MujocoLib;
using Unity.VisualScripting;

public class ModelMouseController : MonoBehaviour
{
    [Header("控制参数")]
    public float rotateSpeed = 2f; // 旋转速度
    public float scaleSpeed = 0.2f; // 缩放速度
    public float translateSpeed = 0.01f; // 平移速度
    public Vector2 scaleRange = new Vector2(0.1f, 10f); // 缩放范围

    private Vector3 lastMousePos; // 上一帧鼠标位置
    private bool isRotating = false; // 是否正在旋转
    private bool isTranslating = false; // 是否正在平移
    private bool isSelected = false; // 模型是否被选中
    public MjBody topLevelMjBody; // 最顶层的MjBody组件

    public bool MouseDrage = false;

    void Start()
    {
        // 初始化鼠标位置
        lastMousePos = Input.mousePosition;
        
        // 查找模型中最顶层的MjBody组件
        MjBody[] bodies = GetComponentsInChildren<MjBody>();
        if (bodies.Length > 0)
        {
            // 假设第一个找到的是最顶层的，或者找到根物体上的MjBody
            topLevelMjBody = GetComponent<MjBody>();
            if (topLevelMjBody == null && bodies.Length > 0)
            {
                topLevelMjBody = bodies[0];
                // 尝试找到真正的根MjBody（没有父MjBody的）
                foreach (MjBody body in bodies)
                {
                    if (body.transform.parent == transform)
                    {
                        topLevelMjBody = body;
                        break;
                    }
                }
            }
            Debug.Log("Found top-level MjBody: " + (topLevelMjBody != null ? topLevelMjBody.name : "None"));
            
            // 设置MjBody的Rigidbody为运动学模式
            if (topLevelMjBody != null)
            {
                // 获取Rigidbody组件并设置为运动学模式
                Rigidbody rb = topLevelMjBody.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                    Debug.Log("Set MjBody Rigidbody to kinematic mode");
                }

                // 也尝试设置所有子物体的Rigidbody为运动学模式
                Rigidbody[] allRbs = topLevelMjBody.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody childRb in allRbs)
                {
                    childRb.isKinematic = true;
                }
                Debug.Log("Set all child Rigidbodies to kinematic mode");
            }
        }
    }

    void Update()
    {
        if (UIUtil.IsPointerOverUI() || !MouseDrage) return;
      
        // 处理模型选择
        HandleModelSelection();

        // 鼠标右键拖拽：旋转模型
        if (Input.GetMouseButtonDown(1))
        {
            isRotating = true;
            lastMousePos = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(1))
        {
            isRotating = false;
        }

        // 鼠标左键拖拽：平移模型（仅当选中时）
        if (/*isSelected &&*/ Input.GetMouseButtonDown(0) /*&& Input.GetKeyDown(KeyCode.LeftShift)*/)
        {
            isTranslating = true;
            lastMousePos = Input.mousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isTranslating = false;
        }

        // 处理平移
        if (isTranslating && topLevelMjBody != null)
        {
            HandleModelTranslation();
        }
    }

    // 处理模型选择
    private void HandleModelSelection()
    {
        // 点击鼠标左键检查是否点击了该模型
        if (Input.GetMouseButtonDown(0) && Camera.main != null)
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                //Debug.Log("Model hit: " + hit.transform.name);
                // 检查点击的是否是当前模型或其子物体
                if (hit.transform.IsChildOf(transform) || hit.transform == transform)
                {
                    isSelected = !isSelected;
                    Debug.Log("Model " + (isSelected ? "selected" : "deselected") + ": " + transform.name);
                }
            }
        }
    }



    // 处理模型平移
    private void HandleModelTranslation()
    {
        Vector3 currentMousePos = Input.mousePosition;

        // 计算鼠标移动距离
        Vector3 deltaMouse = currentMousePos - lastMousePos;
        // 将鼠标移动转换为世界空间的平移
        float moveX = deltaMouse.x * translateSpeed;
        float moveY = deltaMouse.y * translateSpeed;

        // 创建平移向量 (Unity坐标系)
        Vector3 translationUnity = new Vector3(moveX, moveY, 0);

        // 检查MjScene是否存在
        if (MjScene.InstanceExists)
        {
            unsafe
            {
                // 通过MjScene.Instance.Data访问MjData
                var data = MjScene.Instance.Data;
                if (data != null && topLevelMjBody != null)
                {
                    // 计算xpos数组的索引
                    int bodyId = topLevelMjBody.MujocoId;
                    int posIndex = 3 * bodyId;

                    // Unity到Mujoco坐标系转换
                    // Unity: X,Y,Z (左手坐标系，Y轴向上)
                    // Mujoco: X,-Z,Y (右手坐标系，Z轴向上)
                    float moveInMujocoX = translationUnity.x;
                    float moveInMujocoY = translationUnity.y;
                    float moveInMujocoZ = translationUnity.z;

                    // 更新xpos数组 (Mujoco坐标系)
                    // data->xpos[posIndex] += moveInMujocoX;
                    // data->xpos[posIndex + 1] += moveInMujocoY;
                    // data->xpos[posIndex + 2] += moveInMujocoZ;

                    // 转换Unity坐标到Mujoco坐标（Y-up→Z-up）
                    var model = MjScene.Instance.Model;
                    // 修改位置
                    model->body_pos[posIndex] += moveInMujocoX;
                    model->body_pos[posIndex + 1] += moveInMujocoY;
                    model->body_pos[posIndex + 2] += moveInMujocoZ;
                    // 重置速度
                    //model->body_vel[bodyId] += moveInMujocoX;
                    //model->body_angvel[bodyId] += moveInMujocoZ;

                    // 强制更新运动学
                    mj_kinematics(MjScene.Instance.Model, data);


                    //Debug.Log("通过MjData平移模型 (Unity坐标): " + translationUnity);
                    //Debug.Log("模型新位置 (Mujoco坐标): " + new Vector3((float)data->xpos[posIndex], (float)data->xpos[posIndex + 1], (float)data->xpos[posIndex + 2]));
                }
            }
        }
        else
        {
            // 如果没有MjScene（例如在编辑器模式下），直接修改transform
            transform.position += translationUnity;
            Debug.Log("直接平移模型: " + translationUnity);
            Debug.Log("模型新位置: " + transform.position);
        }

        lastMousePos = currentMousePos;
    }

    public void Move(float x, float y, float z)
    {
        Vector3 targetPos = new Vector3(x, y, z);

        if (MjScene.InstanceExists)
        {
            unsafe
            {
                var data = MjScene.Instance.Data;
                if (data != null && topLevelMjBody != null)
                {
                    int bodyId = topLevelMjBody.MujocoId;
                    int posIndex = 3 * bodyId;

                    var model = MjScene.Instance.Model;
                    model->body_pos[posIndex] = targetPos.x;
                    model->body_pos[posIndex + 1] = targetPos.y;
                    model->body_pos[posIndex + 2] = targetPos.z;

                    mj_kinematics(MjScene.Instance.Model, data);
                }
            }
        }
        else
        {
            transform.position = targetPos;
        }
    }
}