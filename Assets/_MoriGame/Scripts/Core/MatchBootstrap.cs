using MoriGame.Audio;
using MoriGame.Configs;
using MoriGame.Devices;
using MoriGame.Economy;
using MoriGame.Player;
using MoriGame.Territory;
using UnityEngine;

namespace MoriGame
{
    /// <summary>
    /// M1 灰盒对局引导：钱包/背包/玩家生命/中控/地盘网格接线 + 开发测试键。
    /// 数值全部来自 PlayerConfig / CoreDeviceConfig（红线：不硬编码）。
    /// 开发键（仅 Editor/Development）：K 自伤 30 · X 直接倒地 · H 打中控 120 · J +50 金币 · P 模拟开火破除重生保护。
    /// </summary>
    public class MatchBootstrap : MonoBehaviour
    {
        [Header("配置（由 MoriSceneSetup 注入）")]
        public PlayerConfig playerConfig;
        public CoreDeviceConfig coreConfig;
        public CoreDevice coreDevice;

        [Header("场景引用")]
        public Transform playerRig;
        public Transform playerAttackTarget;
        public Transform respawnPoint;

        [Header("站桩锚点（2026-11-06 重构：RealSpace 原点=玩家点，网格/刷怪圈世界锚）")]
        public Transform playSpaceAnchor;

        [Header("音效（2026-11-10 资产化：SfxLibrary 由迁移注入，各系统经 SfxBus 访问）")]
        public SfxLibrary sfxLibrary;

        public SfxService Sfx { get; private set; }

        public Audio.BgmDirector Bgm { get; private set; }

        [Header("特效（2026-11-11 资产化：VfxLibrary 由迁移注入，各系统经 Vfx 池访问）")]
        public Vfx.VfxLibrary vfxLibrary;

        public Vfx.VfxPool Vfx { get; private set; }

        public Transform PlayerTarget => playerAttackTarget != null ? playerAttackTarget : playerRig;

        private Vector3 CorePosition() => coreDevice != null ? coreDevice.transform.position : transform.position;

        public bool IsPlayerTarget(Transform target) =>
            target != null && (target == playerRig || target == playerAttackTarget);

        public PlayerWallet Wallet { get; private set; }
        public PlayerInventory Inventory { get; private set; }
        public VitalsStateMachine Vitals { get; private set; }
        public TerritoryGrid Grid { get; private set; }
        public float MatchElapsedSeconds { get; private set; }

        /// <summary>对局事件采集器（M2-T6 结算数据源，Awake new=随局重置）</summary>
        public MatchRecorder Recorder { get; private set; }

        public static MatchBootstrap Instance { get; private set; }

        /// <summary>商店可用：玩家未倒地 + 核心未瘫痪（docs/05 7.3）</summary>
        public bool ShopOpen =>
            Vitals != null && Vitals.State != VitalsState.Downed &&
            coreDevice != null && coreDevice.State != null &&
            coreDevice.State.State == CoreState.Normal;

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 72; // PICO 目标帧率（2026-11-11 性能；vSync 已 0）
            Sfx = gameObject.AddComponent<SfxService>();
            Sfx.Init(sfxLibrary);
            Bgm = gameObject.AddComponent<Audio.BgmDirector>();
            Bgm.Init(sfxLibrary);
            Bgm.StartRotation(); // 对局开始即起播轮播（督战者/终局钩子见 WaveDirector/ZombieAgent）
            Vfx = gameObject.AddComponent<Vfx.VfxPool>();
            Vfx.Init(vfxLibrary);
            if (playSpaceAnchor != null)
            {
                TerritoryGrid.WorldOrigin = new Vector2(playSpaceAnchor.position.x, playSpaceAnchor.position.z);
                // 地面高度取地形实测（2026-11-06 高台制：僵尸走真实地面；探针偏出高台占地避免命中塔体）
                var probe = playSpaceAnchor.position + new Vector3(1.5f, 5f, 0f);
                TerritoryGrid.WorldFloorY = Physics.Raycast(probe, Vector3.down, out var floorHit, 12f)
                    ? floorHit.point.y
                    : playSpaceAnchor.position.y;
            }
            Wallet = new PlayerWallet(playerConfig.startingGold);
            Inventory = new PlayerInventory();
            Recorder = new MatchRecorder();
            Vitals = new VitalsStateMachine(
                playerConfig.maxHp, playerConfig.downedSeconds, playerConfig.respawnProtectionSeconds);

            Grid = new TerritoryGrid();
            Grid.SetupDefaultCores();

            Vitals.Downed += () =>
            {
                Recorder.RecordDowned();
                var penalty = Wallet.ApplyDownedPenalty(
                    playerConfig.downedGoldLossPercent, playerConfig.downedGoldLossCap);
                Recorder.RecordGold(GoldLedgerCategory.DownedPenalty, -penalty);
                SfxBus.Game2D(sfxLibrary != null ? sfxLibrary.playerDown : null);
                if (playerRig != null)
                {
                    MoriGame.Vfx.VfxBus.Play(l => l.playerDowned, playerRig.position + Vector3.up * 0.5f); // 倒地星星（属性名 Vfx 遮蔽命名空间，须全限定）
                }
                Debug.Log($"[MoriGame] 倒地！罚金 -{penalty}（金币剩 {Wallet.Gold}），{playerConfig.downedSeconds:0} 秒后重生");
            };
            Vitals.Respawned += () =>
            {
                if (playerRig != null && respawnPoint != null)
                {
                    playerRig.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
                    StartCoroutine(ProtectionHaloRoutine(playerRig)); // 重生保护光环（2026-11-11，保护期结束即停）
                }
                Debug.Log("[MoriGame] 重生：回到工作台固定位姿，3 秒无敌保护");
            };

            var coreState = coreDevice != null ? coreDevice.EnsureState() : null;
            if (coreState != null)
            {
                coreState.Disabled += () =>
                {
                    MoriGame.Vfx.VfxBus.Play(l => l.coreDown, CorePosition()); // 瘫痪雷击（2026-11-11）
                    if (Zombies.ZombiePool.Instance != null && coreDevice != null) // 瘫痪 EMP 反冲（新手翻身机制）
                    {
                        var origin = coreDevice.transform.position;
                        var count = 0;
                        foreach (var zombie in Zombies.ZombiePool.Instance.ActiveZombies)
                        {
                            if (zombie != null)
                            {
                                zombie.ApplyEmpStun(coreConfig.empKnockbackMeters, coreConfig.empStunSeconds, origin);
                                count++;
                            }
                        }
                        Debug.Log($"[MoriGame] EMP 反冲：击退+眩晕 {count} 只僵尸 {coreConfig.empStunSeconds:0.##} 秒（击退 {coreConfig.empKnockbackMeters:0.#} 米）");
                    }
                    var director = Waves.WaveDirector.Instance;
                    var phaseIndex = director != null && director.Scheduler != null
                        ? director.Scheduler.PhaseIndex - 1
                        : -1;
                    Recorder.RecordCoreParalyze(phaseIndex);
                    var penalty = Wallet.ApplyParalyzePenalty(coreConfig.paralyzeGoldPenalty);
                    Recorder.RecordGold(GoldLedgerCategory.ParalyzePenalty, -penalty);
                    SfxBus.World(sfxLibrary != null ? sfxLibrary.coreDown : null, CorePosition());
                    Debug.Log($"[MoriGame] 中控瘫痪！罚金 -{penalty}，商店停用，地盘保持，{coreConfig.paralyzeSeconds:0} 秒后自动恢复 {coreConfig.recoverHp}");
                };
                coreState.Recovered += () =>
                {
                    MoriGame.Vfx.VfxBus.Play(l => l.coreRecover, CorePosition()); // 恢复特效（2026-11-11）
                    SfxBus.World(sfxLibrary != null ? sfxLibrary.coreRecover : null, CorePosition());
                    Waves.WaveDirector.Instance?.ShowToast($"中控恢复 · 生命 {coreConfig.recoverHp}");
                    Debug.Log($"[MoriGame] 中控瘫痪结束：恢复 {coreConfig.recoverHp} 生命，商店与机枪塔恢复");
                };
            }
        }

        private void Update()
        {
            MatchElapsedSeconds += Time.deltaTime;
            Vitals?.Tick(Time.deltaTime);
            Grid?.Tick(Time.deltaTime);
            // 地盘面积逐帧采样：终局值 + 历史峰值（M2-T6 结算数据）
            if (Grid != null)
            {
                Recorder?.RecordArea(Grid.GetAreaSqm(Territory.CellOwner.P1));
            }
            HandleDevInput();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>重生保护光环：循环播放，保护期结束即停（2026-11-11 特效系统）</summary>
        private System.Collections.IEnumerator ProtectionHaloRoutine(Transform host)
        {
            var handle = MoriGame.Vfx.VfxBus.Loop(l => l.protectionHalo, host != null ? host : transform, Vector3.zero);
            yield return new WaitForSeconds(Mathf.Max(0.1f, playerConfig.respawnProtectionSeconds));
            handle.Stop();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void HandleDevInput()
        {
            if (Input.GetKeyDown(KeyCode.K))
            {
                var applied = Vitals.TakeDamage(30);
                Debug.Log($"[MoriGame] 调试：自伤 30 → HP {Vitals.Hp}/{Vitals.MaxHp}（生效={applied}）");
            }
            if (Input.GetKeyDown(KeyCode.X))
            {
                Vitals.TakeDamage(Vitals.Hp);
            }
            if (Input.GetKeyDown(KeyCode.H))
            {
                var applied = coreDevice != null && coreDevice.TakeDamage(120);
                Debug.Log(applied && coreDevice != null
                    ? $"[MoriGame] 调试：中控 -120 → {coreDevice.State.Hp}/{coreDevice.State.MaxHp}"
                    : "[MoriGame] 调试：中控免疫（瘫痪中）");
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                Wallet.Add(50);
                Debug.Log($"[MoriGame] 调试：金币 +50 → {Wallet.Gold}");
            }
            if (Input.GetKeyDown(KeyCode.P))
            {
                Vitals.BreakProtection();
            }
        }
#else
        private void HandleDevInput()
        {
        }
#endif
    }
}
