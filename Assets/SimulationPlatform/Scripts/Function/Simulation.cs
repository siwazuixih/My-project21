using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Assets.Scripts;
using System;
using TMPro;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Navigation;

public class Simulation : ModelImport
{
    public Dropdown JointSelectDropdown;
    public Button CloseDropdownButton;
    public Button SelectJointButton;
    private List<JointModel> jointList = new List<JointModel>();

    public Button Back;
    public DynamicEdgeArrowGuide EdgeArrow;
    public ModelMouseController ModelMouseController;
    public Button PathPlanButton;
    public Button ResetButton;
    public Button ReplaceButton;
    public Button ColliderGenButton;
    public MissionController MissionController;
    public EditorStyleVector3Control RobotXYZ;
    public EditorStyleVector3Control RobotRotation;
    public EditorStyleVector3Control SceneXYZ;
    public EditorStyleVector3Control SceneRotation;
    public AutoColliderGen_Final ColliderGen;
    public GameObjectTreePanel GameObjectTreePanel;

    // Simulation/Realtime 模式切换按钮
    public Button SimBtn;
    public Button RealBtn;
    public GameObject RightSim;
    public GameObject RightReal;
    public Sprite SimSelected;
    public Sprite SimUnselected;
    public Sprite RealSelected;
    public Sprite RealUnselected;

    public GameObject StatusSim;
    public GameObject StatusReal;

    public ConnectCommander ConnectCommander;

    public StatusPanel robotConnection;
    public StatusPanel boltConnection;

    public GameObject Notice;
    public DobotController Dobot;
    public RobokitController Robokit;

    private bool StepRun = true;

    // 当前运行模式状态
    private bool m_isSimulationMode = true;

    private int runIndex = 0;

    public void SetStepRun(bool stepRun)
    {
        Debug.Log($"是否单步运行：{stepRun}");
        StepRun = stepRun;
    }

    public void StartRun()
    {
        Debug.Log($"开始运行，{(StepRun ? "单步运行" : "连续运行")}");
        Notice.SetActive(true);
        if (StepRun)
        {
            if (ConnectCommander != null)
            {
                ConnectCommander.DispatchTask(runIndex);
                runIndex += 1;
            }
        }
        else
        {
            if (ConnectCommander != null)
            {
                ConnectCommander.DispatchNextTargetSequence();
            }

        }
    }

    private void Awake()
    {
        
    }
    // Start is called before the first frame update
    void Start()
    {
        runIndex = 0;
        if (robotConnection != null)
        {
            robotConnection.Msg = "未连接";
            robotConnection.StatusType = StatusType.NONE;
        }
        if (boltConnection != null)
        {
            boltConnection.Msg = "未连接";
            boltConnection.StatusType = StatusType.NONE;
        }
        if (Back != null)
        {
            Back.onClick.AddListener(OnBackClick);
        }
        if (PathPlanButton != null)
        {
            PathPlanButton.onClick.RemoveListener(OnPathPlanClicked);
            PathPlanButton.onClick.AddListener(OnPathPlanClicked);
        }
        if (ResetButton != null)
        {
            ResetButton.onClick.AddListener(OnResetClicked);
        }
        if (ReplaceButton != null)
        {
            ReplaceButton.onClick.AddListener(OnReplaceClicked);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.onClick.AddListener(PerformReplace);
            SelectJointButton.gameObject.SetActive(false);
        }
        if (ColliderGenButton != null)
        {
            ColliderGenButton.onClick.AddListener(OnColliderGenClicked);
        }
        if (JointSelectDropdown != null)
        {
            JointSelectDropdown.onValueChanged.AddListener(OnJointSelectChanged);
            JointSelectDropdown.gameObject.SetActive(false);
        }
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.onClick.AddListener(OnCloseDropdownClicked);
            CloseDropdownButton.gameObject.SetActive(false);
        }
        if (RobotXYZ != null)
        {
            RobotXYZ.OnValueChanged += OnRobotXYZValueChanged;
        }
        if (RobotRotation != null)
        {
            RobotRotation.OnValueChanged += OnRobotRotationValueChanged;
        }
        if (SceneXYZ != null)
        {
            SceneXYZ.OnValueChanged += OnSceneXYZValueChanged;
        }
        if (SceneRotation != null)
        {
            SceneRotation.OnValueChanged += OnSceneRotationValueChanged;
        }
        // 初始化 Simulation/Realtime 模式切换按钮
        InitializeModeButtons();
        // 加载模型
        if (RunManager.Project != null && RunManager.Project.Scene != null && RunManager.Project.Scene.Id != null)
        {
            LoadModelFromProject(RunManager.Project.Scene.Id);
        }
        // 订阅 DobotController 事件
        SubscribeToDobotEvents();
        // 订阅 RobokitController 事件
        SubscribeToRobokitEvents();
    }

    private void SubscribeToDobotEvents()
    {
        if (Dobot != null)
        {
            Dobot.OnDobotEvent += OnDobotEvent;
            Debug.Log("已订阅 DobotController 事件");
        }
        else
        {
            Debug.LogWarning("DobotController 未赋值，无法订阅事件");
        }
    }

    private void UnsubscribeFromDobotEvents()
    {
        if (Dobot != null)
        {
            Dobot.OnDobotEvent -= OnDobotEvent;
            Debug.Log("已取消订阅 DobotController 事件");
        }
    }

    private void SubscribeToRobokitEvents()
    {
        if (Robokit != null)
        {
            Robokit.OnRobokitEvent += OnRobokitEvent;
            Debug.Log("已订阅 RobokitController 事件");
        }
        else
        {
            Debug.LogWarning("RobokitController 未赋值，无法订阅事件");
        }
    }

    private void UnsubscribeFromRobokitEvents()
    {
        if (Robokit != null)
        {
            Robokit.OnRobokitEvent -= OnRobokitEvent;
            Debug.Log("已取消订阅 RobokitController 事件");
        }
    }

    private void OnDobotEvent(object sender, DobotEventArgs e)
    {
        switch (e.Type)
        {
            case DobotEventArgs.EventType.ConnectionChanged:
                HandleConnectionChanged(e);
                break;

            case DobotEventArgs.EventType.CommandSent:
                HandleCommandSent(e);
                break;

            case DobotEventArgs.EventType.ResponseReceived:
                HandleResponseReceived(e);
                break;

            case DobotEventArgs.EventType.FeedbackUpdated:
                HandleFeedbackUpdated(e);
                break;

            case DobotEventArgs.EventType.ErrorOccurred:
                HandleErrorOccurred(e);
                break;

            case DobotEventArgs.EventType.ChassisPositionUpdated:
                HandleChassisPositionUpdated(e);
                break;

            case DobotEventArgs.EventType.ChassisNavStatusUpdated:
                HandleChassisNavStatusUpdated(e);
                break;
        }
    }

    private void OnRobokitEvent(object sender, DobotEventArgs e)
    {
        OnDobotEvent(sender, e);
    }

    private void HandleConnectionChanged(DobotEventArgs e)
    {
        Debug.Log($"连接状态变化: Port={e.PortType}, Connected={e.IsConnected}, Message={e.Message}");

        if (e.PortType == "Dashboard")
        {
            if (robotConnection != null)
            {
                robotConnection.Msg = e.IsConnected ? "已连接" : "未连接";
                robotConnection.StatusType = e.IsConnected ? StatusType.SUCCESS : StatusType.NONE;
            }
        }
        else if (e.PortType == "Feedback")
        {
        }
        else if (e.PortType == "Chassis")
        {
            if (boltConnection != null)
            {
                boltConnection.Msg = e.IsConnected ? "已连接" : "未连接";
                boltConnection.StatusType = e.IsConnected ? StatusType.SUCCESS : StatusType.NONE;
            }
            Debug.Log($"底盘连接状态: {e.IsConnected}, {e.Message}");
        }
    }

    private void HandleCommandSent(DobotEventArgs e)
    {
        Debug.Log($"命令发送: Command={e.Command}, Success={e.Success}, Error={e.ErrorMessage}");
    }

    private void HandleResponseReceived(DobotEventArgs e)
    {
        Debug.Log($"响应接收: Port={e.PortType}, Response={e.Response}");
    }

    private void HandleFeedbackUpdated(DobotEventArgs e)
    {
        Debug.Log($"反馈更新: Mode={e.RobotModeText}, SpeedScaling={e.SpeedScaling}, VelocityRatio={e.VelocityRatio}");
    }

    private void HandleErrorOccurred(DobotEventArgs e)
    {
        Debug.LogError($"错误: Context={e.Context}, Error={e.ErrorMessage}");
        MessageManage.ShowMessage($"错误: {e.ErrorMessage}", 2);
    }

    private void HandleChassisPositionUpdated(DobotEventArgs e)
    {
        Debug.Log($"底盘位置更新: X={e.ChassisX}, Y={e.ChassisY}, Angle={e.ChassisAngle}");
    }

    private void HandleChassisNavStatusUpdated(DobotEventArgs e)
    {
        Debug.Log($"底盘导航状态更新: State={e.ChassisNavState}, RemainDistance={e.ChassisRemainDistance}");
    }

    private void OnDestroy()
    {
        if (PathPlanButton != null)
        {
            PathPlanButton.onClick.RemoveListener(OnPathPlanClicked);
        }
        UnsubscribeFromDobotEvents();
        UnsubscribeFromRobokitEvents();
    }

    /// <summary>
    /// 从Project的glb.FilePath加载模型
    /// </summary>
    private void LoadModelFromProject(string sceneId)
    {
        if (sceneId != null)
        {
            var sceneModel = ModelManager.GetScene(sceneId);
            if (sceneModel != null && sceneModel.Glb != null && !string.IsNullOrEmpty(sceneModel.Glb.FilePath))
            {
                LoadModelFromFile(sceneModel.Glb.FilePath);
                return;
            }
        }
        MessageManage.ShowMessage("场景或模型路径为空，无法加载", 2);
        Debug.Log("Scene或模型路径为空，无法加载模型");
    }

    /// <summary>
    /// 从文件加载模型（重写基类方法）
    /// </summary>
    protected override string LoadModelFromFile(string filePath)
    {
        string fullpath = base.LoadModelFromFile(filePath);
        return fullpath;
    }

    protected override async Task OnModelLoaded(GameObject model)
    {
        if (modelCamera != null)
        {
            if (modelCamera.GetComponent<CameraController>() == null)
            {
                modelCamera.gameObject.AddComponent<CameraController>();
            }
            EdgeArrow.mainCam = modelCamera;
            EdgeArrow.targetA = model.transform;
            Debug.Log("已将CameraController组件挂载到modelCamera上并设置EdgeArrow目标");
        }
        if (ColliderGen != null)
        {
            ColliderGen.targetObject = model;
            //ColliderGen.hollowParts.Add(model);
            //await ColliderGen.Generate();
        }
        if (ModelMouseController != null)
        {
            //ModelMouseController.Move(-1, 0, -2);
            ModelMouseController.Move(0, 0, 0);
        }

        // 加载并应用碰撞体
        LoadAndApplyColliders(model);

        await Task.Yield();
        RebuildRuntimeNavMesh();
    }

    private bool RebuildRuntimeNavMesh()
    {
        if (MissionController == null)
        {
            Debug.LogError("MissionController 未配置，无法确定需要从 NavMesh 构建中排除的机器人。");
            return false;
        }

        GameObject robotRoot = MissionController.gameObject;
        NavMeshModifier robotModifier = robotRoot.GetComponent<NavMeshModifier>();
        if (robotModifier == null)
        {
            robotModifier = robotRoot.AddComponent<NavMeshModifier>();
        }
        robotModifier.ignoreFromBuild = true;
        robotModifier.applyToChildren = true;

        NavMeshSurface[] surfaces = FindObjectsOfType<NavMeshSurface>(true);
        if (surfaces.Length == 0)
        {
            Debug.LogError("未找到 NavMeshSurface，底盘无法进行路径规划。");
            return false;
        }

        foreach (NavMeshSurface surface in surfaces)
        {
            if (!surface.gameObject.activeSelf)
            {
                surface.gameObject.SetActive(true);
            }
            surface.BuildNavMesh();
            Debug.Log($"NavMesh 已根据当前场景重新构建：{surface.name}，已排除机器人层级：{robotRoot.name}");
        }

        return true;
    }

    /// <summary>
    /// 加载并应用碰撞体
    /// </summary>
    private void LoadAndApplyColliders(GameObject model)
    {
        if (model == null)
        {
            Debug.LogWarning("模型为空，无法加载碰撞体");
            return;
        }

        // 确保项目和场景信息完整
        if (RunManager.Project == null)
        {
            Debug.LogWarning("项目为空，无法加载碰撞体");
            return;
        }

        if (RunManager.Project.Scene == null)
        {
            Debug.LogWarning("场景为空，无法加载碰撞体");
            return;
        }

        if (string.IsNullOrEmpty(RunManager.Project.Scene.Id))
        {
            Debug.LogWarning("场景ID为空，无法加载碰撞体");
            return;
        }

        // 通过 Scene.Id 从 ModelManager 获取 SceneModel
        var sceneModel = ModelManager.GetScene(RunManager.Project.Scene.Id);
        if (sceneModel == null)
        {
            Debug.LogWarning($"未找到场景模型，SceneId: {RunManager.Project.Scene.Id}");
            return;
        }

        if (sceneModel.Glb == null)
        {
            Debug.LogWarning("场景的Glb模型为空");
            return;
        }

        if (string.IsNullOrEmpty(sceneModel.Glb.FilePath))
        {
            Debug.LogWarning("场景的Glb文件路径为空");
            return;
        }

        // 加载碰撞体数据
        Debug.Log($"尝试加载碰撞体数据，模型路径: {sceneModel.Glb.FilePath}");
        ColliderModel colliderModel = ColliderManager.LoadColliderData(sceneModel.Glb.FilePath);

        if (colliderModel == null)
        {
            Debug.Log("未找到该场景的碰撞体数据");
            return;
        }

        if (colliderModel.MjRoots == null || colliderModel.MjRoots.Count == 0)
        {
            // 兼容旧版本数据结构
            Debug.Log("使用兼容模式处理碰撞体数据");
            CreateColliderObjectsFromLegacyData(model, colliderModel);
            return;
        }

        // 创建碰撞体对象
        int totalMeshes = 0;
        foreach (var mjRoot in colliderModel.MjRoots)
        {
            totalMeshes += mjRoot.Meshes.Count;
        }
        Debug.Log($"找到 {colliderModel.MjRoots.Count} 个 MjRoot，共 {totalMeshes} 个碰撞体，正在重建...");
        CreateColliderObjects(model, colliderModel);
    }

    /// <summary>
    /// 使用兼容模式从旧数据创建碰撞体对象（当没有 MjRoot 信息时）
    /// </summary>
    private void CreateColliderObjectsFromLegacyData(GameObject model, ColliderModel colliderModel)
    {
        // 为了兼容旧版本，创建一个默认的 MjRoot
        GameObject mjRootObj = new GameObject("Default_MjRoot");
        mjRootObj.transform.SetParent(model.transform, false);
        mjRootObj.transform.localPosition = Vector3.zero;
        mjRootObj.transform.localRotation = Quaternion.identity;
        mjRootObj.transform.localScale = Vector3.one;

        // 添加 MjBody 组件
        Mujoco.MjBody mjBody = mjRootObj.AddComponent<Mujoco.MjBody>();

        int meshCount = 0;
        
        // 尝试从旧数据结构读取 Meshes
        var oldMeshesField = colliderModel.GetType().GetField("Meshes");
        if (oldMeshesField != null)
        {
            var oldMeshes = oldMeshesField.GetValue(colliderModel) as System.Collections.IList;
            if (oldMeshes != null)
            {
                foreach (var item in oldMeshes)
                {
                    // 使用反射获取 MeshData 的属性
                    var meshData = item;
                    var nameProp = meshData.GetType().GetProperty("Name");
                    var verticesProp = meshData.GetType().GetProperty("Vertices");
                    var trianglesProp = meshData.GetType().GetProperty("Triangles");
                    var isVHACDProp = meshData.GetType().GetProperty("IsVHACD");

                    if (nameProp != null && verticesProp != null && trianglesProp != null)
                    {
                        // 创建临时的 ColliderMeshData 对象
                        ColliderMeshData tempMeshData = new ColliderMeshData
                        {
                            Name = nameProp.GetValue(meshData) as string,
                            Vertices = verticesProp.GetValue(meshData) as string,
                            Triangles = trianglesProp.GetValue(meshData) as string,
                            IsVHACD = (isVHACDProp != null) && (bool)isVHACDProp.GetValue(meshData)
                        };

                        Mesh mesh = ColliderManager.ColliderMeshDataToMesh(tempMeshData);
                        if (mesh != null)
                        {
                            CreateColliderObject(mjRootObj.transform, mesh, tempMeshData);
                            meshCount++;
                        }
                    }
                }
            }
        }

        Debug.Log($"兼容模式完成，创建了 {meshCount} 个碰撞体");
    }

    /// <summary>
    /// 创建单个碰撞体对象的辅助方法
    /// </summary>
    private void CreateColliderObject(Transform parent, Mesh mesh, ColliderMeshData meshData)
    {
        GameObject colliderObj = new GameObject(meshData.Name);
        colliderObj.transform.SetParent(parent, false);
        colliderObj.transform.localPosition = Vector3.zero;
        colliderObj.transform.localRotation = Quaternion.identity;
        colliderObj.transform.localScale = Vector3.one;

        // 添加 MjGeom 组件
        Mujoco.MjGeom mjGeom = colliderObj.AddComponent<Mujoco.MjGeom>();

#if UNITY_EDITOR
        UnityEditor.SerializedObject so = new UnityEditor.SerializedObject(mjGeom);
        var prop = so.FindProperty("ShapeType") ?? so.FindProperty("shapeType") ?? so.FindProperty("m_ShapeType");
        if (prop != null)
        {
            prop.intValue = 6;
            so.ApplyModifiedProperties();
        }
#endif

        Mujoco.MjMeshShape shape = new Mujoco.MjMeshShape();
        shape.Mesh = mesh;
        mjGeom.Mesh = shape;

        MeshCollider meshCollider = colliderObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = meshData.IsVHACD;

        Rigidbody rigidbody = colliderObj.AddComponent<Rigidbody>();
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        rigidbody.constraints = RigidbodyConstraints.FreezeAll;

        ModelCollisionHighlighter highlighter = colliderObj.AddComponent<ModelCollisionHighlighter>();

        if (meshData.IsVHACD)
        {
            colliderObj.tag = "VHACD";
        }

        Debug.Log($"已创建碰撞体: {meshData.Name} (VHACD: {meshData.IsVHACD})");
    }

    /// <summary>
    /// 创建碰撞体对象
    /// </summary>
    private void CreateColliderObjects(GameObject model, ColliderModel colliderModel)
    {
        if (colliderModel.MjRoots == null || colliderModel.MjRoots.Count == 0)
        {
            Debug.LogWarning("碰撞体数据中没有 MjRoot 信息");
            return;
        }

        // 为每个 MjRoot 创建层级结构
        foreach (var mjRootData in colliderModel.MjRoots)
        {
            if (mjRootData.Meshes == null || mjRootData.Meshes.Count == 0)
            {
                continue;
            }

            // 查找或创建父物体
            Transform parentTransform = FindOrCreateParentTransform(model, mjRootData.ParentPath);

            // 创建 MjRoot
            GameObject mjRootObj = new GameObject(mjRootData.Name);
            mjRootObj.transform.SetParent(parentTransform, false);
            mjRootObj.transform.localPosition = Vector3.zero;
            mjRootObj.transform.localRotation = Quaternion.identity;
            mjRootObj.transform.localScale = Vector3.one;

            // 添加 MjBody 组件
            Mujoco.MjBody mjBody = mjRootObj.AddComponent<Mujoco.MjBody>();

            Debug.Log($"已创建 MjRoot: {mjRootData.Name}");

            // 为每个 ColliderMeshData 创建 GameObject 作为 MjRoot 的子对象
            foreach (var meshData in mjRootData.Meshes)
            {
                Mesh mesh = ColliderManager.ColliderMeshDataToMesh(meshData);
                if (mesh == null)
                {
                    Debug.LogWarning($"无法重建 Mesh: {meshData.Name}");
                    continue;
                }

                // 使用辅助方法创建碰撞体
                CreateColliderObject(mjRootObj.transform, mesh, meshData);
            }
        }

        Debug.Log("碰撞体重建完成");
    }

    /// <summary>
    /// 根据路径查找或创建父物体
    /// </summary>
    private Transform FindOrCreateParentTransform(GameObject root, string parentPath)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            return root.transform;
        }

        Transform current = root.transform;
        string[] pathSegments = parentPath.Split('/');

        foreach (string segment in pathSegments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                continue;
            }

            Transform child = current.Find(segment);
            if (child == null)
            {
                // 如果找不到，创建新的
                GameObject newObj = new GameObject(segment);
                newObj.transform.SetParent(current, false);
                newObj.transform.localPosition = Vector3.zero;
                newObj.transform.localRotation = Quaternion.identity;
                newObj.transform.localScale = Vector3.one;
                child = newObj.transform;
            }

            current = child;
        }

        return current;
    }

    private void OnBackClick()
    {
        if (RunManager.RunStatus == RunStatus.IDLE || RunManager.RunStatus == RunStatus.INTERRUPT)
        {
            ModelCollisionHighlighter.SeletectedObjects.Clear();
            if (ColliderGen != null)
            {
                ColliderGen.ClearGenerated();
                ColliderGen.hollowParts.Clear();
            }
            MissionController.ResetMission();
            runIndex = 0;
            SceneManager.LoadScene("Main");
        }
    }

    // Update is called once per frame
    void Update()
    {

    }
    private void OnPathPlanClicked()
    {
        /*
        if (ModelCollisionHighlighter.selectedObject != null)
        {
            //PathPlanManager.Init(currentModel);
            //Debug.Log($"开始规划移动至：{ModelCollisionHighlighter.selectedObject.name}");
            //PathPlanManager.MoveTo(ModelCollisionHighlighter.selectedObject.transform);
        }
        if (ModelCollisionHighlighter.SeletectedObjects.Count > 0)
        {
            Debug.Log($"开始规划，规划点数量：{ModelCollisionHighlighter.SeletectedObjects.Count}");
            //PathPlanManager.Init(currentModel);
            PathPlanManager.InitAct(currentModel);
            PathPlanManager.MoveTo(null);
        }//*/

        Debug.Log("开始仿真前重新构建 NavMesh...");
        if (!RebuildRuntimeNavMesh())
        {
            MessageManage.ShowMessage("NavMesh 构建失败，无法开始仿真", 1);
            return;
        }

        if (MissionController != null)
        {
            float? chassisSpeed = ObserveParam.Instance.BaseSpeedValue;
            float? armSpeed = ObserveParam.Instance.ArmSpeedValue;
            float? distance = ObserveParam.Instance.ObservationDistanceValue;
            float? x = ObserveParam.Instance.XValue;
            float? y = ObserveParam.Instance.YValue;
            float? z = ObserveParam.Instance.ZValue;
            MissionController.ControlMission(ModelCollisionHighlighter.SeletectedObjects, chassisSpeed, armSpeed, distance, x, y, z);
        }
    }

    private void OnResetClicked()
    {
        if (MissionController != null)
        {
            MissionController.ResetMission();
        }
    }

    private async void OnColliderGenClicked()
    {
        if (GameObjectTreePanel != null && currentModel != null && ColliderGen != null)
        {
            //GameObjectTreePanel.Show(currentModel, ColliderGen);
            await ColliderGen.Generate();
        }
        else
        {
            Debug.Log("GameObjectTreePanel、currentModel或ColliderGen未设置");
        }
    }

    private void OnReplaceClicked()
    {
        if (ModelCollisionHighlighter.selectedObject == null)
        {
            Debug.Log("没有高亮的物体可以替换");
            return;
        }

        if (JointSelectDropdown == null)
        {
            Debug.LogError("JointSelectDropdown未赋值");
            return;
        }

        PopulateJointDropdown();

        JointSelectDropdown.gameObject.SetActive(true);
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.gameObject.SetActive(true);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.gameObject.SetActive(true);
        }
        Debug.Log("已弹出接头选择下拉框");
    }

    private void PerformReplace()
    {
        if (ModelCollisionHighlighter.selectedObject == null)
        {
            Debug.Log("没有高亮的物体可以替换");
            return;
        }

        if (JointSelectDropdown.value < 0 || JointSelectDropdown.value >= jointList.Count)
        {
            Debug.Log("请先从下拉框中选择一个接头模型");
            return;
        }

        JointModel selectedJoint = jointList[JointSelectDropdown.value];
        if (selectedJoint.Glb == null || string.IsNullOrEmpty(selectedJoint.Glb.FilePath))
        {
            Debug.Log("选中的接头模型没有关联的GLB文件");
            return;
        }

        Transform highlightedTransform = ModelCollisionHighlighter.selectedObject.transform;
        Vector3 position = highlightedTransform.localPosition;
        Quaternion rotation = highlightedTransform.localRotation;
        Vector3 scale = highlightedTransform.localScale;
        Transform parent = highlightedTransform.parent;
        string oldObjectName = highlightedTransform.gameObject.name;

        string executableDir = PathTool.GetExecutableDirPath();
        string fullModelPath = Path.Combine(executableDir, selectedJoint.Glb.FilePath);

        if (File.Exists(fullModelPath))
        {
            GameObject oldObject = ModelCollisionHighlighter.selectedObject.gameObject;           
            
            StartCoroutine(InstantiateReplacedModel(fullModelPath, position, rotation, scale, parent, oldObject, selectedJoint.Name));
            Debug.Log($"正在替换物体 {oldObjectName} 为 {selectedJoint.Name}");
        }
        else
        {
            Debug.LogError($"模型文件不存在: {fullModelPath}");
        }

        JointSelectDropdown.gameObject.SetActive(false);
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.gameObject.SetActive(false);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.gameObject.SetActive(false);
        }
    }

    private void OnCloseDropdownClicked()
    {
        JointSelectDropdown.gameObject.SetActive(false);
        if (CloseDropdownButton != null)
        {
            CloseDropdownButton.gameObject.SetActive(false);
        }
        if (SelectJointButton != null)
        {
            SelectJointButton.gameObject.SetActive(false);
        }
        Debug.Log("已关闭接头选择下拉框");
    }

    private void PopulateJointDropdown()
    {
        if (JointSelectDropdown == null)
        {
            Debug.LogWarning("JointSelectDropdown未赋值");
            return;
        }

        JointSelectDropdown.options.Clear();
        jointList.Clear();

        if (ModelManager.XmlModel == null || ModelManager.XmlModel.Joints == null || ModelManager.XmlModel.Joints.Count == 0)
        {
            Debug.LogWarning("没有可用的接头模型");
            return;
        }

        foreach (var joint in ModelManager.XmlModel.Joints)
        {
            if (!string.IsNullOrEmpty(joint.Name) && joint.Glb != null && !string.IsNullOrEmpty(joint.Glb.FilePath))
            {
                Dropdown.OptionData option = new Dropdown.OptionData();
                option.text = joint.Name;
                JointSelectDropdown.options.Add(option);
                jointList.Add(joint);
            }
        }

        JointSelectDropdown.value = -1;
        Debug.Log("接头下拉菜单填充完成，共" + jointList.Count + "个接头");
    }

    private void OnJointSelectChanged(int value)
    {
        if (value >= 0 && value < jointList.Count)
        {
            JointModel selectedJoint = jointList[value];
            Debug.Log("选择了接头: " + selectedJoint.Name);
        }
    }

    private void OnRobotXYZValueChanged(Vector3 value)
    {
        if (ModelMouseController != null)
        {
            ModelMouseController.Move(value.x, value.y, value.z);
        }
    }

    private void OnRobotRotationValueChanged(Vector3 value)
    {
        if (MissionController != null && MissionController.chassis.actuatorRot != null)
        {
            MissionController.chassis.actuatorRot.Control = -value.y * Mathf.Deg2Rad;
        }
    }

    private void OnSceneXYZValueChanged(Vector3 value)
    {
        if (currentModel != null)
        {
            currentModel.transform.position = value;
        }
    }

    private void OnSceneRotationValueChanged(Vector3 value)
    {
        if (currentModel != null)
        {
            currentModel.transform.rotation = Quaternion.Euler(value);
        }
    }

    /// <summary>
    /// 初始化 Simulation/Realtime 模式切换按钮
    /// </summary>
    private void InitializeModeButtons()
    {
        // 注册按钮点击事件
        if (SimBtn != null)
        {
            SimBtn.onClick.AddListener(OnSimBtnClicked);
        }
        if (RealBtn != null)
        {
            RealBtn.onClick.AddListener(OnRealBtnClicked);
        }
        // 设置初始状态为 Simulation 模式
        UpdateModeUI(true);
    }

    /// <summary>
    /// SimBtn 点击事件处理
    /// </summary>
    private void OnSimBtnClicked()
    {
        if (!m_isSimulationMode)
        {
            m_isSimulationMode = true;
            UpdateModeUI(true);
        }
    }

    /// <summary>
    /// RealBtn 点击事件处理
    /// </summary>
    private void OnRealBtnClicked()
    {
        if (m_isSimulationMode)
        {
            m_isSimulationMode = false;
            UpdateModeUI(false);
        }
    }

    /// <summary>
    /// 更新模式切换按钮的 UI 状态
    /// </summary>
    /// <param name="isSimulationMode">是否为 Simulation 模式</param>
    private void UpdateModeUI(bool isSimulationMode)
    {
        if (isSimulationMode)
        {
            // Simulation 模式：SimBtn 显示选中状态，RealBtn 显示未选中状态
            if (SimBtn != null && SimSelected != null)
            {
                SimBtn.image.sprite = SimSelected;
            }
            if (RealBtn != null && RealUnselected != null)
            {
                RealBtn.image.sprite = RealUnselected;
            }
            // RightSim 显示，RightReal 不显示
            if (RightSim != null)
            {
                RightSim.SetActive(true);
            }
            if (StatusSim != null)
            {
                StatusSim.SetActive(true);
            }
            if (RightReal != null)
            {
                RightReal.SetActive(false);
            }
            if (StatusReal != null)
            {
                StatusReal.SetActive(false);
            }
        }
        else
        {
            // Realtime 模式：SimBtn 显示未选中状态，RealBtn 显示选中状态
            runIndex = 0;
            if (SimBtn != null && SimUnselected != null)
            {
                SimBtn.image.sprite = SimUnselected;
            }
            if (RealBtn != null && RealSelected != null)
            {
                RealBtn.image.sprite = RealSelected;
            }
            // RightSim 不显示，RightReal 显示
            if (RightSim != null)
            {
                RightSim.SetActive(false);
            }
            if (StatusSim != null)
            {
                StatusSim.SetActive(false);
            }
            if (RightReal != null)
            {
                RightReal.SetActive(true);
            }
            if (StatusReal != null)
            {
                StatusReal.SetActive(true);
            }
        }
    }
}
