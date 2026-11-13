using MoriGame.Gestures;
using MoriGame.Ui;
using TMPro;
using UnityEngine;

namespace MoriGame.Tutorial
{
    /// <summary>StartScene 启动控制器（2026-11-12 用户定版二）：《僵尸来袭》标题 +
    /// 新手教学 / 开始游戏（未毕业锁定）/ 退出游戏 三选项；UI 全程跟随头部（眼高锚定，
    /// 坐姿不落地）；凝视悬停高亮（锁定项永不高亮、捏合无效）；凝视+捏合选择。</summary>
    public class StartSceneController : MonoBehaviour
    {
        public PicoGestureHub hub;
        public TMP_FontAsset font; // builder 注入中文字体（主场景同源）

        public const string PreviewRootName = "UiPreview (EditorOnly)";

        private enum MenuAction { Tutorial, Match, Quit, QuitConfirm, QuitCancel }

        private sealed class MenuOption
        {
            public Transform root;
            public Transform label;
            public Color accent;
            public Color labelColor;
            public bool locked;
            public MenuAction action;
        }

        private GameObject uiOwner; // UI 总根（运行时 UiLive / 编辑器预览 UiPreview）
        private GameObject choicePanel;
        private System.Collections.Generic.List<MenuOption> options;
        private MenuOption hovered;
        private bool choosing;
        private GameObject confirmDialog;
        private System.Collections.Generic.List<MenuOption> confirmOptions;
        private bool confirming; // 退出二次确认弹窗打开中（期间主菜单选项全部接管失效）
        private static readonly Vector3 DefaultPreviewPosition = new Vector3(0f, 1.35f, 1.2f);
        private static readonly Quaternion DefaultPreviewRotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        private Vector3 previewPositionOffset;
        private Quaternion previewRotationOffset = Quaternion.identity;
        private Vector3 previewScale = Vector3.one;
        private Vector3 confirmPreviewPosition;
        private Quaternion confirmPreviewRotation = Quaternion.identity;
        private bool hasConfirmPreviewTransform;

        private void Start()
        {
            var stale = GameObject.Find(PreviewRootName);
            if (stale != null)
            {
                CapturePreviewTransforms(stale.transform);
                Destroy(stale); // 烘焙进场景的编辑器预览层不进运行时，位姿偏移已转交实时 UI
            }
            HideCarriedWeapon(); // 启动页无武器逻辑：Rig 上挂的枪模整体隐藏
            uiOwner = new GameObject("UiLive");
            SceneLoader.PlayFadeIn();
            BuildChoicePanel();
        }

        /// <summary>编辑器预览入口（TutorialScenesBuilder.PreviewStartUi 调用）：静态烘焙 UI，免 Play 查看</summary>
        public void BuildPreview()
        {
            var existing = GameObject.Find(PreviewRootName);
            if (existing != null)
            {
                uiOwner = existing;
                var existingPanel = existing.transform.Find("ChoicePanel");
                if (existingPanel != null)
                {
                    choicePanel = existingPanel.gameObject;
                    return; // 已有预览是用户手调资产，重跑入口只保留、不复位
                }
            }
            else
            {
                uiOwner = new GameObject(PreviewRootName);
            }
            BuildChoicePanel();
        }

        private void CapturePreviewTransforms(Transform previewRoot)
        {
            var panel = previewRoot.Find("ChoicePanel");
            if (panel == null)
            {
                return;
            }
            previewPositionOffset = panel.position - DefaultPreviewPosition;
            previewRotationOffset = Quaternion.Inverse(DefaultPreviewRotation) * panel.rotation;
            previewScale = panel.lossyScale;
            var confirm = panel.Find("ConfirmQuit");
            if (confirm == null)
            {
                return;
            }
            confirmPreviewPosition = confirm.localPosition;
            confirmPreviewRotation = confirm.localRotation;
            hasConfirmPreviewTransform = true;
        }

        private static void HideCarriedWeapon()
        {
            foreach (var w in FindObjectsByType<Combat.WeaponController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                w.gameObject.SetActive(false);
            }
            foreach (var h in FindObjectsByType<Combat.WeaponHolster>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                h.gameObject.SetActive(false);
            }
        }

        private void BuildChoicePanel()
        {
            choicePanel = new GameObject("ChoicePanel");
            choicePanel.transform.SetParent(uiOwner.transform, false);
            PlacePanel(); // 编辑器预览：固定取景位；运行时 Update 每帧按真实头部重定位
            choicePanel.transform.localScale = previewScale;

            var graduated = TutorialGraduation.IsGraduated();
            options = new System.Collections.Generic.List<MenuOption>();

            // ── 标题版头：《僵尸来袭》（WW Polygon 构图：红箭头条 + 大标题 + 技术注记）──
            var header = UiTheme.ChamferQuad(choicePanel.transform, "Header", UiTheme.PanelBg,
                new Vector2(0.92f, 0.26f), 0f);
            header.transform.localPosition = new Vector3(0f, 0.30f, 0f);
            UiTheme.AccentStrip(header, UiTheme.AccentRed);
            // TMP 世界空间字号语义：实际字形高 ≈ fontSize÷10（0.115 只渲染 1.15 厘米——此前一直偏小的根因）
            MakeLabel(header.transform, "Title", "僵尸来袭", 1.15f, UiTheme.TextMain,
                new Vector2(0.74f, 0.16f), new Vector3(0f, 0.042f, -0.004f), 0.12f);
            MakeLabel(header.transform, "Caption", "// ZOMBIE SURGE · MAIN MENU", 0.32f, UiTheme.TextDim,
                new Vector2(0.74f, 0.07f), new Vector3(0f, -0.072f, -0.004f), 0.2f);
            MakeLabel(header.transform, "Tag", "MORI//DEFENSE", 0.34f, UiTheme.TextDim,
                new Vector2(0.22f, 0.06f), new Vector3(0.266f, 0.107f, -0.004f), 0.22f);

            options.Add(BuildOption("新手教学", "// TUTORIAL", UiTheme.AccentBlue, UiTheme.TextMain,
                UiTheme.TextDim, 0.2f, false, MenuAction.Tutorial, 0.04f));
            options.Add(BuildOption("开始游戏",
                graduated ? "// OPERATION" : "锁定 · 未通关教学",
                graduated ? UiTheme.AccentOrange : UiTheme.LockedBg,
                graduated ? UiTheme.TextMain : UiTheme.TextDim,
                graduated ? UiTheme.TextDim : UiTheme.AccentRed,
                0.06f, !graduated, MenuAction.Match, -0.18f, graduated ? 0.32f : 0.3f));
            options.Add(BuildOption("退出游戏", "// QUIT", UiTheme.AccentRed, UiTheme.TextMain,
                UiTheme.TextDim, 0.2f, false, MenuAction.Quit, -0.40f));
            BuildConfirmDialog();
            choosing = true;
        }

        /// <summary>退出二次确认弹窗（防误触）：凝视"退出游戏"+捏合后展开，盖在菜单按钮区上方；
        /// 打开期间仅弹窗两个按钮响应凝视+捏合。编辑器预览默认展开便于查看微调，运行时隐藏待触发。</summary>
        private void BuildConfirmDialog()
        {
            confirmDialog = new GameObject("ConfirmQuit");
            confirmDialog.transform.SetParent(choicePanel.transform, false);
            confirmDialog.transform.localPosition = hasConfirmPreviewTransform
                ? confirmPreviewPosition
                : new Vector3(0f, -0.18f, -0.012f); // 菜单按钮区上方，略靠前避让
            confirmDialog.transform.localRotation = hasConfirmPreviewTransform
                ? confirmPreviewRotation
                : Quaternion.identity;
            confirmDialog.SetActive(false);
            confirmOptions = new System.Collections.Generic.List<MenuOption>();

            var panel = UiTheme.FramedQuad(confirmDialog.transform, "Panel", new Vector2(0.50f, 0.24f),
                UiTheme.PanelBg);
            UiTheme.AccentStrip(panel, UiTheme.AccentRed);
            MakeLabel(panel.transform, "Title", "确认退出游戏？", 0.5f, UiTheme.TextMain,
                new Vector2(0.44f, 0.09f), new Vector3(0f, 0.075f, -0.004f), 0.1f);
            MakeLabel(panel.transform, "Hint", "// CONFIRM TO QUIT", 0.26f, UiTheme.TextDim,
                new Vector2(0.44f, 0.05f), new Vector3(0f, 0.025f, -0.004f), 0.18f);

            confirmOptions.Add(BuildConfirmButton("确认退出", MenuAction.QuitConfirm, UiTheme.AccentRed, -0.115f));
            confirmOptions.Add(BuildConfirmButton("取消", MenuAction.QuitCancel, UiTheme.AccentBlue, 0.115f));
            if (!Application.isPlaying)
            {
                confirmDialog.SetActive(true); // 编辑器预览：展开供查看/微调（运行时由 ShowConfirm 展开）
            }
        }

        private MenuOption BuildConfirmButton(string label, MenuAction action, Color accent, float x)
        {
            var go = UiTheme.FramedQuad(confirmDialog.transform, "CBtn_" + label,
                new Vector2(0.20f, 0.13f), UiTheme.PanelBg);
            go.transform.localPosition = new Vector3(x, -0.055f, -0.004f);
            UiTheme.AccentStrip(go, accent);
            go.AddComponent<BoxCollider>().size = new Vector3(0.20f, 0.13f, 0.02f);
            var labelT = MakeLabel(go.transform, "Label", label, 0.42f, UiTheme.TextMain,
                new Vector2(0.18f, 0.08f), new Vector3(0f, 0f, -0.004f), 0.06f);
            return new MenuOption
            {
                root = go.transform,
                label = labelT,
                accent = accent,
                labelColor = UiTheme.TextMain,
                locked = false,
                action = action
            };
        }

        /// <summary>面板定位：编辑器预览用固定取景位（抬头可见高度）；运行时跟随头部（眼高锚定，
        /// 坐姿不落地——此前 Start 读到的 XR 位姿未追踪导致 UI 在脚平面，2026-11-12 修复）</summary>
        private void PlacePanel()
        {
            if (!Application.isPlaying)
            {
                choicePanel.transform.SetPositionAndRotation(
                    DefaultPreviewPosition, DefaultPreviewRotation);
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f)
            {
                fwd = Vector3.forward;
            }
            var baseRotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var basePosition = cam.transform.position + fwd.normalized * 1.2f;
            choicePanel.transform.SetPositionAndRotation(
                basePosition + previewPositionOffset, baseRotation * previewRotationOffset);
        }

        private MenuOption BuildOption(string label, string caption, Color accent, Color labelColor,
            Color captionColor, float captionTracking, bool locked, MenuAction action, float y,
            float captionSize = 0.32f)
        {
            var go = UiTheme.FramedQuad(choicePanel.transform, "Btn_" + label, new Vector2(0.46f, 0.18f),
                locked ? UiTheme.LockedBg : UiTheme.PanelBg);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            UiTheme.AccentStrip(go, accent);
            go.AddComponent<BoxCollider>().size = new Vector3(0.46f, 0.18f, 0.02f);
            var labelT = MakeLabel(go.transform, "Label", label, 0.68f, labelColor,
                new Vector2(0.42f, 0.10f), new Vector3(0.012f, 0.03f, -0.004f), 0.08f);
            MakeLabel(go.transform, "Caption", caption, captionSize, captionColor,
                new Vector2(0.44f, 0.06f), new Vector3(0.012f, -0.048f, -0.004f), captionTracking);
            return new MenuOption
            {
                root = go.transform,
                label = labelT,
                accent = accent,
                labelColor = labelColor,
                locked = locked,
                action = action
            };
        }

        private Transform MakeLabel(Transform parent, string objName, string content, float fontSize,
            Color color, Vector2 sizeDelta, Vector3 localPos, float trackingFrac)
        {
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null)
            {
                tmp.font = font; // 中文字体图集（builder 注入）——默认西文字体渲染汉字是方块
            }
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = trackingFrac; // TMP 单位=em（TMP_Text.cs:4597），0.1=字宽 10%
            tmp.enableWordWrapping = false;
            tmp.text = content;
            tmp.alignment = TextAlignmentOptions.Center; // 居中排版（坐标按面板中线配平）
            tmp.rectTransform.sizeDelta = sizeDelta;
            return go.transform;
        }

        private void Update()
        {
            if (choicePanel == null)
            {
                return;
            }
            if (Application.isPlaying && !confirming)
            {
                PlacePanel(); // 全程跟随头部；但弹窗打开时冻结——跟随模式下水平偏移的按钮永远凝视不到
            }
            if (!choosing || hub == null || !hub.IsTracked(GestureHand.Right))
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            if (!Physics.Raycast(new Ray(cam.transform.position, cam.transform.forward), out var hit, 2.5f))
            {
                SetHovered(null);
                return;
            }
            MenuOption picked = null;
            var activeOptions = confirming ? confirmOptions : options; // 弹窗打开期间只认弹窗按钮
            foreach (var o in activeOptions)
            {
                if (hit.transform == o.root)
                {
                    picked = o;
                    break;
                }
            }
            SetHovered(picked);
            if (hovered != null && hub.PinchDown(GestureHand.Right))
            {
                choosing = false;
                Activate(hovered);
            }
        }

        private void SetHovered(MenuOption option)
        {
            if (option != null && option.locked)
            {
                option = null; // 锁定项永不高亮（置灰即不可选的明确信号）
            }
            if (hovered == option)
            {
                return;
            }
            if (hovered != null)
            {
                hovered.root.localScale = Vector3.one;
                hovered.label.GetComponent<TextMeshPro>().color = hovered.labelColor;
            }
            hovered = option;
            if (hovered != null)
            {
                hovered.root.localScale = new Vector3(1.15f, 1.15f, 1f);
                hovered.label.GetComponent<TextMeshPro>().color = hovered.accent; // 高亮反馈：放大15%+文字染强调色
            }
        }

        private void Activate(MenuOption option)
        {
            switch (option.action)
            {
                case MenuAction.Tutorial:
                    SceneLoader.FadeTo("Tutorial");
                    break;
                case MenuAction.Match:
                    SceneLoader.FadeTo("MoriMain");
                    break;
                case MenuAction.Quit:
                    ShowConfirm(); // 二次确认防误触：不直接退出
                    break;
                case MenuAction.QuitConfirm:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                    break;
                case MenuAction.QuitCancel:
                    HideConfirm();
                    break;
            }
        }

        private void ShowConfirm()
        {
            SetHovered(null); // 清掉"退出游戏"残留高亮
            foreach (var o in options)
            {
                o.root.gameObject.SetActive(false); // 模态：主菜单按钮整体隐藏——防透字，也防射线打穿弹窗命中身后碰撞体
            }
            confirmDialog.SetActive(true);
            Physics.SyncTransforms(); // 刚启用的弹窗碰撞体立即可被射线命中（否则当帧查询落空 → 永久卡死）
            confirming = true;
            choosing = true; // 弹窗按钮接管凝视+捏合
        }

        private void HideConfirm()
        {
            SetHovered(null);
            confirmDialog.SetActive(false);
            foreach (var o in options)
            {
                o.root.gameObject.SetActive(true);
            }
            Physics.SyncTransforms();
            confirming = false;
            choosing = true;
        }
    }
}
