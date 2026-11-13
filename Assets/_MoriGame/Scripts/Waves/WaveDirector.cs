using System.Collections.Generic;
using MoriGame.Build;
using MoriGame.Combat;
using MoriGame.Configs;
using MoriGame.Gestures;
using MoriGame.Shop;
using MoriGame.Zombies;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MoriGame.Ui;

namespace MoriGame.Waves
{
    /// <summary>
    /// M2-T1 波次导演（8 分钟整局）：驱动 WaveScheduler——按 MatchPhaseConfig 阶段表推进
    /// （12 秒准备 + 五阶段）；2026-11-09 拍波制：入场 0.8s 首拍（≤3s 硬性要求），每 12s 一拍，
    /// 拍量 = 入场数逐拍 +1 到阶段封顶（waveSizeCap：前三阶段 20、后两 25），切换前 10s 减速窗不出新拍；阶段到达奖励 300 金币（经济 v2）
    /// 入钱包 + HUD Toast；07:12 督战者事件（仅一次，免费生成）；
    /// 正式波次激活即关闭练习刷怪循环（G 键仍可手动重开）；到点冻结输入、冻结 MatchRecorder 并弹正式结算（M2-T6）。
    /// 数值全部读 MatchPhaseConfig 资产（红线：不硬编码）。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        [Header("配置（由 MoriSceneSetup 注入）")]
        public MatchPhaseConfig config;
        public TMP_FontAsset font;

        [Header("拍波节奏（2026-11-09 用户定案：首拍=入场数，逐拍+1 到阶段封顶 15/20）")]
        [Tooltip("两拍之间的间隔秒数（2026-11-10 用户定案 12 秒）")]
        public float waveIntervalSeconds = 12f;
        [Tooltip("阶段入口到首拍的延迟秒数（用户要求出怪不超过 3 秒）")]
        public float firstWaveDelaySeconds = 0.8f;
        [Tooltip("分帧出怪：每帧最多从池激活的僵尸数（防同帧激活 25 只卡顿，实测池激活是尖峰源）")]
        public int maxSpawnsPerFrame = 2;

        public static WaveDirector Instance { get; private set; }
        public WaveScheduler Scheduler { get; private set; }

        private float waveTimer;
        private int waveIndexInPhase;                  // 阶段内拍序（WaveSize 递增依据，换阶段归零）
        private readonly List<string> spawnQueue = new List<string>();   // 待分帧激活的僵尸请求
        private readonly List<ZombieSpawnOption> phaseOptions = new List<ZombieSpawnOption>();
        private bool pendingOverseer;
        private ZombiePool pool;
        private TMP_Text toastLabel;
        private float toastRemaining;
        private float toastDuration = 2f;
        private Color toastBaseColor = new Color(1f, 0.85f, 0.35f);

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (config == null)
            {
                Debug.LogError("[MoriGame] WaveDirector 缺少 MatchPhaseConfig（重跑「搭建灰盒场景」注入）");
                enabled = false;
                return;
            }
            var phases = new List<PhaseSpec>();
            foreach (var p in config.phases)
            {
                phases.Add(new PhaseSpec
                {
                    phaseId = p.phaseId,
                    displayName = p.displayName,
                    startSeconds = p.startSeconds,
                    endSeconds = p.endSeconds,
                    threatBudgetPerInterval = p.threatBudgetPerPlayer,
                    arrivalGoldReward = p.phaseArrivalGoldReward,
                    initialAliveCount = p.initialAliveCount,
                    typeWeights = new List<float>(p.typeWeights),
                    waveSizeCap = p.waveSizeCap,
                    aliveCap = p.aliveCap,
                    allowedZombieIds = new List<string>(p.allowedZombieIds)
                });
            }
            var specialEvents = new List<SpecialEventSpec>();
            foreach (var e in config.specialEvents)
            {
                specialEvents.Add(new SpecialEventSpec
                {
                    atSeconds = e.atSeconds,
                    eventId = e.eventId,
                    parameter = e.parameter
                });
            }
            Scheduler = new WaveScheduler(phases, config.budgetIntervalSeconds,
                config.matchDurationSeconds, config.maxBudgetCarryRounds,
                config.phaseSwitchSlowWindowSeconds, specialEvents);
            Scheduler.PhaseEntered += OnPhaseEntered;
            Scheduler.MatchEnded += OnMatchEnded;
            Scheduler.SpecialEventReached += OnSpecialEventReached;

            pool = ZombiePool.Instance;
            // 预生成池（2026-11-09 实测：运行期 Instantiate 僵尸预制体=几十毫秒卡顿）：
            // 全阶段类型并集 × 每类 5 只，准备期分帧造好藏进池，战斗期零实例化
            var preloadIds = new List<string>();
            foreach (var phase in phases)
            {
                foreach (var id in phase.allowedZombieIds)
                {
                    if (!preloadIds.Contains(id))
                    {
                        preloadIds.Add(id);
                    }
                }
            }
            if (!preloadIds.Contains("Overseer"))
            {
                preloadIds.Add("Overseer");
            }
            StartCoroutine(pool.PreloadRoutine(preloadIds, perType: 10)); // 同屏上限 20（2026-11-10 三次调压），每类 10 只垫高池
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var devSpawner = FindFirstObjectByType<ZombieDevSpawner>();
            if (devSpawner != null)
            {
                devSpawner.practiceLoop = false; // 正式波次接管练习循环（G 键可手动重开）
            }
#endif
            toastLabel = BuildToast();
        }

        private void Update()
        {
            if (Scheduler == null)
            {
                return;
            }
            Scheduler.Tick(Time.deltaTime);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.T))
            {
                // 逐秒推进保真预算窗/阶段事件，供真机快速验收终局结算（T6，2026-11-10）
                for (int i = 0; i < 60; i++)
                {
                    Scheduler.Tick(1f);
                }
                Debug.Log($"[MoriGame] 调试：时间快进 60 秒 → {Scheduler.ElapsedSeconds:0}s / {config.matchDurationSeconds}s");
            }
#endif

            if (toastLabel != null && toastRemaining > 0f)
            {
                toastRemaining -= Time.deltaTime;
                var t = Mathf.Clamp01(toastRemaining / toastDuration);
                toastLabel.color = new Color(toastBaseColor.r, toastBaseColor.g, toastBaseColor.b, t);
                if (toastRemaining <= 0f)
                {
                    toastLabel.gameObject.SetActive(false);
                }
            }

            if (Scheduler.Ended)
            {
                return;
            }
            if (pendingOverseer && pool != null && pool.ActiveCount < AliveCap &&
                pool.TryGetSpawnPosition(out var overseerPos) &&
                pool.Spawn("Overseer", overseerPos) != null)
            {
                pendingOverseer = false;
                MatchBootstrap.Instance?.Bgm?.OnBossSpawned(); // Boss 主题（2026-11-10 BGM）
                Debug.Log("[MoriGame] 督战者已实体生成（07:12 特殊事件，仅一次）");
            }
            // 拍波调度（2026-11-09）：阶段入口 0.8s 首拍，此后每 10s 一拍；切换前 10s 减速窗不出新拍
            var phase = Scheduler.CurrentPhase;
            if (phase != null && phase.allowedZombieIds.Count > 0)
            {
                waveTimer -= Time.deltaTime;
                if (waveTimer <= 0f)
                {
                    if (!Scheduler.InSlowWindow)
                    {
                        ScheduleWave(phase);
                    }
                    waveIndexInPhase++;
                    waveTimer = waveIntervalSeconds;
                }
            }
            DrainSpawnQueue();
        }

        /// <summary>当前阶段同屏上限（阶段 aliveCap &gt; 0 优先，否则全局默认；2026-11-10 P1—P4=20、P5=25）</summary>
        private int AliveCap
        {
            get
            {
                var phase = Scheduler != null ? Scheduler.CurrentPhase : null;
                var cap = phase != null && phase.aliveCap > 0 ? phase.aliveCap : config.maxActiveZombiesPerPlayer;
                return Mathf.Min(cap, config.maxActiveZombiesTotal);
            }
        }

        /// <summary>规划一拍（2026-11-09 拍波制）：目标数 = WaveSize(入场数, 拍序, 阶段封顶)，
        /// 逐只扣预算入队；激活交给 DrainSpawnQueue 分帧做，本方法零实例化零激活</summary>
        private void ScheduleWave(PhaseSpec phase)
        {
            var size = WaveComposition.WaveSize(phase.initialAliveCount, waveIndexInPhase, phase.waveSizeCap);
            var planned = 0;
            for (var i = 0; i < size; i++)
            {
                var selected = WaveComposition.SelectAffordable(
                    phaseOptions, Scheduler.CarriedBudget, Random.value);
                if (selected < 0)
                {
                    break; // 预算不够这一档，留到下一拍
                }
                var option = phaseOptions[selected];
                if (!Scheduler.TrySpend(option.ThreatPoints))
                {
                    break;
                }
                spawnQueue.Add(option.ZombieId);
                planned++;
            }
            if (planned > 0)
            {
                Debug.Log($"[MoriGame] {phase.displayName} 第 {waveIndexInPhase + 1} 拍：目标 {size}，入队 {planned}");
            }
        }

        /// <summary>分帧激活出队请求：每帧最多 maxSpawnsPerFrame 只——同帧激活 30 只（NavMeshAgent
        /// enable + 采样）是 PICO 卡顿尖峰源；场满或找不到连通刷怪点即丢弃剩余请求（下一拍补）</summary>
        private void DrainSpawnQueue()
        {
            if (spawnQueue.Count == 0)
            {
                return;
            }
            var drained = 0;
            while (spawnQueue.Count > 0 && drained < maxSpawnsPerFrame)
            {
                if (pool == null || pool.ActiveCount >= AliveCap || !pool.TryGetSpawnPosition(out var pos))
                {
                    spawnQueue.Clear();
                    return;
                }
                pool.Spawn(spawnQueue[0], pos);
                spawnQueue.RemoveAt(0);
                drained++;
            }
        }

        /// <summary>缓存本阶段可刷类型表（含占比权重，换阶段重建；Overseer 走特殊事件免费生成，不占预算）</summary>
        private void BuildPhaseOptions(PhaseSpec phase)
        {
            phaseOptions.Clear();
            if (pool == null)
            {
                return;
            }
            for (var i = 0; i < phase.allowedZombieIds.Count; i++)
            {
                var id = phase.allowedZombieIds[i];
                if (id == "Overseer")
                {
                    continue;
                }
                var zombie = pool.ConfigFor(id);
                if (zombie != null && pool.HasModel(id)) // 模型被置空的类型不进类型表（2026-11-10 T 模型整顿）
                {
                    var weight = i < phase.typeWeights.Count ? phase.typeWeights[i] : 0f;
                    phaseOptions.Add(new ZombieSpawnOption(id, zombie.threatPoints, weight));
                }
            }
        }

        private void OnPhaseEntered(PhaseSpec phase)
        {
            waveIndexInPhase = 0; // 拍序归零：每阶段从入场数重新爬
            waveTimer = firstWaveDelaySeconds; // 入场 ≤3 秒首拍（2026-11-09 用户硬性要求）
            BuildPhaseOptions(phase); // 阶段类型表换挡
            var match = MatchBootstrap.Instance;
            if (phase.arrivalGoldReward > 0 && match != null)
            {
                match.Wallet.Add(phase.arrivalGoldReward); // docs/05 阶段到达奖励
                match.Recorder?.RecordGold(GoldLedgerCategory.PhaseReward, phase.arrivalGoldReward);
                Audio.SfxBus.Coin(phase.arrivalGoldReward);
                ShowToast($"阶段奖励 +{phase.arrivalGoldReward} 金币 · {phase.displayName}");
                Debug.Log($"[MoriGame] {phase.displayName} 开始：+{phase.arrivalGoldReward} 金币（余 {match.Wallet.Gold}）");
            }
        }

        private void OnSpecialEventReached(SpecialEventSpec evt)
        {
            if (evt.eventId == "SpawnOverseer")
            {
                pendingOverseer = pool != null && pool.HasModel("Overseer"); // 模型置空期不挂生成请求（2026-11-10）
                ShowToast("⚠ 督战者降临预警！", new Color(1f, 0.35f, 0.25f));
                Debug.Log(pendingOverseer
                    ? "[MoriGame] 07:12 督战者事件触发，等待活跃位生成 Boss（仅一次）"
                    : "[MoriGame] 07:12 督战者事件触发：模型已置空，跳过实体生成（仅警报）");
            }
            else
            {
                Debug.Log($"[MoriGame] 特殊事件 {evt.eventId}（{evt.parameter}）@{evt.atSeconds:0}s");
            }
        }

        private void OnMatchEnded()
        {
            Debug.Log("[MoriGame] 对局结束（08:00）——冻结输入，正式结算");
            MatchBootstrap.Instance?.Bgm?.FadeOutStop(); // 结算界面静场（2026-11-10 BGM）
            BuildManager.Instance?.CancelPlacement();
            DisableIfExists<WeaponController>();
            DisableIfExists<Economy.SupplyCrate>();
            DisableIfExists<GestureLocomotion>();
            // 2026-11-05 展示位范式：商店=场景展示位（ShopPanel 已弃用）——结束时全部停止抓取
            foreach (var stand in FindObjectsByType<ShopStand>(FindObjectsSortMode.None))
            {
                stand.enabled = false;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DisableIfExists<ZombieDevSpawner>();
#endif
            // M2-T6：终局冻结采集器（时长 + 核心生命比例），评分并弹结算面板
            var match = MatchBootstrap.Instance;
            var recorder = match?.Recorder;
            if (recorder != null)
            {
                var core = match.coreDevice;
                var ratio = 0f;
                if (core != null && core.State != null && core.State.MaxHp > 0)
                {
                    ratio = core.State.Hp / (float)core.State.MaxHp;
                }
                recorder.Finalize(Scheduler.ElapsedSeconds, ratio);
            }
            var score = recorder != null ? ScoreCalculator.Calculate(recorder.Snapshot()) : null;
            if (recorder != null)
            {
                MatchResultStore.Save(recorder, score);
            }
            SettlementPanel.Show(font, recorder, score);
        }

        private static void DisableIfExists<T>() where T : Behaviour
        {
            var found = FindFirstObjectByType<T>();
            if (found != null)
            {
                found.enabled = false;
            }
        }

        /// <summary>HUD Toast（2026-11-05 改 public：展示位购买/归位/失败提示复用此条，不再自建）</summary>
        public void ShowToast(string message, Color? color = null)
        {
            if (toastLabel == null)
            {
                return;
            }
            toastLabel.gameObject.SetActive(true);
            toastLabel.text = message;
            toastRemaining = toastDuration;
            toastBaseColor = color ?? new Color(1f, 0.85f, 0.35f);
            toastLabel.color = toastBaseColor;
        }

        /// <summary>阶段奖励 Toast：固定在玩法锚点右侧大版提示，不随头部移动。</summary>
        private TMP_Text BuildToast()
        {
            if (font == null)
            {
                return null;
            }
            var canvasGo = new GameObject("MatchToast", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(100f, 12f);
            rt.localScale = Vector3.one * 0.018f;
            UiTheme.PlaceFixedPanel(rt, WorldUiPanels.Toast, 65f, 2.4f, 0.8f);
            var toastBg = canvasGo.AddComponent<UnityEngine.UI.Image>();
            toastBg.color = UiTheme.PanelBg;
            toastBg.raycastTarget = false;
            UiTheme.AddUguiBorder(rt, UiTheme.PanelBorder, 0.9f);
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(rt, false);
            var textRt = (RectTransform)text.transform;
            textRt.sizeDelta = new Vector2(94f, 9f);
            text.font = font;
            text.fontSize = 4.2f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.85f, 0.35f, 0f);
            text.raycastTarget = false;
            text.gameObject.SetActive(false);
            return text;
        }

#if UNITY_EDITOR
        public GameObject BuildToastEditorPreview()
        {
            var text = BuildToast();
            if (text == null)
            {
                return null;
            }
            text.text = "阶段奖励 +300 金币";
            text.color = new Color(1f, 0.85f, 0.35f, 1f);
            text.gameObject.SetActive(true);
            return text.transform.parent.gameObject;
        }
#endif
    }
}
