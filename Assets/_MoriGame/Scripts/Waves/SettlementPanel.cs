using MoriGame.Gestures;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using MoriGame.Ui;

namespace MoriGame.Waves
{
    /// <summary>
    /// M2-T6 终局结算面板：四项明细（含口径复算式）+ 总分 + 奖牌 + 关键数据 + 再来一局。
    /// 沿用 StatusHud/Tutorial 的世界空间 Canvas ×0.01 惯例与中文字体，无新美术。
    /// 再来一局=按钮区凝视 + 右捏合持续 1 秒（复用补给箱交互范式）；编辑器 N 键直达。
    /// </summary>
    public class SettlementPanel : MonoBehaviour
    {
        public TMP_FontAsset font;
        public PicoGestureHub hub;
        public KeyCode editorRestartKey = KeyCode.N;
        public float restartHoldSeconds = 1f;

        private BoxCollider restartZone;
        private TMP_Text holdLabel;
        private Transform head;
        private float holdProgress;

        /// <summary>由 WaveDirector.OnMatchEnded 调用：构建面板并接管交互。</summary>
        public static SettlementPanel Show(TMP_FontAsset font, MatchRecorder recorder, ScoreResult score,
            bool editorPreview = false)
        {
            var cam = Camera.main;
            if ((!editorPreview && cam == null) || font == null || recorder == null || score == null)
            {
                Debug.LogError("[MoriGame] 结算面板缺依赖（相机/字体/记录器/评分）");
                return null;
            }
            var rig = MatchBootstrap.Instance != null ? MatchBootstrap.Instance.playerRig : null; // 结算彩带/金牌烟花（2026-11-11）
            if (!editorPreview && rig != null)
            {
                var center = rig.position + rig.forward * 0.8f + Vector3.up * 0.8f;
                Vfx.VfxBus.Play(l => l.medalConfetti, center);
                if (score.Medal == Medal.Gold)
                {
                    Vfx.VfxBus.Play(l => l.medalFireworks, center + rig.forward * 0.6f + Vector3.up * 0.8f);
                }
            }
            var go = new GameObject("MatchSettlement", typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.transform.localScale = Vector3.one * 0.02f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(116f, 84f);
            UiTheme.PlaceFixedPanel(rt, WorldUiPanels.Settlement, 65f, 2.4f, 0.95f);

            var bg = new GameObject("Bg", typeof(RectTransform)).AddComponent<Image>();
            bg.transform.SetParent(rt, false);
            ((RectTransform)bg.transform).sizeDelta = new Vector2(116f, 84f);
            bg.color = UiTheme.PanelBg;
            bg.raycastTarget = false;
            UiTheme.AddUguiBorder(rt, UiTheme.PanelBorder, 1.2f); // WW Polygon 米白粗框（2026-11-12）

            var panel = go.AddComponent<SettlementPanel>();
            panel.font = font;
            panel.hub = FindFirstObjectByType<PicoGestureHub>();
            panel.BuildContent(rt, recorder, score);
            Physics.SyncTransforms();
            return panel;
        }

#if UNITY_EDITOR
        public static SettlementPanel BuildEditorPreview(TMP_FontAsset font)
        {
            var recorder = new MatchRecorder();
            recorder.RecordArea(18f);
            for (var i = 0; i < 12; i++)
            {
                recorder.RecordKill("Normal", 10);
            }
            recorder.Finalize(480f, 0.8f);
            return Show(font, recorder, ScoreCalculator.Calculate(recorder.Snapshot()), true);
        }
#endif

        private void BuildContent(RectTransform rt, MatchRecorder r, ScoreResult s)
        {
            var medalColor = s.Medal switch
            {
                Medal.Gold => new Color(1f, 0.84f, 0.25f),
                Medal.Silver => new Color(0.83f, 0.85f, 0.9f),
                Medal.Bronze => new Color(0.8f, 0.52f, 0.28f),
                _ => new Color(0.6f, 0.65f, 0.7f)
            };
            MakeLine(rt, "Title", $"{MedalName(s.Medal)} · 总分 {s.Total} / 1000", 6.2f, 34f, medalColor);
            MakeLine(rt, "Territory", $"地盘 {FormatScore(s.Territory)} / 400　= 400×min(面积/24,1)　面积 {r.FinalAreaSqm:0.##}㎡ · 峰值 {r.PeakAreaSqm:0.##}㎡",
                3.4f, 21f, UiTheme.TextMain);
            MakeLine(rt, "Combat", $"战斗 {FormatScore(s.Combat)} / 300　= 300×min(积分/240,1)　积分 {r.CombatScore} · 击杀 {r.KillCount}",
                3.4f, 13f, UiTheme.TextMain);
            MakeLine(rt, "Defense", $"防守 {FormatScore(s.Defense)} / 200　= 核心完整度 {FormatScore(s.DefenseCore)} + 阶段分 {FormatScore(s.DefensePhases)}　核心 {r.FinalCoreHpRatio * 100f:0}% · 瘫痪 {r.CoreParalyzeCount} 次",
                3.4f, 5f, UiTheme.TextMain);
            MakeLine(rt, "Survival", $"生存 {FormatScore(s.Survival)} / 100　= max(100−20×倒地, 0)　倒地 {r.DownedCount} 次",
                3.4f, -3f, UiTheme.TextMain);

            var income = r.GoldDelta(GoldLedgerCategory.Kill) + r.GoldDelta(GoldLedgerCategory.PhaseReward) +
                         r.GoldDelta(GoldLedgerCategory.Supply) + r.GoldDelta(GoldLedgerCategory.Refund);
            var expense = r.GoldDelta(GoldLedgerCategory.Build) + r.GoldDelta(GoldLedgerCategory.DownedPenalty) +
                          r.GoldDelta(GoldLedgerCategory.ParalyzePenalty);
            MakeLine(rt, "KeyData",
                $"建筑摧毁 {r.BuildingsDestroyed} · 对局 {FormatTime(r.MatchDurationSeconds)} · 金币 收入 +{income} / 支出 {expense}",
                3f, -12f, UiTheme.TextDim);

            // 再来一局：按钮区（凝视碰撞体 + 右捏合蓄力）
            var buttonGo = new GameObject("RestartButton", typeof(RectTransform), typeof(Canvas));
            buttonGo.transform.SetParent(rt, false);
            var buttonCanvas = buttonGo.GetComponent<Canvas>();
            buttonCanvas.renderMode = RenderMode.WorldSpace;
            var buttonRt = (RectTransform)buttonGo.transform;
            buttonRt.sizeDelta = new Vector2(40f, 9f);
            buttonRt.localPosition = new Vector3(0f, -27f, 0f);
            var buttonBg = buttonGo.AddComponent<Image>();
            buttonBg.color = UiTheme.PanelBg;
            buttonBg.raycastTarget = false;
            UiTheme.AddUguiBorder(buttonRt, UiTheme.AccentOrange, 0.8f); // 橙框强调主按钮（WW 风格）
            MakeLabel(buttonRt, "ButtonText", "再来一局（凝视 + 捏合 1 秒）", 3.6f, UiTheme.TextMain);

            var zoneGo = new GameObject("RestartZone");
            zoneGo.transform.SetParent(rt, false);
            zoneGo.transform.localPosition = new Vector3(0f, -27f, -0.02f);
            restartZone = zoneGo.AddComponent<BoxCollider>();
            // Canvas ×0.01 缩放：局部单位 1 = 1 厘米世界尺寸
            restartZone.size = new Vector3(40f, 9f, 2f);
            restartZone.isTrigger = true;

            holdLabel = MakeLabel(rt, "HoldProgress", "", 3f, -34f, new Color(0.55f, 0.9f, 1f));
        }

        private TMP_Text MakeLabel(RectTransform parent, string name, string content, float fontSize, float y, Color color)
        {
            var text = MakeLabel(parent, name, content, fontSize, color);
            ((RectTransform)text.transform).localPosition = new Vector3(0f, y, 0f);
            return text;
        }

        private static TMP_Text MakeLabel(RectTransform parent, string name, string content, float fontSize, Color color)
        {
            var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            var textRt = (RectTransform)text.transform;
            textRt.sizeDelta = new Vector2(110f, 9f);
            text.font = parent.GetComponentInParent<SettlementPanel>().font;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private void MakeLine(RectTransform rt, string name, string content, float fontSize, float y, Color color)
        {
            var text = MakeLabel(rt, name, content, fontSize, color);
            ((RectTransform)text.transform).localPosition = new Vector3(0f, y, 0f);
            ((RectTransform)text.transform).sizeDelta = new Vector2(112f, 9f);
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(editorRestartKey))
            {
                Restart();
                return;
            }
#endif
            if (restartZone == null || hub == null)
            {
                return;
            }
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                head = cam.transform;
            }
            var gazed = Physics.Raycast(new Ray(head.position, head.forward), out var hit, 3f) &&
                        hit.collider == restartZone;
            var pinching = gazed && hub.Pinching(GestureHand.Right);
            if (pinching && holdProgress <= 0f)
            {
                MoriGame.Audio.SfxBus.Ui(MoriGame.Audio.SfxBus.Lib != null ? MoriGame.Audio.SfxBus.Lib.uiClick : null);
            }
            holdProgress = pinching ? holdProgress + Time.deltaTime / restartHoldSeconds : 0f;
            if (holdLabel != null)
            {
                holdLabel.text = pinching
                    ? $"重开 {Mathf.Clamp01(holdProgress) * 100f:0}%"
                    : string.Empty;
            }
            if (holdProgress >= 1f)
            {
                Restart();
            }
        }

        private void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        private static string MedalName(Medal medal)
        {
            return medal switch
            {
                Medal.Gold => "金牌",
                Medal.Silver => "银牌",
                Medal.Bronze => "铜牌",
                _ => "无奖牌"
            };
        }

        private static string FormatScore(float value)
        {
            return value.ToString("0.#");
        }

        private static string FormatTime(float seconds)
        {
            var total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
