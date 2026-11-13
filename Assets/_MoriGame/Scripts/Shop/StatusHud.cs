using MoriGame.Build;
using MoriGame.Combat;
using MoriGame.Devices;
using MoriGame.Player;
using TMPro;
using UnityEngine;
using MoriGame.Ui;

namespace MoriGame.Shop
{
    /// <summary>常驻状态 HUD：远距固定在玩法锚点左侧，避开玩家到前方僵尸的视线。
    /// 生命 / 中控血量 / 金币 / 地盘面积 / 弹匣 / 倒计时；倒地/中控瘫痪时整条切换为对应提示。</summary>
    public class StatusHud : MonoBehaviour
    {
        public TMP_Text label;

        [Header("布局（米，Inspector 可调）")]
        public float widthMeters = 1.65f;
        public float heightMeters = 0.32f;
        public float distanceMeters = 2.3f;
        public float heightAboveAnchor = 0.86f;
        public float yawOffsetDegrees = -65f;

        [Header("字号自动适配（画布单位）")]
        public float fontMinUnits = 5f;
        public float fontMaxUnits = 8f;

        private WeaponController weapon;

        private void Awake()
        {
            BuildVisuals();
        }

        private void BuildVisuals()
        {
            var rt = (RectTransform)transform;
            rt.sizeDelta = new Vector2(widthMeters * 100f, heightMeters * 100f);
            UiTheme.PlaceFixedPanel(transform, WorldUiPanels.StatusHud,
                yawOffsetDegrees, distanceMeters, heightAboveAnchor);
            if (label != null)
            {
                ((RectTransform)label.transform).sizeDelta =
                    new Vector2(widthMeters * 100f - 6f, heightMeters * 100f - 4f);
                label.enableAutoSizing = true;
                label.fontSizeMin = fontMinUnits;
                label.fontSizeMax = fontMaxUnits;
            }
            if (transform.Find("Bg") == null)
            {
                var bg = new GameObject("Bg", typeof(RectTransform)).AddComponent<UnityEngine.UI.Image>();
                bg.transform.SetParent(transform, false);
                ((RectTransform)bg.transform).sizeDelta = rt.sizeDelta;
                bg.color = UiTheme.PanelBg;
                bg.raycastTarget = false;
                bg.transform.SetAsFirstSibling();
            }
            if (transform.Find("BorderT") == null)
            {
                UiTheme.AddUguiBorder(rt, UiTheme.PanelBorder, 1.1f);
            }
            if (label != null)
            {
                label.transform.SetAsLastSibling();
            }
        }

#if UNITY_EDITOR
        public void BuildEditorPreview()
        {
            BuildVisuals();
            if (label != null)
            {
                label.text = "生命 100/100 · 中控 1000/1000 · 金币 300\n面积 16㎡ · 弹匣 12/12 · 剩余 08:00\n落地准备";
                label.color = UiTheme.TextMain;
            }
        }
#endif

        private void Update()
        {
            var match = MatchBootstrap.Instance;
            if (match == null || label == null)
            {
                return;
            }
            if (weapon == null)
            {
                weapon = FindFirstObjectByType<WeaponController>();
            }
            // 放置模式引导常驻条（2026-11-02 四轮定案）：进入放置即接管 HUD 底条（绿色调），
            // 落地/取消自动恢复常规内容；地面预览旁的操作小字已移除（保留绿/红合法性预览本身）
            if (BuildManager.IsPlacingActive)
            {
                label.text = "放置模式：凝视绿框捏合放置 · 张掌取消";
                label.color = new Color(0.45f, 1f, 0.6f);
                return;
            }
            label.color = UiTheme.TextMain;
            if (match.Vitals.State == VitalsState.Downed)
            {
                var remain = match.playerConfig.downedSeconds - match.Vitals.StateElapsedSeconds;
                label.text = $"倒地 · {remain:F1} 秒后重生";
                return;
            }
            if (match.Vitals.State == VitalsState.RespawnProtection)
            {
                var remain = match.playerConfig.respawnProtectionSeconds - match.Vitals.StateElapsedSeconds;
                label.text = $"重生保护 · {remain:F1}s · 开火提前解除";
                return;
            }
            if (match.coreDevice != null && match.coreDevice.State != null &&
                match.coreDevice.State.State == CoreState.Disabled)
            {
                label.text = $"中控瘫痪 {match.coreDevice.State.ParalyzeRemainingSeconds:F1}s · 商店停用";
                return;
            }
            // 新手引导已移交 TutorialController 清单+JIT（M2-T0，2026-11-04）——HUD 底条准备期直接显示常规状态
            var core = match.coreDevice != null && match.coreDevice.State != null
                ? match.coreDevice.State.Hp + "/" + match.coreDevice.State.MaxHp
                : "-";
            var area = BuildManager.Instance != null ? BuildManager.Instance.OwnedAreaSqm : 0f;
            var ammo = weapon != null && weapon.Deployed
                ? $" · 弹匣 {weapon.AmmoInMag}/{weapon.MagazineSize}"
                : "";
            // T1 对局计时：三行大字号显示；结束后指引玩家看侧方结算。
            var director = Waves.WaveDirector.Instance;
            if (director != null && director.Scheduler != null && director.Scheduler.Ended)
            {
                label.text = "对局结束\n查看侧方结算面板";
                return;
            }
            var remaining = director != null && director.Scheduler != null
                ? FormatClock(director.Scheduler.RemainingSeconds)
                : "--:--";
            var phaseName = director != null && director.Scheduler != null &&
                            director.Scheduler.CurrentPhase != null
                ? director.Scheduler.CurrentPhase.displayName
                : "准备";
            var ammoText = string.IsNullOrEmpty(ammo) ? "弹匣 --" : ammo.TrimStart(' ', '·');
            label.text = $"生命 {match.Vitals.Hp}/{match.Vitals.MaxHp} · 中控 {core} · 金币 {match.Wallet.Gold}\n面积 {area:0.##}㎡ · {ammoText} · 剩余 {remaining}\n{phaseName}";
        }

        private static string FormatClock(float seconds)
        {
            var total = Mathf.CeilToInt(seconds);
            return $"{total / 60}:{total % 60:D2}";
        }
    }
}
