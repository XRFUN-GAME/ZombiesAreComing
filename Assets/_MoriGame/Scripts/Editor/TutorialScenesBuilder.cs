using System.Linq;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MoriGame.Editor
{
    /// <summary>M4-T0 教学链场景构建（触发通道 build-tutorial.trigger）：
    /// ① Tutorial = MoriMain 副本裁剪（删 WaveDirector/SupplyCrate/ZombieDevSpawner，加 TutorialFlow，重烘导航）；
    /// ② StartScene = 极简 + XR Origin/Light 复制 + StartSceneController；
    /// ③ Build Settings 场景顺序 StartScene → Tutorial → MoriMain。
    /// 幂等：重复运行重建两场景（用户手工场景 MoriMain 只读不写）。</summary>
    public static class TutorialScenesBuilder
    {
        private const string MainScene = "Assets/_MoriGame/Scenes/MoriMain.unity";
        private const string TutorialScene = "Assets/_MoriGame/Scenes/Tutorial.unity";
        private const string StartScene = "Assets/_MoriGame/Scenes/StartScene.unity";

        public static void Build()
        {
            // ---------- ① Tutorial ----------
            EditorSceneManager.OpenScene(MainScene);
            var tutorial = EditorSceneManager.GetActiveScene();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/_MoriGame/Art/Fonts/MoriZhCN_SDF.asset"); // 项目中文字体图集（微软雅黑）——西文 TMP 无汉字会渲染成方块
            if (font == null)
            {
                font = Object.FindFirstObjectByType<TMP_Text>() != null
                    ? Object.FindFirstObjectByType<TMP_Text>().font : null;
            }

            foreach (var name in new[] { "WaveDirector", "SupplyCrate", "ZombieDevSpawner" })
            {
                var go = GameObject.Find(name);
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                    Debug.Log("[MoriGame] Tutorial 裁剪：" + name);
                }
            }
            var oldFlow = GameObject.Find("TutorialFlow");
            if (oldFlow != null)
            {
                Object.DestroyImmediate(oldFlow);
            }
            var flowGo = new GameObject("TutorialFlow");
            var flow = flowGo.AddComponent<MoriGame.Tutorial.TutorialFlow>();
            flow.hub = Object.FindFirstObjectByType<MoriGame.Gestures.PicoGestureHub>();
            flow.font = font;
            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface != null)
            {
                surface.BuildNavMesh(); // 靶场僵尸导航（教学场景烘焙数据随场景保存）
            }
            EditorSceneManager.SaveScene(tutorial, TutorialScene);
            Debug.Log("[MoriGame] Tutorial.unity 构建完成（设施/手势/教学流）");

            // ---------- ② StartScene（additive 建场景：旧版 Single 会先关 MoriMain，抓到的引用全销毁——XR Origin 丢失根因）----------
            var xrSource = GameObject.Find("XR Origin");
            var lightSource = GameObject.Find("Directional Light");
            var hubSource = Object.FindFirstObjectByType<MoriGame.Gestures.PicoGestureHub>();
            var xrRoot = xrSource != null ? xrSource.transform : null;
            var hubRoot = hubSource != null ? hubSource.transform.root : null;
            var start = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var startGo = new GameObject("StartScene");
            startGo.AddComponent<MoriGame.Tutorial.StartSceneController>();
            GameObject xrCopy = null;
            if (xrSource != null)
            {
                xrCopy = Object.Instantiate(xrSource); // 落活动场景（Tutorial），随后搬入 StartScene
                SceneManager.MoveGameObjectToScene(xrCopy, start);
            }
            else
            {
                Debug.LogError("[MoriGame] StartScene 缺 XR Origin（凝视+捏合不可用）");
            }
            if (lightSource != null)
            {
                var lightCopy = Object.Instantiate(lightSource);
                SceneManager.MoveGameObjectToScene(lightCopy, start);
            }
            if (hubSource != null && (xrRoot == null || (hubRoot != xrRoot && !hubRoot.IsChildOf(xrRoot))))
            {
                // 只克隆 hub 物体本身——克隆 transform.root 会把整个 RealSpace 世界（含它自带的 XR Origin 相机）拖进 StartScene
                var hubCopy = Object.Instantiate(hubSource.gameObject);
                SceneManager.MoveGameObjectToScene(hubCopy, start);
            }
            SceneManager.MoveGameObjectToScene(startGo, start);
            var ctrl = startGo.GetComponent<MoriGame.Tutorial.StartSceneController>();
            ctrl.hub = xrCopy != null
                ? xrCopy.GetComponentInChildren<MoriGame.Gestures.PicoGestureHub>(true)
                : startGo.scene.GetRootGameObjects()
                    .Select(r => r.GetComponentInChildren<MoriGame.Gestures.PicoGestureHub>(true))
                    .FirstOrDefault(h => h != null);
            ctrl.font = font; // 字体资产跨场景引用有效
            foreach (var r in start.GetRootGameObjects())
            {
                Debug.Log("[MoriGame] StartScene root: " + r.name);
            }
            EditorSceneManager.SaveScene(start, StartScene);
            EditorSceneManager.CloseScene(start, true);
            Debug.Log("[MoriGame] StartScene.unity 构建完成（XR Origin+相机/手势 hub/路由面板）");

            // ---------- ③ Build Settings ----------
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(StartScene, true),
                new EditorBuildSettingsScene(TutorialScene, true),
                new EditorBuildSettingsScene(MainScene, true)
            };
            Debug.Log("[MoriGame] Build Settings 场景顺序：StartScene → Tutorial → MoriMain（" +
                      string.Join("/", EditorBuildSettings.scenes.Select(s => s.path)) + "）");

            EditorSceneManager.OpenScene(MainScene); // 还原工作场景
        }

        /// <summary>编辑器可视化（preview-startui.trigger）：首次打开 StartScene 烘焙选择页；已有预览原样保留手调。
        /// 运行时 StartSceneController.Start() 读取菜单/弹窗位姿偏移，再删除预览层并重建实时 UI。</summary>
        public static void PreviewStartUi()
        {
            EditorSceneManager.OpenScene(StartScene);
            var cam = Object.FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
            var ctrl = Object.FindFirstObjectByType<MoriGame.Tutorial.StartSceneController>();
            if (cam == null || ctrl == null)
            {
                throw new System.InvalidOperationException(
                    "StartScene 缺相机或 StartSceneController——先跑 build-tutorial.trigger 重建场景");
            }
            ctrl.BuildPreview();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), StartScene);
            Debug.Log("[MoriGame] StartScene UI 预览已就绪（已有手调原样保留，运行时转为位姿偏移）");
        }
    }
}
