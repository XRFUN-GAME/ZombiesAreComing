using MoriGame.Gestures;
using TMPro;
using UnityEngine;
using MoriGame.Ui;

namespace MoriGame.Tutorial
{
    /// <summary>教学关流程（2026-11-11 M4-T0，站桩制教学场景；2026-11-13 练习靶自动补员）：
    /// 靶场常驻 4 只不动练习靶（dummy 无威胁），死亡动画结束并回池后在原槽位补足；
    /// 四步实操由 TutorialController 检测打勾；四步全勾 → 毕业面板 → 写毕业标记 → 3 秒淡出加载主对局。
    /// 过程常驻「重新演示（蓝）/跳过（橙）」按钮（凝视+捏合，跳过=直接毕业）。
    /// 无波次/无计时/无经济压力（场景删除 WaveDirector/补给箱）。</summary>
    public class TutorialFlow : MonoBehaviour
    {
        public PicoGestureHub hub;
        public TMP_FontAsset font; // builder 注入中文字体
        public int dummyCount = 4;

        private MatchBootstrap match;
        private TutorialController tutorial;
        private bool graduateShown;
        private float loadAt = -1f;
        private bool leaving;
        private GameObject graduatePanel;
        private Transform redoButton;
        private Transform skipButton;
        private Zombies.ZombieAgent[] dummySlots;
        private float nextDummyMaintenanceAt;
        private const float DummyMaintenanceInterval = 0.2f;

        private void Start()
        {
            SceneLoader.PlayFadeIn();
            TutorialSession.Active = true; // 教学会话硬闸：僵尸全系统禁索敌禁攻击
            match = MatchBootstrap.Instance;
            tutorial = FindFirstObjectByType<TutorialController>();
            if (match != null && match.Wallet != null)
            {
                match.Wallet.Add(99999); // 教学关金币无限（购买走正常流程自然成功）
            }
            SpawnDummies();
        }

        private void OnDestroy()
        {
            TutorialSession.Active = false;
        }

        private void Update()
        {
            if (leaving)
            {
                return;
            }
            if (Time.time >= nextDummyMaintenanceAt)
            {
                nextDummyMaintenanceAt = Time.time + DummyMaintenanceInterval;
                MaintainDummies();
            }

            // 四步全勾（TutorialController 检测）→ 毕业：写标记 → 3 秒后加载主对局
            if (!graduateShown && tutorial != null && tutorial.ChecklistAllDone)
            {
                TutorialGraduation.SetGraduated();
                graduateShown = true;
                loadAt = Time.time + 3f;
                tutorial.SetUiVisible(false);
                BuildGraduatePanel();
                Debug.Log("[MoriGame] 教学毕业：标记已写入，3 秒后进入主对局");
            }
            if (graduateShown && loadAt > 0f && Time.time >= loadAt)
            {
                Leave("MoriMain");
                return;
            }

            HandleButtons();
        }

        // ---------- 靶场 ----------

        private void SpawnDummies()
        {
            var pool = FindFirstObjectByType<Zombies.ZombiePool>();
            var anchor = match != null ? match.playSpaceAnchor : null;
            if (pool == null || anchor == null)
            {
                return;
            }
            dummySlots = new Zombies.ZombieAgent[Mathf.Max(0, dummyCount)];
            for (var i = 0; i < dummySlots.Length; i++)
            {
                SpawnDummySlot(pool, anchor, i);
            }
        }

        private void MaintainDummies()
        {
            var pool = FindFirstObjectByType<Zombies.ZombiePool>();
            var anchor = match != null ? match.playSpaceAnchor : null;
            if (pool == null || anchor == null)
            {
                return;
            }
            if (dummySlots == null || dummySlots.Length != dummyCount)
            {
                dummySlots = new Zombies.ZombieAgent[Mathf.Max(0, dummyCount)];
            }
            for (var i = 0; i < dummySlots.Length; i++)
            {
                var current = dummySlots[i];
                if (current == null || !current.gameObject.activeSelf)
                {
                    SpawnDummySlot(pool, anchor, i);
                }
            }
        }

        private void SpawnDummySlot(Zombies.ZombiePool pool, Transform anchor, int slot)
        {
            var angle = (slot - (dummyCount - 1) * 0.5f) * 22f;
            var pos = anchor.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 3.5f;
            var zombie = pool.Spawn("Normal", pos);
            if (zombie != null)
            {
                zombie.dummyMode = true;
                dummySlots[slot] = zombie;
            }
        }

        // ---------- 毕业面板 ----------

        private void BuildGraduatePanel()
        {
            graduatePanel = UiTheme.FramedQuad(null, "GraduatePanel", new Vector2(2f, 0.65f),
                UiTheme.PanelBg, 0.06f);
            UiTheme.PlaceFixedPanel(graduatePanel.transform, WorldUiPanels.TutorialGraduate, 70f, 2.4f, 0.95f);
            UiTheme.AccentStrip(graduatePanel, UiTheme.AccentOrange, 0.06f);
            var textGo = new GameObject("Title");
            textGo.transform.SetParent(graduatePanel.transform, false);
            textGo.transform.localPosition = new Vector3(0.03f, 0f, -0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.text = "训练完成！即将进入正式关卡";
            tmp.fontSize = 1.4f;
            tmp.color = UiTheme.TextMain;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(1.92f, 0.6f);
        }

        // ---------- 重新演示 / 跳过 ----------

        private void HandleButtons()
        {
            if (hub == null || !hub.IsTracked(GestureHand.Right))
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null || !Physics.Raycast(
                    new Ray(cam.transform.position, cam.transform.forward), out var hit, 4f))
            {
                return;
            }
            var name = hit.transform.name;
            var picked = name == "BtnRedo" || name == "BtnSkip";
            if (!picked || !hub.PinchDown(GestureHand.Right))
            {
                return;
            }
            if (name == "BtnRedo")
            {
                Redo();
            }
            else
            {
                Skip();
            }
        }

        /// <summary>重新演示：清单清零 + 靶场补足（毕业面板隐藏）</summary>
        public void Redo()
        {
            if (graduatePanel != null)
            {
                Destroy(graduatePanel);
            }
            graduateShown = false;
            loadAt = -1f;
            if (tutorial != null)
            {
                tutorial.SetUiVisible(true);
                tutorial.ResetTutorial();
            }
            MaintainDummies();
        }

        /// <summary>跳过 = 直接毕业（写标记）进主对局</summary>
        public void Skip()
        {
            TutorialGraduation.SetGraduated();
            Leave("MoriMain");
        }

        private void Leave(string scene)
        {
            if (leaving)
            {
                return;
            }
            leaving = true;
            SceneLoader.FadeTo(scene);
        }

        // ---------- 常驻按钮（凝视+捏合，左蓝右橙） ----------

        private bool buttonsBuilt;
        private float buttonsBuildAt = 3f;

        private void LateUpdate()
        {
            if (buttonsBuilt || Time.time < buttonsBuildAt)
            {
                return;
            }
            buttonsBuilt = true;
            redoButton = BuildButton("BtnRedo", WorldUiPanels.TutorialRedo, "重新演示", UiTheme.AccentBlue, -155f);
            skipButton = BuildButton("BtnSkip", WorldUiPanels.TutorialSkip, "跳过", UiTheme.AccentOrange, 155f);
            Physics.SyncTransforms();
        }

        private Transform BuildButton(string name, string panelId, string label, Color color, float yawOffsetDegrees)
        {
            var go = UiTheme.FramedQuad(null, name, new Vector2(0.54f, 0.26f), UiTheme.PanelBg);
            UiTheme.PlaceFixedPanel(go.transform, panelId, yawOffsetDegrees, 2.3f, 0.85f);
            UiTheme.AccentStrip(go, color);
            go.AddComponent<BoxCollider>().size = new Vector3(0.54f, 0.26f, 0.02f);
            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.text = label;
            tmp.fontSize = 1f;
            tmp.color = UiTheme.TextMain;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.23f);
            return go.transform;
        }

#if UNITY_EDITOR
        public GameObject[] BuildEditorPreviews()
        {
            BuildGraduatePanel();
            redoButton = BuildButton("BtnRedo", WorldUiPanels.TutorialRedo,
                "重新演示", UiTheme.AccentBlue, -155f);
            skipButton = BuildButton("BtnSkip", WorldUiPanels.TutorialSkip,
                "跳过", UiTheme.AccentOrange, 155f);
            return new[] { graduatePanel, redoButton.gameObject, skipButton.gameObject };
        }
#endif

        /// <summary>UI 四边面片（凝视按钮共用）</summary>
        public static class UiQuadMesh
        {
            private static Mesh cached;

            public static Mesh Get(float halfW, float halfH)
            {
                if (cached == null)
                {
                    cached = new Mesh { name = "TutorialUIQuad" };
                    cached.vertices = new[]
                    {
                        new Vector3(-halfW, -halfH, 0f), new Vector3(halfW, -halfH, 0f),
                        new Vector3(-halfW, halfH, 0f), new Vector3(halfW, halfH, 0f)
                    };
                    cached.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                    cached.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    cached.RecalculateNormals();
                }
                return cached;
            }
        }
    }
}
