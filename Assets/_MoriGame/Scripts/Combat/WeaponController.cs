using MoriGame.Audio;
using MoriGame.Build;
using MoriGame.Configs;
using MoriGame.Gestures;
using MoriGame.Shop;
using MoriGame.Zombies;
using UnityEngine;

namespace MoriGame.Combat
{
    /// <summary>瞄准模式（2026-11-02 红线定案：出包前由 MoriSceneSetup.BuildAimMode 常量选定，
    /// **游戏内一律不可切换**——所有开发设置仅出包前修改，见 CLAUDE.md 红线规则）：
    /// Gaze = A 视线瞄准（弹丸从枪口飞向视线命中点，判定走头部射线）；
    /// Barrel = B 枪管瞄准（弹丸沿枪管直射，判定走 muzzle + 枪管 forward，与视觉同线）。</summary>
    public enum AimMode : byte
    {
        Gaze = 0,
        Barrel = 1
    }

    /// <summary>
    /// 武器发射组件（docs/07 2026-11-03 定案"食指伸直=开火"）：
    /// 平时手自由；手腕抬到胸前有效区（高度腰—肩之间 + 离胸口横向距离 ≤0.55m，肘弯 90°±30° 的代理判定
    /// ——手部追踪无肘关节，以"手收回胸前"近似肘弯）0.3 秒出枪；
    /// **有效区内右手食指伸直持续开火**（射速受 WeaponConfig 约束），食指弯曲停火；
    /// 捏合已完全退出射击判定（捏合=UI 确认专用：购买/放置）；手不在有效区时食指伸直无效（静默防误触）；
    /// 手腕低于腰部持续 1 秒自动收枪（枪隐藏）。
    /// 食指伸直判定 = IndexExtensionDetector 夹角法（腕→食指近节 vs 食指近节→指尖夹角 < indexExtendedMaxAngle，2026-11-04 真机修正）。
    /// 蓄势光效：食指伸直且在有效区 → 枪口蓄势光（"现在指着就会开火"状态感）；不在有效区无任何反馈。
    /// 持枪位置 = 场景手动摆放（2026-11-02 定案）：枪作为手腕关节的子物体存在场景里（MoriSceneSetup
    /// 把手动贴合好的位姿换算到手腕空间挂入），运行时零定位代码——手在哪枪在哪，与手柄模型挂载一致。
    /// 换弹：左手拍击弹匣（0.15m 内 + 速度 ≥1.2m/s，docs/07 定案）或编辑器 R 键；弹匣空自动辅助换弹。
    /// 命中反馈：准星脉冲（白=命中 / 金=弱点击杀）+ 音效（弱点高音叮）；开火即破重生保护。
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Tooltip("由 MoriSceneSetup 注入")]
        public WeaponConfig config;
        public PicoGestureHub gestureHub;
        public Transform muzzle;
        public Transform magazine;
        [Tooltip("弹丸对象池（MoriSceneSetup 注入）——真实弹丸替代曳光线")]
        public ProjectilePool projectilePool;
        public Renderer muzzleFlash;
        public Reticle reticle;
        [Tooltip("枪口蓄势光（食指伸直+有效区时激活，MoriSceneSetup 注入）")]
        public GameObject chargeGlow;
        [Tooltip("枪体视觉根（收枪时隐藏它——组件本身必须常驻驱动状态机）")]
        public GameObject visualRoot;

        [Tooltip("瞄准模式（红线：出包前 MoriSceneSetup.BuildAimMode 选定烘入，游戏内不可切换）")]
        public AimMode aimMode = AimMode.Barrel;

        [Header("抬手开火有效区（docs/07，坐姿以头部为基准）")]
        [Tooltip("腰部低于头部的距离（米）")]
        public float waistDropMeters = 0.55f;
        [Tooltip("肩部低于头部的距离（米）")]
        public float shoulderDropMeters = 0.15f;
        [Tooltip("手腕离头部中轴的最大横向距离（米）——肘弯 90°±30° 的代理判定")]
        public float chestDistanceMeters = 0.55f;
        public float raiseSeconds = 0.3f;
        public float stowSeconds = 1f;

        [Header("食指伸直开火（docs/07 2026-11-03 定案）")]
        [Tooltip("指向向量（食指根→指尖）与掌基向量（腕→食指根）夹角小于此值判伸直（度）")]
        public float indexExtendedMaxAngle = 35f; // 2026-11-04 真机二调：30° 需略抬指尖，35° 下垂少许即触发（扣扳机 45°+ 仍停火）

        [Header("拍击弹匣换弹")]
        public float slapRadiusMeters = 0.15f;
        public float slapSpeedMps = 1.2f;

        public float gazeMaxMeters = 40f;
        public float feedbackSeconds = 0.06f;

        /// <summary>状态 HUD 用（2026-11-02 二轮）：出枪态与弹匣余量</summary>
        public bool Deployed => deploy != null && deploy.Deployed;

        /// <summary>枪套锁（2026-11-05 站桩工作台制，WeaponHolster 驱动）：
        /// true=枪在插槽（出枪/开火锁死，真枪 root 由 WeaponHolster 放回工作台）；抓起（Unholster）解锁并立即装备。
        /// 抬手出枪/垂手收枪（旧规则）仅在解锁后有效。</summary>
        public bool Holstered { get; private set; }

        /// <summary>枪回插槽锁死（WeaponHolster 放回路径调用）</summary>
        public void SetHolstered(bool holstered)
        {
            Holstered = holstered;
            if (holstered && deploy != null)
            {
                deploy.ForceStow();
            }
        }

        /// <summary>抓起即装备（站桩制零切换：跳过抬手计时，出枪立即可开火）</summary>
        public void Unholster()
        {
            Holstered = false;
            if (deploy != null)
            {
                deploy.ForceDeploy();
            }
        }

        /// <summary>会话内实际开火次数（TryFire 成功；M2-T0 图例淡出信号源）</summary>
        public int ShotCount { get; private set; }
        public int AmmoInMag => state != null ? state.AmmoInMag : 0;
        public int MagazineSize => state != null ? state.MagazineSize : 0;

        private WeaponState state;
        private WeaponDeployState deploy;
        private MagSlapDetector slap;
        private Transform head;
        private float feedbackRemaining;
        private Vector3 lastLeftWrist;
        private bool hasLastLeftWrist;
        private bool indexExtendedNow;
        private Vector3 chargeGlowBaseScale = Vector3.one;
        private bool chargeGlowScaleCaptured;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogError("[MoriGame] WeaponController 缺少 WeaponConfig（场景搭建注入失败）");
                enabled = false;
                return;
            }
            state = new WeaponState(config.magazineSize, config.fireRatePerSecond, config.reloadSeconds);
            deploy = new WeaponDeployState(raiseSeconds, stowSeconds);
            slap = new MagSlapDetector(slapSpeedMps);
        }

        private void Update()
        {
            if (state == null)
            {
                return;
            }
            state.Tick(Time.deltaTime);
            UpdateFeedback();
            indexExtendedNow = gestureHub != null &&
                               gestureHub.TryGetIndexExtended(GestureHand.Right, indexExtendedMaxAngle, out var extended) &&
                               extended;
            TickDeploy();
            TickSlapReload();
            HandleFireInput();
            HandleReloadInput();
            UpdateChargeGlow();
        }

        /// <summary>抬手开火状态机 + 枪体显隐（编辑器无手势追踪时保持出枪可用）</summary>
        private void TickDeploy()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                head = cam.transform;
            }
            if (Holstered)
            {
                deploy.ForceStow();
                if (visualRoot != null && !visualRoot.activeSelf)
                {
                    visualRoot.SetActive(true); // 插槽中显示真枪；Holstered 只锁出枪与开火
                }
                return;
            }
            var wrist = Vector3.zero;
            var handTracked = gestureHub != null &&
                              gestureHub.TryGetWristPosition(GestureHand.Right, out wrist);
            var inZone = true;
            var belowWaist = false;
            if (handTracked)
            {
                inZone = IsInChestZone(wrist);
                belowWaist = wrist.y < head.position.y - waistDropMeters;
            }
            deploy.Tick(Time.deltaTime, inZone, belowWaist);
            // 真机上武器与手模同步显隐（2026-11-02 实测：追踪丢失手模隐藏，枪不能悬空残留在手腕上）；
            // 编辑器无手部追踪（TrackingEverActive 恒 false）保持纯抬手显隐可用
            var show = deploy.Deployed;
            if (show && gestureHub != null && gestureHub.TrackingEverActive(GestureHand.Right))
            {
                show = gestureHub.IsHandVisible(GestureHand.Right);
            }
            if (visualRoot != null && visualRoot.activeSelf != show)
            {
                visualRoot.SetActive(show);
            }
        }

        private bool IsInChestZone(Vector3 wrist)
        {
            var headPos = head.position;
            var heightOk = wrist.y <= headPos.y - shoulderDropMeters &&
                           wrist.y >= headPos.y - waistDropMeters;
            if (!heightOk)
            {
                return false;
            }
            var flat = wrist - headPos;
            flat.y = 0f;
            return flat.magnitude <= chestDistanceMeters;
        }

        /// <summary>左手拍击弹匣换弹（docs/07 定案）：进入 0.15m 且速度 ≥阈值 判拍击，停留不触发</summary>
        private void TickSlapReload()
        {
            if (gestureHub == null || magazine == null || !deploy.Deployed)
            {
                hasLastLeftWrist = false;
                return;
            }
            if (!gestureHub.TryGetWristPosition(GestureHand.Left, out var leftWrist))
            {
                hasLastLeftWrist = false;
                return;
            }
            var speed = 0f;
            if (hasLastLeftWrist)
            {
                speed = (leftWrist - lastLeftWrist).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            }
            lastLeftWrist = leftWrist;
            hasLastLeftWrist = true;

            var within = (leftWrist - magazine.position).magnitude <= slapRadiusMeters;
            if (slap.Update(within, speed, Time.deltaTime) && state.StartReload())
            {
                SfxBus.Game2D(SfxBus.Lib != null ? SfxBus.Lib.magSnap : null);
                Debug.Log($"[MoriGame] 拍击弹匣换弹（速度 {speed:0.0} m/s，剩余 {state.ReloadRemainingSeconds:0.0}s）");
            }
        }

        private void HandleFireInput()
        {
            var blocked = !deploy.Deployed || BuildManager.IsPlacingActive ||
                MatchBootstrap.Instance != null && MatchBootstrap.Instance.Vitals != null &&
                MatchBootstrap.Instance.Vitals.State == Player.VitalsState.Downed;
            if (!blocked && gestureHub != null && gestureHub.TrackingEverActive(GestureHand.Right))
            {
                // 真机：手不在胸前有效区（含已放下/追踪丢失）食指伸直=无效（静默防误触）；
                // 编辑器无手势追踪（TrackingEverActive 恒 false）跳过此门，鼠标回退可用
                var wrist = Vector3.zero;
                if (!gestureHub.TryGetWristPosition(GestureHand.Right, out wrist) || !IsInChestZone(wrist))
                {
                    blocked = true;
                }
            }
            var hold = !blocked && (indexExtendedNow || Input.GetMouseButton(0)); // 食指伸直持续开火；编辑器回退鼠标左键
            if (!hold)
            {
                return;
            }
            var result = state.TryFire(); // 伸直期间按射速连发，食指弯曲即停火
            if (result == FireResult.Empty)
            {
                state.StartReload(); // 辅助换弹：弹匣空自动开始
                return;
            }
            if (result != FireResult.Fired)
            {
                return;
            }
            ShotCount++;
            Fire();
        }

        /// <summary>枪口蓄势光（2026-11-03 食指伸直开火）：伸直+有效区=激活（轻微呼吸），
        /// "现在指着就会开火"的状态感；不在有效区=完全静默。凝视 UI/放置中不亮（那时不会开火）。</summary>
        private void UpdateChargeGlow()
        {
            if (chargeGlow == null)
            {
                return;
            }
            var zoneOk = deploy.Deployed && indexExtendedNow &&
                         !BuildManager.IsPlacingActive &&
                         (MatchBootstrap.Instance == null || MatchBootstrap.Instance.Vitals == null ||
                          MatchBootstrap.Instance.Vitals.State != Player.VitalsState.Downed);
            if (zoneOk && gestureHub != null && gestureHub.TrackingEverActive(GestureHand.Right))
            {
                var wrist = Vector3.zero;
                zoneOk = gestureHub.TryGetWristPosition(GestureHand.Right, out wrist) && IsInChestZone(wrist);
            }
            if (chargeGlow.activeSelf != zoneOk)
            {
                chargeGlow.SetActive(zoneOk);
            }
            if (!zoneOk)
            {
                return;
            }
            if (!chargeGlowScaleCaptured)
            {
                chargeGlowBaseScale = chargeGlow.transform.localScale;
                chargeGlowScaleCaptured = true;
            }
            var pulse = 1f + 0.18f * Mathf.Sin(Time.time * 9f); // 9Hz 轻微呼吸
            chargeGlow.transform.localScale = chargeGlowBaseScale * pulse;
        }

        private void HandleReloadInput()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.R))
            {
                state.StartReload();
            }
#endif
        }

        private void Fire()
        {
            if (MatchBootstrap.Instance != null)
            {
                MatchBootstrap.Instance.Vitals?.BreakProtection(); // 主动射击提前结束重生保护
            }
            // 双瞄准模式（红线：出包前 BuildAimMode 选定，游戏内不切换）：
            // A 视线（头部射线，弹丸枪口→视线命中点）/ B 枪管（muzzle + 枪管 forward 直射，判定与视觉同线）
            var useBarrel = aimMode == AimMode.Barrel && muzzle != null;
            var ray = useBarrel
                ? new Ray(muzzle.position, muzzle.forward)
                : new Ray(head.position, head.forward);
            var hitSomething = PlayerStationFilter.Raycast(ray, gazeMaxMeters, out var hit); // 穿透工作台/高塔/补给箱（2026-11-09）
            var start = muzzle != null ? muzzle.position : ray.origin;
            var end = hitSomething ? hit.point : ray.origin + ray.direction * gazeMaxMeters;

            feedbackRemaining = feedbackSeconds;
            if (muzzle != null)
            {
                Vfx.VfxBus.Play(l => l.muzzleShot, muzzle.position, muzzle.rotation);
                Vfx.VfxBus.Play(l => l.shellEject, muzzle.position); // 弹壳（2026-11-11 特效系统替换 Renderer 闪光）
            }
            SfxBus.Game2D(SfxBus.Lib != null ? SfxBus.Lib.playerFire : null); // 每发一响（2026-11-11：PlayOneShot 重叠混音，射速快于音长也每发可闻）
            // 2026-11-03 弹丸降速（80→25）连带：结算改回开火瞬间——命中判定即时，弹丸纯视觉慢飞
            // （原"到达即结算"在 25 米/秒下 8 米目标延迟 0.3 秒，手感变钝；僵尸死亡演出与弹丸视觉脱钩可接受）
            Settle(hitSomething, hit);
            if (projectilePool != null)
            {
                projectilePool.Spawn(start, end, null); // 纯视觉弹道
            }
        }

        /// <summary>弹丸到达命中点结算（2026-11-02 真实弹丸）：伤害/脉冲/音效随弹丸落点，视觉与判定同路径</summary>
        private void Settle(bool hitSomething, RaycastHit hit)
        {
            if (!hitSomething)
            {
                return;
            }
            var zombie = hit.collider.GetComponentInParent<ZombieAgent>();
            if (zombie == null || !zombie.IsAlive)
            {
                return;
            }
            var weak = hit.collider.GetComponent<ZombieWeakpoint>() != null;
            var legs = hit.collider.GetComponent<ZombieLeg>() != null;
            var killed = zombie.ApplyHit(config.damagePerPellet * config.pelletsPerShot, weak,
                transform.position, legs);
            if (reticle != null)
            {
                reticle.Hit(weak, killed);
            }
            var lib = SfxBus.Lib;
            SfxBus.World(weak ? (lib != null ? lib.weakpointHit : null) : (lib != null ? lib.hit : null),
                hit.point, true); // pitch 0.95—1.05 防重复感
        }

        private void UpdateFeedback()
        {
            if (feedbackRemaining <= 0f)
            {
                return;
            }
            feedbackRemaining -= Time.deltaTime;
            if (feedbackRemaining <= 0f && muzzleFlash != null)
            {
                muzzleFlash.enabled = false; // 旧 Renderer 闪光保留字段可回退（2026-11-11 起枪口焰走 VFX）
            }
        }

        /// <summary>开火 loop 与射速解耦（2026-11-10 资产化）：食指伸直期间循环播整段，弯指/让位即停</summary>
    }
}
