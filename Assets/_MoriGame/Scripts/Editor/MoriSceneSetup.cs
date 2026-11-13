using System.IO;
using ByteDance.PICO.XR;
using MoriGame.Build;
using MoriGame.Combat;
using MoriGame.Configs;
using MoriGame.Devices;
using MoriGame.Gestures;
using MoriGame.Player;
using MoriGame.Shop;
using MoriGame.Territory;
using MoriGame.Zombies;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.AI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

namespace MoriGame.Editor
{
    /// <summary>
    /// M1-T3.5 场景搭建（幂等，可重复执行）：
    /// 28×28 地面 / 出生标记（P1）/ 中控正式模型（2×2×1.2 米，4×4 格）/ TerritoryGridDebugger / XR Player（头 + 双手追踪锚点）。
    /// 构建时由 BuildScript.BuildAndroidAPK 自动调用。
    /// </summary>
    public static class MoriSceneSetup
    {
        private const string SceneDir = "Assets/_MoriGame/Scenes";
        private const string ScenePath = SceneDir + "/MoriMain.unity";
        private const string MatDir = "Assets/_MoriGame/Art/Materials";
        // 2026-11-06 实测反馈：1.3 米够不着，减半到 0.65 米（伸手即可交互）。
        private const float WorkbenchRadiusMeters = 0.65f;
        private const float WorkbenchSurfaceY = 0.73f;

        private static readonly string[] OwnedRootNames =
        {
            "Ground", "Spawn_NE", "Spawn_NW", "Spawn_SE", "Spawn_SW", "Spawn_P1",
            "CoreDevice", "TerritoryDebugger", "XR Origin", "Directional Light", "Main Camera",
            "Match", "ShopPanel", "RespawnPoint", "ZombiePool", "BuildManager", "TeleportFade",
            "WaveDirector", "ShopStands", "Tutorial", "Workbench"
            // "ShopPanel" 保留：清理旧场景残留（2026-11-05 起不再新建）；
            // "Tutorial" 2026-11-05 补登记（M2-T0 漏登导致重搭重复堆积，本次一并清掉）；
            // "WristMenuPanel" 2026-11-05 移出：主动卡占位页暂撤（用户决策），M2-T4 需要时再回挂；
            // "Workbench" 2026-11-05 站桩工作台制登记（C 形台面/枪械插槽/WeaponHolster）
        };

        /// <summary>搭建代码版本：改动 MoriSceneSetup 时手动递增；Console/报告可见，用于识别"编辑器跑旧程序集"（磁盘代码变了但编辑器未刷新）</summary>
        public const string SetupVersion = "2026-11-06.coreshrink";

        /// <summary>瞄准模式出包前定案（红线规则：**游戏内一律不可切换**，所有开发设置仅出包前修改）：
        /// Gaze = A 视线瞄准（弹丸从枪口飞向视线命中点）；Barrel = B 枪管瞄准（弹丸沿枪管直射）。
        /// 改这里 → 重搭场景/出包生效。</summary>
        public const AimMode BuildAimMode = AimMode.Barrel;

        [MenuItem("MoriGame/搭建灰盒场景")]
        public static void SetupFromMenu()
        {
            SetupGreyboxScene();
        }

        public static void SetupGreyboxScene()
        {
            // 2026-11-06 红线加固：场景已由用户手工搭建（RealSpace 玩法树 + CUBE 地图 2.2 万块地形），
            // 全量重搭会 ClearExisting 清空手工作品。必须显式放行（创建 Builds/rebuild-scene.allow）才可执行；
            // 日常改动走 RealSpaceMigration（Builds/migrate-realspace.trigger）增量迁移。
            if (!File.Exists("Builds/rebuild-scene.allow"))
            {
                throw new System.InvalidOperationException(
                    "全量重搭已禁用（会清空用户手工场景：RealSpace 玩法树与地图资源）。" +
                    "确认要重搭时先创建 Builds/rebuild-scene.allow 放行文件；日常改场景用 migrate-realspace.trigger 迁移。");
            }
            var scene = LoadOrCreateScene();
            var gunPose = SnapshotGunPose(); // 用户手动摆的持枪位姿先存下来（ClearExisting 会连 XR Origin 整树删除）
            ClearExisting();
            var materials = CreateMaterials();
            var configs = LoadConfigs();
            CreateLight();
            CreateGround(materials.ground);
            CreateSpawnMarkers(materials.spawnActive);
            var coreDevice = CreateCoreDevice(configs.core);
            CreateDebugger(materials.territoryOverlay);
            var rig = CreatePlayerRig(materials, configs.pistol, gunPose);
            var workbench = CreateWorkbench(materials, rig);
            var respawnPoint = CreateRespawnPoint();
            CreateMatch(configs, rig.origin, coreDevice, respawnPoint);
            CreateShopStands(materials, configs.turret, configs.fence, rig.hub, workbench);
            CreateSupplyCrate(materials, rig.hub, workbench);
            CreateZombiePool(materials, configs);
            CreateBuildManager(materials, configs, rig.hub);
            CreateWaveDirector(configs);
            CreateTutorial(rig);

            var navigationSettings = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
            var bakeRadius = navigationSettings.FindProperty("m_BuildSettings.agentRadius");
            if (bakeRadius == null)
            {
                throw new System.InvalidOperationException("找不到 NavMesh 场景烘焙半径设置");
            }
            bakeRadius.floatValue = 0.14f;
            navigationSettings.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh(); // 地面标记 NavigationStatic 后烘焙（僵尸寻路）
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            var navMeshData = AssetDatabase.LoadAssetAtPath<NavMeshData>(SceneDir + "/MoriMain/NavMesh.asset");
            var serializedNavMesh = navMeshData != null ? new SerializedObject(navMeshData) : null;
            var radiusProperty = serializedNavMesh?.FindProperty("m_NavMeshBuildSettings.agentRadius");
            if (radiusProperty == null || !Mathf.Approximately(radiusProperty.floatValue, 0.14f))
            {
                throw new System.InvalidOperationException("NavMesh 烘焙半径未刷新为 0.14 米，实际=" +
                    (radiusProperty == null ? "missing" : radiusProperty.floatValue.ToString("F3")));
            }
            Debug.Log($"[MoriGame] 灰盒场景就绪 v{SetupVersion}: " + ScenePath);
        }

        private static Scene LoadOrCreateScene()
        {
            if (File.Exists(ScenePath))
            {
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            return scene;
        }

        private static void ClearExisting()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (System.Array.IndexOf(OwnedRootNames, root.name) >= 0)
                {
                    Object.DestroyImmediate(root);
                }
            }
        }

        /// <summary>持枪位姿由用户在场景里手动摆放（2026-11-02 定案，贴合官方手模）：
        /// 重搭/构建都会清场景重建，先把手摆好的枪位姿快照下来带过去——用户微调永不被构建冲掉。
        /// null = 场景里还没有枪（首搭），CreatePistol 用内置基准位姿。</summary>
        /// <summary>枪位姿快照（2026-11-02 手动贴合定稿 + 2026-11-03 修复）：
        /// 以 **XR Origin 本地坐标** 存取——世界位姿快照在 rig 挪位/转向后会整体错位
        /// （2026-11-03 实测：出生点 (2,2)→(1,1) 后枪离手 1 米）。</summary>
        private static Pose? SnapshotGunPose()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name != "XR Origin")
                {
                    continue;
                }
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Pistol")
                    {
                        return new Pose(
                            root.transform.InverseTransformPoint(t.position),
                            Quaternion.Inverse(root.transform.rotation) * t.rotation);
                    }
                }
            }
            return null;
        }

        private struct SceneMaterials
        {
            public Material ground;
            public Material spawnActive;
            public Material territoryOverlay;
            public Material acidProjectile;
            public Material muzzleFlash;
            public Material pistolDark;
            public Material previewOk;
            public Material previewBlocked;
            public Material previewExpand;
            public Material turretRange;
            public Material turretRangeFaint;
            public Material roadblockGrey;
            public Material teleportOk;
            public Material teleportReady;
            public Material teleportBlocked;
            public Material teleportCooldown;
            public Material reticleDot;
            public Material pulseWhite;
            public Material pulseGold;
            public Material projectileBall;
            public Material muzzleCharge;
            public Material fenceField;
            public Material standHover;
            public Material standGrey;
            public Material anchorGlow;
        }

        private static SceneMaterials CreateMaterials()
        {
            Directory.CreateDirectory(MatDir);
            var stale = MatDir + "/SpawnIdle.mat";
            if (File.Exists(stale))
            {
                AssetDatabase.DeleteAsset(stale); // 单角标记后不再使用
            }
            return new SceneMaterials
            {
                ground = EnsureMat("GroundGrey", new Color(0.62f, 0.64f, 0.67f)), // 2026-11-02 实测调亮：原 0.42 偏暗压格线对比
                spawnActive = EnsureMat("SpawnActive", new Color(0.25f, 0.95f, 0.4f)),
                territoryOverlay = EnsureCustomShaderMat("TerritoryGridOverlay", "MoriGame/TerritoryGrid"),
                acidProjectile = EnsureUnlitMat("AcidProjectile", new Color(0.35f, 1f, 0.18f)),
                muzzleFlash = EnsureUnlitMat("MuzzleFlash", new Color(1f, 0.72f, 0.2f)),
                pistolDark = EnsureMat("PistolDark", new Color(0.16f, 0.17f, 0.2f)),
                previewOk = EnsureUnlitMat("PreviewOk", new Color(0.3f, 1f, 0.45f)),
                previewBlocked = EnsureUnlitMat("PreviewBlocked", new Color(1f, 0.25f, 0.2f)),
                previewExpand = EnsureUnlitMat("PreviewExpand", new Color(0.2f, 0.9f, 0.8f)), // 相邻扩格青绿（2026-11-04 修订三态）
                turretRange = EnsureUnlitMat("TurretRange", new Color(1f, 0.85f, 0.25f)), // 射界描边亮金（预览呼吸）
                turretRangeFaint = EnsureUnlitMat("TurretRangeFaint", new Color(0.62f, 0.5f, 0.12f)), // 落地常驻暗金
                roadblockGrey = EnsureMat("RoadblockGrey", new Color(0.55f, 0.5f, 0.45f)),
                teleportOk = EnsureUnlitMat("TeleportOk", new Color(0.3f, 1f, 0.5f)),
                teleportReady = EnsureUnlitMat("TeleportReady", new Color(0.35f, 0.6f, 1f)), // 传送就绪蓝（2026-11-03 呼吸环）
                teleportBlocked = EnsureUnlitMat("TeleportBlocked", new Color(1f, 0.3f, 0.25f)),
                teleportCooldown = EnsureUnlitMat("TeleportCooldown", new Color(0.45f, 0.45f, 0.45f)),
                reticleDot = EnsureUnlitMat("ReticleDot", Color.white),
                pulseWhite = EnsureUnlitMat("PulseWhite", new Color(1f, 1f, 1f)),
                pulseGold = EnsureUnlitMat("PulseGold", new Color(1f, 0.84f, 0.25f)),
                projectileBall = EnsureUnlitMat("ProjectileBall", new Color(1f, 0.85f, 0.3f)),
                muzzleCharge = EnsureUnlitMat("MuzzleCharge", new Color(0.4f, 0.95f, 1f)), // 蓄势光青色（2026-11-03 食指伸直开火）
                fenceField = EnsureTransparentUnlitMat("ElectricFenceField", new Color(0.2f, 0.85f, 1f, 0.28f)),
                standHover = EnsureUnlitMat("StandHover", new Color(0.55f, 0.95f, 1f)), // 展示位悬停微发光青（2026-11-05 范式终稿）
                standGrey = EnsureMat("StandGrey", new Color(0.32f, 0.33f, 0.36f)), // 展示位不可抓置灰
                anchorGlow = EnsureTransparentUnlitMat("AnchorGlowGold", new Color(1f, 0.84f, 0.25f, 0.5f)) // 悬浮锚点微光圈淡金
            };
        }

        /// <summary>引用项目内自定义 shader 的材质：场景引用 → shader 必然进入构建（运行时 Shader.Find 不会）</summary>
        private static Material EnsureCustomShaderMat(string name, string shaderName)
        {
            var path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogError("[MoriGame] 找不到 shader " + shaderName);
                    return null;
                }
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        private static Material EnsureMat(string name, Color color)
        {
            var path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            ApplyColor(m, color);
            return m;
        }

        private static Material EnsureUnlitMat(string name, Color color)
        {
            var path = MatDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            ApplyColor(m, color);
            return m;
        }

        private static Material EnsureTransparentUnlitMat(string name, Color color)
        {
            var m = EnsureUnlitMat(name, color);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void ApplyColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor"))
            {
                m.SetColor("_BaseColor", color);
            }
            else
            {
                m.color = color;
            }
            EditorUtility.SetDirty(m);
        }

        private static void CreateLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            lightGo.transform.localEulerAngles = new Vector3(50f, -30f, 0f);
        }

        private static void CreateGround(Material material)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(28f, 1f, 28f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            GameObjectUtility.SetStaticEditorFlags(ground, StaticEditorFlags.NavigationStatic); // NavMesh 烘焙输入
        }

        private static void CreateSpawnMarkers(Material active)
        {
            // 守中布局（2026-11-03 传送收紧连带）：出生点必须在初始地盘内（格 (30,30)）
            CreateMarker("Spawn_P1", 1, 1, active);
        }

        private static void CreateMarker(string name, int x, int z, Material material)
        {
            var root = new GameObject(name);
            root.transform.position = new Vector3(x, 0f, z);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            disc.transform.localScale = new Vector3(0.7f, 0.03f, 0.7f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(disc.GetComponent<Collider>());

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            pole.transform.localScale = new Vector3(0.08f, 1.2f, 0.08f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(pole.GetComponent<Collider>());
        }

        private static CoreDevice CreateCoreDevice(CoreDeviceConfig config)
        {
            const string modelPath = "Assets/ArtAsset/ZhongKong/ZhongKong.prefab";
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                throw new FileNotFoundException("找不到中控模型 prefab", modelPath);
            }

            var device = new GameObject("CoreDevice");
            device.transform.position = Vector3.zero;
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, device.transform);
            model.name = "Visual";
            // 实测 prefab 已为 Unity Y 轴朝上；Y=90° 转向工作台，实例根不缩放。
            // 模型大小由 prefab 内嵌 FBX 节点缩放驱动（2026-11-06 用户改 0.45→0.25，随 prefab 资产持久化）。
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            model.transform.localScale = Vector3.one;

            // 碰撞体/导航障碍按实测视觉包围盒自适应（2026-11-06 中控缩小）：缩放再变重搭即跟随，
            // 不再硬编码占地格尺寸——占地格仍 4×4（TerritoryGrid.CoreDeviceSizeCells，放置缓冲区）。
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                bounds.Encapsulate(renderer.bounds);
            }
            var hitBox = device.AddComponent<BoxCollider>();
            hitBox.center = bounds.center;
            hitBox.size = bounds.size;
            var core = device.AddComponent<CoreDevice>();
            core.config = config;
            var obstacle = device.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = hitBox.center;
            obstacle.size = hitBox.size;
            return core;
        }

        private static void CreateDebugger(Material overlayMaterial)
        {
            var go = new GameObject("TerritoryDebugger");
            var debugger = go.AddComponent<TerritoryGridDebugger>();
            debugger.overlayMaterial = overlayMaterial;
        }

        private static (XROrigin origin, PicoGestureHub hub) CreatePlayerRig(
            SceneMaterials materials, WeaponConfig pistol, Pose? gunPose)
        {
            var root = new GameObject("XR Origin");
            root.transform.position = new Vector3(1f, 0f, 1f); // 站桩固定点：初始地盘内，中控在背后
            root.transform.localEulerAngles = new Vector3(0f, 45f, 0f);
            var origin = root.AddComponent<XROrigin>();

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(root.transform, false);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var camera = camGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f; // VR 标准（2026-11-02 实测：0.3 会裁切近距离虚拟手）
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<XrNodeTracker>().node = XRNode.Head;
            origin.Camera = camera;
            origin.CameraYOffset = 0f;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // 2026-11-02 手部体系统一：删自建 RightHandAnchor/LeftHandAnchor 占位 Cube 与射线——
            // 手部一切以官方 HandLeft/HandRight prefab 为准（PXR_Hand 23 关节自驱），商店悬停走手腕位置。
            // 持枪定位同日定案：枪由用户在场景里手动摆放贴合手模，CreatePistol 把位姿换算挂到右手腕关节，
            // 运行时零定位代码（层级刚性跟随，与手柄模型挂载机制一致）。
            var reticle = CreateReticle(camGo.transform, materials);
            root.AddComponent<DevLocomotion>();
            var hub = CreateGestureLayer(root, offset.transform, materials);

            // 弹丸对象池（2026-11-02 真实弹丸，同僵尸池模式）
            var poolGo = new GameObject("ProjectilePool");
            poolGo.transform.SetParent(root.transform, false);
            var projectilePool = poolGo.AddComponent<ProjectilePool>();
            projectilePool.ballMaterial = materials.projectileBall;

            CreatePistol(root.transform, hub, pistol, materials, reticle, gunPose, projectilePool);
            CreateStatusHud(camGo.transform);
            return (origin, hub);
        }

        /// <summary>
        /// M1-T3.6 手势底座，与 PICO「PICO Hand Tracking」Building Block 同配方：
        /// 打开工程手势追踪开关 + 把官方 HandLeft/HandRight prefab 挂到 Camera Offset（PXR_Hand 运行时自驱关节/捏合/射线）。
        /// </summary>
        private static PicoGestureHub CreateGestureLayer(GameObject rigRoot, Transform cameraOffset,
            SceneMaterials materials)
        {
            PXR_ProjectSetting.GetProjectConfig().handTracking = true;
            PXR_ProjectSetting.SaveAssets();

            var leftGo = InstantiateHandPrefab(
                "Packages/com.bytedance.pico.xr/Resources/Prefabs/HandLeft.prefab", cameraOffset);
            var rightGo = InstantiateHandPrefab(
                "Packages/com.bytedance.pico.xr/Resources/Prefabs/HandRight.prefab", cameraOffset);

            var hubGo = new GameObject("PicoGestureHub");
            hubGo.transform.SetParent(cameraOffset, false);
            var hub = hubGo.AddComponent<PicoGestureHub>();
            if (leftGo != null)
            {
                hub.leftHand = leftGo.GetComponent<PXR_Hand>();
            }
            if (rightGo != null)
            {
                hub.rightHand = rightGo.GetComponent<PXR_Hand>();
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 拆信标手势工具（GestureBeaconInput）已随建设即扩张删除（2026-11-03）
#endif
            // GestureLocomotion 不再挂载（2026-11-06 用户决策：侧推转向移除，站桩制玩家完全固定；
            // 传送 2026-11-05 已移除）。组件保留不删可回挂，传送环材质同留。
            return hub;
        }

        private static GameObject InstantiateHandPrefab(string path, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[MoriGame] 找不到手部 prefab：" + path);
                return null;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            // 2026-11-02 实测反馈：删 SDK 自带捏合触控射线视觉（DefaultRay——随捏合变粗变长的"针头"）。
            // PXR_Hand 对 defaultRay 引用全程判空（Start/UpdateRayPose），删除无副作用；
            // RayPose 节点保留——SDK 每帧驱动的位姿锚（RayValid 时激活），纯逻辑无视觉。
            var touchRay = instance.transform.Find("RayPose/DefaultRay");
            if (touchRay != null)
            {
                Object.DestroyImmediate(touchRay.gameObject);
            }
            return instance;
        }

        private static Transform CreateRespawnPoint()
        {
            var go = new GameObject("RespawnPoint");
            go.transform.position = new Vector3(1f, 0f, 1f); // 站桩固定点重生，不再传送
            go.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            return go.transform;
        }

        /// <summary>
        /// M1-T5 应急手枪正式模型（2026-11-06 替换 Cube 占位）：
        /// 枪根（WeaponController 常驻驱动状态机）+ 视觉子根 Visual（收枪时隐藏，防组件自锁）
        /// + GunModel 正式 prefab + Magazine 弹匣锚点（左手拍击换弹判定）。
        /// 持枪位姿 = 用户在场景里手动摆放贴合官方手模（运行时零定位代码）；
        /// 这里把该位姿换算挂到右手腕关节下——手腕被 SDK 逐帧驱动，枪随层级刚性跟随（手柄机制）。
        /// gunPose 为重搭前的用户位姿快照（微调不丢）；首搭无快照时用内置基准
        /// （2026-11-02 用户手动贴合定稿：rig 根本地 pos (-0.0711,-0.0201,0.0767) / euler (0,7.403,90)）。
        /// </summary>
        private static void CreatePistol(Transform rigRoot, PicoGestureHub hub, WeaponConfig pistol,
            SceneMaterials materials, Reticle reticle, Pose? gunPose, ProjectilePool projectilePool)
        {
            var gun = new GameObject("Pistol");
            gun.transform.SetParent(rigRoot, false);
            gun.transform.localPosition = new Vector3(-0.0711f, -0.0201f, 0.0767f); // 用户手动贴合定稿基准
            gun.transform.localRotation = Quaternion.Euler(0f, 7.403f, 90f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(gun.transform, false);

            const string gunModelPath = "Assets/ArtAsset/Gun/Sci-FiSubmachineGun/Gun.prefab";
            var gunModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(gunModelPath);
            if (gunModelAsset == null)
            {
                throw new FileNotFoundException("找不到枪械模型 prefab", gunModelPath);
            }
            var gunModel = (GameObject)PrefabUtility.InstantiatePrefab(gunModelAsset, visual.transform);
            gunModel.name = "GunModel";
            gunModel.transform.localPosition = Vector3.zero;
            gunModel.transform.localRotation = Quaternion.identity;
            gunModel.transform.localScale = Vector3.one;

            var muzzleGo = new GameObject("Muzzle");
            muzzleGo.transform.SetParent(visual.transform, false);
            muzzleGo.transform.localPosition = new Vector3(0f, 0.015f, 0.185f);

            var magGo = new GameObject("Magazine");
            magGo.transform.SetParent(visual.transform, false);
            magGo.transform.localPosition = new Vector3(0f, -0.05f, 0.01f); // 握把下段 = 弹匣位置

            var flash = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flash.name = "MuzzleFlash";
            flash.transform.SetParent(muzzleGo.transform, false);
            flash.transform.localScale = new Vector3(0.05f, 0.05f, 0.02f);
            flash.GetComponent<MeshRenderer>().sharedMaterial = materials.muzzleFlash;
            Object.DestroyImmediate(flash.GetComponent<Collider>());
            var flashRenderer = flash.GetComponent<MeshRenderer>();
            flashRenderer.enabled = false;

            // 枪口蓄势光（2026-11-03 食指伸直开火）：食指伸直+有效区激活，9Hz 呼吸由 WeaponController 驱动
            var glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            glow.name = "ChargeGlow";
            glow.transform.SetParent(muzzleGo.transform, false);
            glow.transform.localPosition = new Vector3(0f, 0f, 0.02f);
            glow.transform.localScale = new Vector3(0.028f, 0.028f, 0.028f);
            glow.GetComponent<MeshRenderer>().sharedMaterial = materials.muzzleCharge;
            Object.DestroyImmediate(glow.GetComponent<Collider>());
            glow.SetActive(false);

            var weapon = gun.AddComponent<WeaponController>();
            weapon.config = pistol;
            weapon.gestureHub = hub;
            weapon.muzzle = muzzleGo.transform;
            weapon.magazine = magGo.transform;
            weapon.visualRoot = visual;
            weapon.projectilePool = projectilePool; // 2026-11-02 真实弹丸（曳光 LineRenderer 已移除）
            weapon.muzzleFlash = flashRenderer;
            weapon.chargeGlow = glow;
            weapon.reticle = reticle;
            weapon.aimMode = BuildAimMode; // 红线：出包前定案，游戏内不可切换
            if (reticle != null)
            {
                reticle.useBarrelRay = BuildAimMode == AimMode.Barrel;
                reticle.barrelOrigin = muzzleGo.transform; // 枪管瞄准模式的准星射线源
            }

            // 用户快照优先（微调保留，rig 本地坐标存取——rig 挪动不影响），然后统一换算挂到右手腕关节
            if (gunPose.HasValue)
            {
                gun.transform.position = rigRoot.TransformPoint(gunPose.Value.position);
                gun.transform.rotation = rigRoot.rotation * gunPose.Value.rotation;
            }
            var wrist = hub != null && hub.rightHand != null &&
                        hub.rightHand.handJoints.Count > (int)HandJoint.JointWrist
                ? hub.rightHand.handJoints[(int)HandJoint.JointWrist]
                : null;
            if (wrist != null)
            {
                gun.transform.SetParent(wrist, true); // worldPositionStays：世界位姿不变 → 捕获为手腕本地偏移
            }
        }

        /// <summary>准星（2026-11-02 动态深度）：盘面预转 90° 正对视线，位置由 Reticle 每帧按命中深度设置</summary>
        private static Reticle CreateReticle(Transform camera, SceneMaterials materials)
        {
            var go = new GameObject("Reticle");
            go.transform.SetParent(camera, false);
            go.transform.localPosition = new Vector3(0f, 0f, 2f);

            var dot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            dot.name = "Dot";
            dot.transform.SetParent(go.transform, false);
            dot.transform.localPosition = Vector3.zero;
            dot.transform.localEulerAngles = new Vector3(90f, 0f, 0f); // 盘面正对视线
            dot.transform.localScale = new Vector3(0.016f, 0.001f, 0.016f);
            dot.GetComponent<MeshRenderer>().sharedMaterial = materials.reticleDot;
            Object.DestroyImmediate(dot.GetComponent<Collider>());

            var pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "Pulse";
            pulse.transform.SetParent(go.transform, false);
            pulse.transform.localPosition = Vector3.zero;
            pulse.transform.localEulerAngles = new Vector3(90f, 0f, 0f);
            pulse.transform.localScale = new Vector3(0.02f, 0.001f, 0.02f);
            var pulseRenderer = pulse.GetComponent<MeshRenderer>();
            pulseRenderer.sharedMaterial = materials.pulseWhite;
            Object.DestroyImmediate(pulse.GetComponent<Collider>());
            pulse.SetActive(false);

            var reticle = go.AddComponent<Reticle>();
            reticle.dot = dot.transform;
            reticle.pulse = pulse.transform;
            reticle.pulseRenderer = pulseRenderer;
            reticle.pulseWhiteMaterial = materials.pulseWhite;
            reticle.pulseGoldMaterial = materials.pulseGold;
            return reticle;
        }

        /// <summary>M2-T2 六类僵尸共用男性 Humanoid Avatar 与 Animator，模型/体型按类型注入。</summary>
        private static void CreateZombiePool(SceneMaterials materials, GreyboxConfigs configs)
        {
            const string modelRoot = "Assets/ToonyTinyPeople/TT_Zombies/";
            var go = new GameObject("ZombiePool");
            var pool = go.AddComponent<ZombiePool>();
            pool.normalConfig = configs.zombie;
            pool.configs.AddRange(configs.zombies);
            pool.acidMaterial = materials.acidProjectile;

            Avatar avatar = null;
            var modelAssets = AssetDatabase.LoadAllAssetsAtPath(modelRoot + "model/m_zombies_customizable.FBX");
            foreach (var asset in modelAssets)
            {
                if (asset is Avatar candidate)
                {
                    avatar = candidate;
                    break;
                }
            }
            if (avatar == null)
            {
                Debug.LogError("[MoriGame] 缺少男性僵尸 Humanoid Avatar");
            }
            var controller = ZombieAnimatorGenerator.EnsureController();
            pool.modelSets.Add(ZombieModel(modelRoot, "Normal", "TT_zombie_M_01",
                avatar, controller, Vector3.one * 0.6f)); // 2026-11-09 实测反馈：全员再 ×0.6（翻倍版 1.0 起 → 0.6）
            pool.modelSets.Add(ZombieModel(modelRoot, "Runner", "TT_zombie_M_09_broken",
                avatar, controller, Vector3.one * 0.78f));
            pool.modelSets.Add(ZombieModel(modelRoot, "Spitter", "TT_zombie_M_01",
                avatar, controller, Vector3.one * 0.96f, "Body_08_robust"));
            pool.modelSets.Add(ZombieModel(modelRoot, "ShieldGuard", "TT_zombie_M_14_riot_police",
                avatar, controller, Vector3.one * 1.14f));
            pool.modelSets.Add(ZombieModel(modelRoot, "Brute", "TT_zombie_M_20_butcher",
                avatar, controller, Vector3.one * 1.32f));
            pool.modelSets.Add(ZombieModel(modelRoot, "Overseer", "TT_zombie_M_22_soldier",
                avatar, controller, Vector3.one * 1.5f));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            go.AddComponent<ZombieDevSpawner>();
#endif
        }

        private static ZombiePool.ModelSet ZombieModel(string modelRoot, string zombieId,
            string prefabName, Avatar avatar, RuntimeAnimatorController controller,
            Vector3 scale, string bodyMeshOverride = null)
        {
            var path = modelRoot + "prefabs/zombie_male/" + prefabName + ".prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[MoriGame] 缺少僵尸 prefab：" + path);
            }
            return new ZombiePool.ModelSet
            {
                zombieId = zombieId,
                prefab = prefab,
                avatar = avatar,
                controller = controller,
                visualScale = scale,
                bodyMeshOverride = bodyMeshOverride
            };
        }

        /// <summary>M1-T6 建造模式管理器（购买→0.5 米吸附预览→左捏旋转→捏合确认/张掌取消）</summary>
        private static void CreateBuildManager(SceneMaterials materials, GreyboxConfigs configs, PicoGestureHub hub)
        {
            var go = new GameObject("BuildManager");
            var build = go.AddComponent<BuildManager>();
            build.gestureHub = hub;
            build.previewOkMaterial = materials.previewOk;
            build.previewBlockedMaterial = materials.previewBlocked;
            build.previewExpandMaterial = materials.previewExpand; // 相邻扩格三态（2026-11-04 修订）
            build.roadblockMaterial = materials.roadblockGrey;
            build.fenceMaterial = materials.fenceField; // 半透明青色区域体积，无碰撞、无 NavMesh 阻挡
            build.turretRangeMaterial = materials.turretRange; // 机枪塔射界描边（预览亮金/落地暗金）
            build.turretRangeFaintMaterial = materials.turretRangeFaint;
        }

        /// <summary>M2-T1 波次导演：8 分钟整局（12 秒准备 + 五阶段 → 结算），数值读 MatchPhaseConfig</summary>
        private static void CreateWaveDirector(GreyboxConfigs configs)
        {
            var go = new GameObject("WaveDirector");
            var director = go.AddComponent<Waves.WaveDirector>();
            director.config = configs.match;
            director.font = ChineseFontGenerator.EnsureFontAsset();
        }

        private struct GreyboxConfigs
        {
            public PlayerConfig player;
            public CoreDeviceConfig core;
            public PropConfig roadblock;
            public PropConfig turret;
            public PropConfig fence;
            public WeaponConfig pistol;
            public ZombieConfig zombie;
            public ZombieConfig[] zombies;
            public MatchPhaseConfig match;
        }

        private static GreyboxConfigs LoadConfigs()
        {
            return new GreyboxConfigs
            {
                player = LoadConfig<PlayerConfig>("Assets/_MoriGame/Configs/Player/PlayerConfig.asset"),
                core = LoadConfig<CoreDeviceConfig>("Assets/_MoriGame/Configs/Devices/CoreDeviceConfig.asset"),
                roadblock = LoadConfig<PropConfig>("Assets/_MoriGame/Configs/Props/Prop_Roadblock.asset"),
                turret = LoadConfig<PropConfig>("Assets/_MoriGame/Configs/Props/Prop_AutoTurret.asset"),
                fence = LoadConfig<PropConfig>("Assets/_MoriGame/Configs/Props/Prop_ElectricFence.asset"),
                pistol = LoadConfig<WeaponConfig>("Assets/_MoriGame/Configs/Weapons/Weapon_Pistol.asset"),
                zombie = LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Normal.asset"),
                zombies = new[]
                {
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Normal.asset"),
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Runner.asset"),
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Spitter.asset"),
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_ShieldGuard.asset"),
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Brute.asset"),
                    LoadConfig<ZombieConfig>("Assets/_MoriGame/Configs/Zombies/Zombie_Overseer.asset")
                },
                match = LoadConfig<MatchPhaseConfig>("Assets/_MoriGame/Configs/MatchPhases.asset")
            };
        }

        private static T LoadConfig<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogError("[MoriGame] 缺少配置资产 " + path + "（先执行 ConfigAssetGenerator.GeneratePlayerCoreConfigs）");
            }
            return asset;
        }

        private static void CreateMatch(GreyboxConfigs configs, XROrigin origin, CoreDevice coreDevice,
            Transform respawnPoint)
        {
            var go = new GameObject("Match");
            var match = go.AddComponent<MatchBootstrap>();
            match.playerConfig = configs.player;
            match.coreConfig = configs.core;
            match.coreDevice = coreDevice;
            match.playerRig = origin.transform;
            match.playerAttackTarget = origin.transform;
            EnsurePlayerHitProxy(origin.transform);
            match.respawnPoint = respawnPoint;
        }

        public static void EnsurePlayerHitProxy(Transform playerRig)
        {
            var hitProxy = playerRig.GetComponent<CapsuleCollider>();
            if (hitProxy == null)
            {
                hitProxy = playerRig.gameObject.AddComponent<CapsuleCollider>();
            }
            hitProxy.isTrigger = true;
            hitProxy.center = new Vector3(0f, 0.9f, 0f);
            hitProxy.radius = 0.25f;
            hitProxy.height = 1.8f;
            hitProxy.direction = 1;
        }

        /// <summary>站桩工作台：世界固定，玩家转向时台面不跟转；右侧真枪插槽由 WeaponHolster 接管。</summary>
        private static Transform CreateWorkbench(SceneMaterials materials,
            (XROrigin origin, PicoGestureHub hub) rig)
        {
            var root = new GameObject("Workbench");
            root.transform.position = rig.origin.transform.position;
            root.transform.rotation = Quaternion.Euler(0f, rig.origin.transform.eulerAngles.y, 0f);

            // 悬浮版无实体台面：不再创建旧工作台大盒/导航障碍；玩家承伤代理在 XR 根的小胶囊上。

            // ---- 悬浮锚点制：枪/建筑锚点悬浮呼吸（AnchorFloat）+ 淡金微光圈标记
            const float anchorDistance = 0.45f; // 锚点距玩家水平距离（米）
            const float anchorHeightY = 0.86f; // 枪锚点用户定版高度；环独立对齐建筑环，不反向移动枪模
            const float anchorRingHeightY = 0.70f;

            // 枪锚点：右前 45°（悬浮原地 ±1.5cm/1.5s 呼吸；微光圈标记；「捏合拿起」提示牌保留）
            var slotPosition = new Vector3(
                Mathf.Sin(45f * Mathf.Deg2Rad) * anchorDistance, anchorHeightY,
                Mathf.Cos(45f * Mathf.Deg2Rad) * anchorDistance);
            var anchor = new GameObject("PistolSlotAnchor");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = slotPosition;
            anchor.AddComponent<MoriGame.Build.AnchorFloat>();
            CreateAnchorRing(anchor.transform, Vector3.up * (anchorRingHeightY - anchorHeightY),
                0.16f, materials.anchorGlow);

            var hintGo = new GameObject("PistolGrabHint");
            hintGo.transform.SetParent(root.transform, false);
            hintGo.transform.localPosition = slotPosition + Vector3.up * 0.22f;
            var hintCanvas = hintGo.AddComponent<Canvas>();
            hintCanvas.renderMode = RenderMode.WorldSpace;
            var hintRect = (RectTransform)hintGo.transform;
            hintRect.sizeDelta = new Vector2(24f, 6f);
            hintGo.transform.localScale = Vector3.one * 0.01f;
            CreateText(hintRect, "Text", "捏合拿起", 3.4f,
                Vector2.zero, new Vector2(24f, 5f), FontStyle.Bold);
            hintGo.SetActive(false);

            var holster = root.AddComponent<WeaponHolster>();
            holster.hub = rig.hub;
            holster.weapon = Object.FindFirstObjectByType<WeaponController>();
            holster.slotAnchor = anchor.transform;
            holster.hoverMaterial = materials.standHover;
            holster.hoverHint = hintGo;
            holster.hoverRadiusMeters = 0.12f;
            return root.transform;
        }

        /// <summary>悬浮锚点微光圈：淡金半透明圆片平铺锚点下方，取出后留光圈指引放回位</summary>
        private static void CreateAnchorRing(Transform parent, Vector3 localPosition, float radius, Material material)
        {
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "AnchorRing";
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = localPosition;
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = material;
            Object.DestroyImmediate(ring.GetComponent<Collider>());
        }

        /// <summary>工作台左侧建筑展示区：两座桌面展示位各放常驻迷你模型与名称价格标签。
        /// 抓起时 ShopStand 克隆跟手，原位模型不动如货架；无库存数字。
        /// 编辑器回归键：4=抓/放机枪塔，5=抓/放电网；抓起后 Q 归位，V 扔掉。</summary>
        private static void CreateShopStands(SceneMaterials materials, PropConfig turret, PropConfig fence,
            PicoGestureHub hub, Transform workbench)
        {
            var root = new GameObject("ShopStands");
            root.transform.SetParent(workbench, false);
            // 悬浮锚点制（2026-11-12 用户恢复）：左前 45°（机枪塔）/70°（电网），距 0.45 米、高 0.70 米
            const float distance = 0.45f;
            const float heightY = 0.70f;
            CreateStand(root.transform, "Stand_AutoTurret", turret, null, true,
                new Vector3(-Mathf.Sin(45f * Mathf.Deg2Rad) * distance, heightY,
                    Mathf.Cos(45f * Mathf.Deg2Rad) * distance), materials, hub, KeyCode.Alpha4);
            CreateStand(root.transform, "Stand_ElectricFence", fence, new[] { fence }, false,
                new Vector3(-Mathf.Sin(70f * Mathf.Deg2Rad) * distance, heightY,
                    Mathf.Cos(70f * Mathf.Deg2Rad) * distance), materials, hub, KeyCode.Alpha5);
        }

        private static void CreateStand(Transform parent, string name, PropConfig prop, PropConfig[] sharedGroup,
            bool turretModel, Vector3 position, SceneMaterials materials, PicoGestureHub hub, KeyCode grabKey)
        {
            var stand = new GameObject(name);
            stand.transform.SetParent(parent, false);
            stand.transform.localPosition = position;

            // 悬浮锚点制：底板/台面废除，展示=迷你模型悬浮（AnchorFloat 呼吸）+ 淡金微光圈
            stand.AddComponent<MoriGame.Build.AnchorFloat>();
            CreateAnchorRing(stand.transform, Vector3.zero, 0.14f, materials.anchorGlow);

            // 浮空标签（名称+价格单行；ShopStand.LateUpdate billboard 朝玩家）
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(stand.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.3f, 0f); // 模型上方悬空
            var labelCanvas = labelGo.AddComponent<Canvas>();
            labelCanvas.renderMode = RenderMode.WorldSpace;
            var lrt = (RectTransform)labelGo.transform;
            lrt.sizeDelta = new Vector2(30f, 7f);
            labelGo.transform.localScale = Vector3.one * 0.01f; // 0.3×0.07 米
            var nameLabel = CreateText(lrt, "Name",
                prop != null ? $"{prop.displayName} {prop.price} 金币" : "-", 3.4f,
                new Vector2(0f, 0f), new Vector2(30f, 6f), FontStyle.Bold);
            var grabHint = CreateText(lrt, "GrabHint", "捏合拿起", 3.2f,
                new Vector2(0f, -6f), new Vector2(30f, 5f), FontStyle.Bold);
            grabHint.gameObject.SetActive(false);

            // 常驻迷你模型（货架本体；悬停发光/置灰对象）——2026-11-12 用户定版：现用正式预制体 1:5 展示
            var shopStand = stand.AddComponent<ShopStand>();
            shopStand.hub = hub;
            shopStand.prop = prop;
            shopStand.sharedGroup = sharedGroup;
            shopStand.nameLabel = nameLabel;
            shopStand.labelRoot = labelGo.transform;
            shopStand.hoverMaterial = materials.standHover;
            shopStand.greyMaterial = materials.standGrey;
            shopStand.hoverHint = grabHint.gameObject;
            shopStand.grabKey = grabKey;
            RebuildStandDisplay(stand.transform, turretModel); // 迷你模型 + modelRoot + 抓取半径统一装配
        }

        /// <summary>重建展示位迷你模型（2026-11-12 用户定版）：现用正式预制体 1:5 展示（灰盒废除），
        /// 接好 modelRoot 并把抓取交互半径加大到 0.18 米。创建（CreateStand）与迁移同步共用。</summary>
        public static void RebuildStandDisplay(Transform stand, bool turretModel)
        {
            var shopStand = stand.GetComponent<ShopStand>();
            var oldModel = stand.Find("Model");
            if (oldModel != null)
            {
                Object.DestroyImmediate(oldModel.gameObject);
            }
            var prefabPath = turretModel
                ? "Assets/资产/NEW/预制体/Prop_机枪塔.prefab"
                : "Assets/资产/NEW/预制体/Prop_电网.prefab";
            var displayPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (displayPrefab == null)
            {
                Debug.LogError("[MoriGame] 展示预制体缺失：" + prefabPath);
                return;
            }
            var model = new GameObject("Model");
            model.transform.SetParent(stand, false);
            model.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            model.transform.localScale = Vector3.one * 0.14f; // 迷你展示（用户 2026-11-12：缩小+锚点 45°/95° 拉开防抢触发）；手持保持同尺寸
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(displayPrefab);
            inst.transform.SetParent(model.transform, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var col in model.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(col); // 模型只作视觉（防挡凝视/抓取克隆挡射线）
            }
            if (shopStand != null)
            {
                shopStand.modelRoot = model.transform;
                shopStand.hoverRadiusMeters = 0.18f; // 用户：悬浮模型加大交互范围（原 0.08）
            }
        }

        private static void CreateSupplyCrate(SceneMaterials materials, PicoGestureHub hub, Transform workbench)
        {
            var root = new GameObject("SupplyCrate");
            root.transform.SetParent(workbench, false);
            root.transform.localPosition = new Vector3(0.03f, WorkbenchSurfaceY + 0.05f, 0.62f);

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(visual.transform, false);
            body.transform.localScale = new Vector3(0.18f, 0.1f, 0.14f);
            body.GetComponent<MeshRenderer>().sharedMaterial = materials.pulseGold;
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lid.name = "Lid";
            lid.transform.SetParent(visual.transform, false);
            lid.transform.localPosition = new Vector3(0f, 0.065f, 0f);
            lid.transform.localScale = new Vector3(0.19f, 0.03f, 0.15f);
            lid.GetComponent<MeshRenderer>().sharedMaterial = materials.pistolDark;
            Object.DestroyImmediate(lid.GetComponent<Collider>());

            var gazeCollider = root.AddComponent<BoxCollider>();
            gazeCollider.center = new Vector3(0f, 0.025f, 0f);
            gazeCollider.size = new Vector3(0.22f, 0.17f, 0.18f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            labelGo.transform.localScale = Vector3.one * 0.008f;
            var canvas = labelGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.sizeDelta = new Vector2(32f, 10f);
            var label = CreateText(labelRect, "Text", "补给箱\n凝视+捏合 1.5 秒", 3.2f,
                Vector2.zero, new Vector2(32f, 10f), FontStyle.Bold);

            var crate = root.AddComponent<Economy.SupplyCrate>();
            crate.hub = hub;
            crate.visualRoot = visual;
            crate.label = label;
            crate.gazeCollider = gazeCollider;
        }

        /// <summary>腕部菜单已整体清理（2026-11-10 M2-T7）：占位页 2026-11-05 撤、ShopPanel/ShopRow/ShopToast/WristMenu
        /// 移出 Assets 备份于 Deprecated_2026-11-10_T7/，M2-T4 主动卡接入时从备份回挂。</summary>

        /// <summary>M2-T0 新手引导四件套（2026-11-04）：清单/JIT/图例三 UI + 手模左蓝右橙腕带。
        /// 准备期秒数读 MatchPhases phases[0].endSeconds（与 WaveScheduler 同源，不硬编码）。
        /// 2026-11-05 展示位范式：步骤②③改读 ShopStand/BuildManager 信号，不再注入 ShopPanel。</summary>
        private static void CreateTutorial((XROrigin origin, PicoGestureHub hub) rig)
        {
            var go = new GameObject("Tutorial");
            var tutorial = go.AddComponent<Tutorial.TutorialController>();
            tutorial.hub = rig.hub;
            tutorial.build = Object.FindFirstObjectByType<BuildManager>();
            tutorial.director = Object.FindFirstObjectByType<Waves.WaveDirector>();
            tutorial.weapon = Object.FindFirstObjectByType<WeaponController>();
            tutorial.holster = Object.FindFirstObjectByType<WeaponHolster>();
            tutorial.font = ChineseFontGenerator.EnsureFontAsset();
            var matchConfig = LoadConfig<MatchPhaseConfig>("Assets/_MoriGame/Configs/MatchPhases.asset");
            if (matchConfig != null && matchConfig.phases != null && matchConfig.phases.Count > 0)
            {
                tutorial.prepSeconds = matchConfig.phases[0].endSeconds;
            }
        }

        /// <summary>常驻状态 HUD：布局由 StatusHud.Awake 按 Inspector 参数应用（默认 0.35×0.05 米，
        /// 视野下缘居中，坐姿 1.2 米眼高基准 ≤40% 视野宽）；此处仅建结构，编辑器预览值与运行值一致。</summary>
        private static void CreateStatusHud(Transform camera)
        {
            var go = new GameObject("StatusHud");
            go.transform.SetParent(camera, false);
            go.transform.localPosition = new Vector3(0f, -0.36f, 0.9f);
            go.transform.localScale = Vector3.one * 0.01f;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(35f, 5f);
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.04f, 0.05f, 0.08f, 0.55f);
            bg.raycastTarget = false;
            var hud = go.AddComponent<StatusHud>();
            hud.label = CreateText(rt, "HudLabel", "", 3, Vector2.zero, new Vector2(33f, 4f), FontStyle.Normal);
        }

        /// <summary>
        /// TMP 版文本创建（2026-11-02 迁移）：中文字体统一 MoriZhCN_SDF（ChineseFontGenerator 生成）。
        /// 字号语义：TMP fontSize 与 Legacy 同为画布单位（×0.01 缩放 = 米），标题 9 / 正文 6-7 →
        /// em 高 6-9cm，坐姿 0.8-1 米视距下视野角 3.4°-6.4°，远高于 0.5° 可读下限。
        /// </summary>
        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize,
            Vector2 anchoredPos, Vector2 sizeDelta, FontStyle style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
            var text = go.AddComponent<TextMeshProUGUI>();
            if (zhFont == null)
            {
                zhFont = ChineseFontGenerator.EnsureFontAsset();
            }
            if (zhFont != null)
            {
                text.font = zhFont;
            }
            text.fontSize = fontSize;
            text.fontStyle = style == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.text = content;
            return text;
        }

        private static TMP_FontAsset zhFont;
    }
}
