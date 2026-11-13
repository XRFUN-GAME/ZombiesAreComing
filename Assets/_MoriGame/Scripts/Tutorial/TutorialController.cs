using ByteDance.PICO.XR;
using MoriGame.Build;
using MoriGame.Combat;
using MoriGame.Gestures;
using MoriGame.Shop;
using MoriGame.Waves;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MoriGame.Ui;

namespace MoriGame.Tutorial
{
    /// <summary>
    /// M2-T0 新手引导四件套控制器：①交互式清单（准备期 12 秒四步实操，检测真实手势事件打勾）
    /// ②JITHint 情境提示（两类，每类每局限 2 次）③常驻手势图例（右橙，操作满 3 次淡出）
    /// ④左蓝右橙配色贯穿（手模腕部色带/图例/文案着色统一）。
    /// **2026-11-05 站桩工作台制（docs/07）：四步=抓枪→射击→抓建筑→放置（全程站桩不动）。**
    /// **2026-11-06：侧推转向移除——转向教学行/左图例/被咬转身提示（HurtNoTurn）一并撤除。**
    /// 纯逻辑在 TutorialProgress.cs（EditMode 单测）；本类只做信号采集、UI 渲染与色带挂接。
    /// </summary>
    public class TutorialController : MonoBehaviour
    {
        [Header("引用（MoriSceneSetup 注入）")]
        public PicoGestureHub hub;
        public WeaponController weapon;
        public WeaponHolster holster;
        public BuildManager build;
        public WaveDirector director;
        public TMP_FontAsset font;

        [Header("准备期（读 MatchPhases phases[0].endSeconds，注入）")]
        public float prepSeconds = 30f;

        [Header("JIT 提示")]
        public float hintDuration = 3f;

        [Header("配色（左蓝右橙贯穿）")]
        public Color leftBlue = new Color(0.35f, 0.62f, 1f);
        public Color rightOrange = new Color(1f, 0.6f, 0.2f);

        // ---- 纯逻辑状态 ----
        private readonly TutorialChecklist checklist = new TutorialChecklist();
        private bool inTutorialScene; // 步骤清单 UI 仅教学关构建（M4-T0）
        private readonly JitHintBudget hints = new JitHintBudget();
        private readonly LegendFadeTracker legend = new LegendFadeTracker();

        // ---- 信号前值（边沿检测）----
        private int lastHolsterGrabs;
        private int lastPlaced;
        private int lastShots;
        private int lastGrabCount;

        // ---- 清单阶段 ----
        private bool prepActive = true;   // 准备期内 true；AllDone 或超时后 false（清单淡出，JIT/图例继续）
        private float checklistFade;      // 收起淡出进度

        // ---- JIT 状态 ----
        private bool firstWaveFired;      // ElapsedSeconds 跨 prepSeconds 边沿
        private float hintRemaining;      // 当前 JIT 条剩余显示时间
        private float nextStandHintAt = 12f; // 展示位闲置提示节奏（12 秒未抓起首提，之后每 12 秒，预算封顶）

        // ---- 手模色带（左蓝右橙；手骨就绪后挂一次）----
        private bool bandsAttached;

        private TMP_Text checklistTitle;
        private TMP_Text[] checklistRows;
        private TMP_Text checklistTimer;
        private CanvasGroup checklistGroup;
        private TMP_Text hintText;
        private CanvasGroup hintGroup;
        private TMP_Text legendRight;
        private CanvasGroup legendRightGroup;
        private TMP_Text rotateHint;

        private static readonly string[] StepLines =
        {
            "① 右手伸向右侧插槽，捏合抓枪",
            "② 食指伸直，射击一次",
            "③ 伸向左侧建筑，右手捏合抓起",
            "④ 右捏放置 · 左捏旋转 · 张掌取消"
        };

        private void Start()
        {
            // 步骤清单 UI 仅教学关构建（M4-T0：主对局删步骤清单，四件套移入教学关；JITHint/图例/腕带照旧）
            inTutorialScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Tutorial";
            if (inTutorialScene)
            {
                BuildUi();
            }
        }

        /// <summary>四步全勾（教学关毕业检测，TutorialFlow 用）</summary>
        public bool ChecklistAllDone => checklist.AllDone;

        /// <summary>重新演示：清单清零 + UI 行复位（教学关「重新演示」按钮，M4-T0）</summary>
        public void ResetTutorial()
        {
            checklist.ResetAll();
            if (checklistRows == null)
            {
                return;
            }
            for (var i = 0; i < checklistRows.Length; i++)
            {
                checklistRows[i].text = "□ " + StepLines[i];
                checklistRows[i].color = i == 0 ? Color.white : new Color(0.75f, 0.78f, 0.82f);
            }
        }

        public void SetUiVisible(bool visible)
        {
            if (uiRoot != null)
            {
                uiRoot.gameObject.SetActive(visible);
            }
        }

        private void Update()
        {
            AttachHandBands();

            var elapsed = director != null && director.Scheduler != null
                ? director.Scheduler.ElapsedSeconds : 0f;

            CollectSignals();
            TickFirstWaveHint(elapsed);
            TickStandIdleHint(elapsed);
            TickChecklist(elapsed);
            TickLegend();
        }

        // ---------- 信号采集与步骤判定 ----------

        private void CollectSignals()
        {
            if (holster != null && WeaponHolster.GrabCount != lastHolsterGrabs)
            {
                lastHolsterGrabs = WeaponHolster.GrabCount;
                legend.Increment(TutorialAction.Confirm);
                checklist.MarkDone(TutorialStep.GrabWeapon); // 抓起枪=步骤①（抓起即装备）
            }
            if (weapon != null && weapon.ShotCount != lastShots)
            {
                lastShots = weapon.ShotCount;
                legend.Increment(TutorialAction.Fire);
                checklist.MarkDone(TutorialStep.FireOnce); // 射击一次=步骤②
            }
            if (ShopStand.TotalGrabCount != lastGrabCount)
            {
                lastGrabCount = ShopStand.TotalGrabCount;
                legend.Increment(TutorialAction.Confirm);
                checklist.MarkDone(TutorialStep.GrabStand); // 抓起建筑=步骤③（抓起即购买）
            }
            if (build != null && build.PropsPlacedCount != lastPlaced)
            {
                lastPlaced = build.PropsPlacedCount;
                legend.Increment(TutorialAction.Confirm);
                checklist.MarkDone(TutorialStep.PlaceDown); // 落地放置=步骤④
            }
        }

        private void TickFirstWaveHint(float elapsed)
        {
            if (!firstWaveFired && elapsed >= prepSeconds)
            {
                firstWaveFired = true;
                ShowHint("右手食指伸直=开火，指向僵尸即射击", rightOrange);
            }
        }

        private void TickStandIdleHint(float elapsed)
        {
            if (checklist.IsDone(TutorialStep.GrabStand) || elapsed < nextStandHintAt)
            {
                return;
            }
            if (hints.TryConsume(JitHintKind.StandIdle))
            {
                ShowHint("右手伸向展示位模型，捏合=抓起（抓起即购买）", rightOrange);
            }
            nextStandHintAt = elapsed + 12f; // 预算耗尽也推进节奏，防每帧 TryConsume
        }

        private void TickChecklist(float elapsed)
        {
            if (inTutorialScene) // 教学关：清单常驻无倒计时（无波次压力，M4-T0）
            {
                if (checklistGroup == null)
                {
                    return;
                }
                checklistGroup.alpha = 1f;
                checklistTimer.text = "跟着做";
                for (var i = 0; i < checklistRows.Length; i++)
                {
                    var done = checklist.IsDone((TutorialStep)i);
                    var current = checklist.CurrentStep == i;
                    checklistRows[i].text = (done ? "✓ " : "□ ") + StepLines[i];
                    checklistRows[i].color = done ? new Color(0.45f, 1f, 0.6f)
                        : current ? Color.white : new Color(0.75f, 0.78f, 0.82f);
                }
                return;
            }
            if (prepActive && (checklist.AllDone || elapsed >= prepSeconds))
            {
                prepActive = false; // 全部完成或 30 秒到 → 进战斗，清单淡出
            }
            if (checklistGroup == null)
            {
                return;
            }
            if (prepActive)
            {
                checklistGroup.alpha = 1f;
                var remain = Mathf.Max(0f, prepSeconds - elapsed);
                checklistTimer.text = $"跟着做 · {remain:0} 秒后开战";
                for (var i = 0; i < checklistRows.Length; i++)
                {
                    var done = checklist.IsDone((TutorialStep)i);
                    var current = checklist.CurrentStep == i;
                    checklistRows[i].text = (done ? "✓ " : "□ ") + StepLines[i];
                    checklistRows[i].color = done ? new Color(0.45f, 1f, 0.6f)
                        : current ? Color.white : new Color(0.75f, 0.78f, 0.82f);
                }
            }
            else if (checklistGroup.alpha > 0f)
            {
                checklistFade += Time.deltaTime / 0.8f;
                checklistGroup.alpha = Mathf.Clamp01(1f - checklistFade);
                if (checklistGroup.alpha <= 0f)
                {
                    checklistGroup.gameObject.SetActive(false);
                }
            }
        }

        private void TickLegend()
        {
            FadeLabel(legendRightGroup, legend.RightVisible);
        }

        private static void FadeLabel(CanvasGroup group, bool visible)
        {
            if (group == null)
            {
                return;
            }
            var target = visible ? 1f : 0f;
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime / 1.2f);
        }

        private void ShowHint(string message, Color color)
        {
            if (hintText == null)
            {
                return;
            }
            hintText.gameObject.SetActive(true);
            hintText.text = message;
            hintText.color = color;
            hintGroup.alpha = 1f;
            hintRemaining = hintDuration;
        }

        private void LateUpdate()
        {
            if (hintGroup != null && hintRemaining > 0f)
            {
                hintRemaining -= Time.deltaTime;
                var t = Mathf.Clamp01(hintRemaining / 0.8f); // 尾段 0.8 秒线性淡出
                hintGroup.alpha = hintRemaining <= 0f ? 0f : Mathf.Min(1f, t);
                if (hintRemaining <= 0f)
                {
                    hintText.gameObject.SetActive(false);
                }
            }
        }

        // ---------- 手模腕部色带（左蓝右橙贯穿：手模描边灰盒实现=腕背色带，正式美术到位再换描边） ----------

        private void AttachHandBands()
        {
            if (bandsAttached || hub == null)
            {
                return;
            }
            const int wristJoint = (int)HandJoint.JointWrist;
            if (hub.leftHand == null || hub.rightHand == null ||
                hub.leftHand.handJoints == null || hub.rightHand.handJoints == null ||
                hub.leftHand.handJoints.Count <= wristJoint || hub.rightHand.handJoints.Count <= wristJoint ||
                hub.leftHand.handJoints[wristJoint] == null || hub.rightHand.handJoints[wristJoint] == null)
            {
                return; // PXR_Hand 关节表就绪后再挂（编辑器搭建期列表可能为空）
            }
            AttachBand(hub.leftHand.handJoints[wristJoint], leftBlue, "HandBand_Left");
            AttachBand(hub.rightHand.handJoints[wristJoint], rightOrange, "HandBand_Right");
            bandsAttached = true;
        }

        private static void AttachBand(Transform wrist, Color color, string name)
        {
            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            band.name = name;
            band.transform.SetParent(wrist, false);
            band.transform.localPosition = new Vector3(0f, 0.008f, 0.028f); // 腕背侧（灰盒朝向，真机不可读再调）
            band.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);
            band.transform.localScale = new Vector3(0.055f, 0.012f, 0.02f);
            Object.Destroy(band.GetComponent<Collider>());
            var renderer = band.GetComponent<MeshRenderer>();
            // CreatePrimitive 默认材质=内置 Standard（URP 下洋红，被看成红色长方体）——换 Sprites/Default（URP 兼容，2026-11-11 修复）
            renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = color };
            // 挂在腕骨下会随 PicoGestureHub.SetHandRenderers 的显隐扫描一起管理（藏手时色带同步隐藏）
        }

        // ---------- UI 构建（世界固定侧后方大版布局，避开前方靶场/出怪视线） ----------

        private Transform uiRoot;

        private void BuildUi()
        {
            if (font == null)
            {
                enabled = false;
                return;
            }
            uiRoot = new GameObject("TutorialUi").transform;

            BuildChecklist(uiRoot);
            BuildHintBar(uiRoot);
            BuildLegend(uiRoot);
        }

        private void BuildChecklist(Transform root)
        {
            var go = new GameObject("Checklist", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * 0.02f;
            UiTheme.PlaceFixedPanel(go.transform, WorldUiPanels.TutorialChecklist, 70f, 2.4f, 0.95f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(104f, 52f);
            checklistGroup = go.AddComponent<CanvasGroup>();

            var bg = go.AddComponent<Image>();
            bg.color = UiTheme.PanelBg;
            bg.raycastTarget = false;
            UiTheme.AddUguiBorder(rt, UiTheme.PanelBorder);

            checklistTitle = CreateText(rt, "Title", "新手引导", 4.4f, new Vector2(0f, 21.5f), new Vector2(96f, 7f),
                UiTheme.TextMain);
            checklistTimer = CreateText(rt, "Timer", "", 3.4f, new Vector2(0f, 14.5f), new Vector2(96f, 5f),
                UiTheme.AccentCyan);
            checklistRows = new TMP_Text[TutorialChecklist.StepCount];
            for (var i = 0; i < checklistRows.Length; i++)
            {
                var y = 6.5f - i * 8.5f;
                checklistRows[i] = CreateText(rt, "Step" + i, StepLines[i], 4f,
                    new Vector2(0f, y), new Vector2(96f, 8f));
            }
        }

        private void BuildHintBar(Transform root)
        {
            hintGroup = BuildLabel(root, "JitHint", Vector3.zero, new Vector2(80f, 10f),
                3.4f, out hintText);
            hintGroup.transform.localScale = Vector3.one * 0.02f;
            UiTheme.PlaceFixedPanel(hintGroup.transform, WorldUiPanels.TutorialJitHint, 180f, 2.3f, 0.45f);
            hintText.gameObject.SetActive(false);
        }

        private void BuildLegend(Transform root)
        {
            legendRightGroup = BuildLabel(root, "LegendRight", Vector3.zero,
                new Vector2(68f, 7f), 3.2f, out legendRight);
            legendRightGroup.transform.localScale = Vector3.one * 0.02f;
            UiTheme.PlaceFixedPanel(legendRightGroup.transform, WorldUiPanels.TutorialLegendRight, 120f, 2.3f, 1.25f);
            legendRight.text = "右捏=抓取 · 食指伸直=开火";
            legendRight.color = rightOrange;

            var leftGroup = BuildLabel(root, "RotateHint", Vector3.zero,
                new Vector2(88f, 12f), 3.2f, out rotateHint);
            leftGroup.transform.localScale = Vector3.one * 0.02f;
            UiTheme.PlaceFixedPanel(leftGroup.transform, WorldUiPanels.TutorialRotateHint, -115f, 2.3f, 1.15f);
            rotateHint.text = "左捏=旋转 · 右捏=放置 · 张掌=取消\n越界自动吸附可放边缘";
            rotateHint.color = leftBlue;
        }

#if UNITY_EDITOR
        public GameObject[] BuildEditorPreviews()
        {
            BuildUi();
            if (uiRoot == null)
            {
                return System.Array.Empty<GameObject>();
            }
            checklistTimer.text = "跟着做";
            hintText.text = "指向僵尸，右手食指伸直开火";
            hintText.gameObject.SetActive(true);
            hintGroup.alpha = 1f;
            var panels = new[]
            {
                checklistGroup.gameObject,
                hintGroup.gameObject,
                legendRightGroup.gameObject,
                rotateHint.transform.parent.gameObject
            };
            Object.DestroyImmediate(uiRoot.gameObject);
            uiRoot = null;
            return panels;
        }
#endif

        private CanvasGroup BuildLabel(Transform root, string name, Vector3 localPos, Vector2 size,
            float fontSize, out TMP_Text text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            var group = go.AddComponent<CanvasGroup>();
            var bg = go.AddComponent<Image>();
            bg.color = UiTheme.PanelBg;
            bg.raycastTarget = false;
            UiTheme.AddUguiBorder(rt, UiTheme.PanelBorder); // WW Polygon 米白粗框（2026-11-12）
            text = CreateText(rt, "Label", "", fontSize, Vector2.zero, size - new Vector2(2f, 1f));
            text.color = UiTheme.TextMain;
            return group;
        }

        private TMP_Text CreateText(Transform parent, string name, string content, float fontSize,
            Vector2 pos, Vector2 size, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color ?? Color.white;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }
    }
}
