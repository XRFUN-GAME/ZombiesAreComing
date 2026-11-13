using ByteDance.PICO.XR;
using MoriGame.Build;
using MoriGame.Configs;
using MoriGame.Economy;
using MoriGame.Gestures;
using MoriGame.Waves;
using TMPro;
using UnityEngine;

namespace MoriGame.Shop
{
    /// <summary>
    /// 展示位账务纯逻辑（docs/07 2026-11-05 范式终稿 + 2026-11-05 去库存修订，零 Unity 依赖可单测）：
    /// **无库存概念——钱够就能抓**。抓起即购买（扣款）；归位（拖回展示位/追踪丢失）与
    /// 扔掉（张掌前推）都是未落地，全额退款；放下进放置预览后账务移交 BuildManager
    /// （取消/封路回滚同样退款，落地不再有第二次扣款）。置灰 = 购买预检不过
    /// （商店关闭/未解锁/达部署上限/金币不足）。
    /// </summary>
    public sealed class ShopStandSession
    {
        public bool InHand { get; private set; }

        /// <summary>可抓判定（置灰的反命题）：购买预检（商店开放 + 解锁 + 部署上限 + 金币），不变异</summary>
        public static bool CanGrab(PropConfig prop, PlayerWallet wallet,
            bool shopOpen, float matchElapsedSeconds, int placedForProp, int sharedNormalDeployed,
            int refundCredit = 0)
        {
            return PreviewBuy(prop, wallet, shopOpen, matchElapsedSeconds,
                       placedForProp, sharedNormalDeployed, refundCredit) == ShopBuyResult.Ok;
        }

        /// <summary>购买预检（不变异）；直接换建筑时 refundCredit 把旧件全额退款计入可用金币</summary>
        public static ShopBuyResult PreviewBuy(PropConfig prop, PlayerWallet wallet,
            bool shopOpen, float matchElapsedSeconds, int placedForProp, int sharedNormalDeployed,
            int refundCredit = 0)
        {
            if (!shopOpen)
            {
                return ShopBuyResult.ShopClosed;
            }
            if (matchElapsedSeconds < prop.unlockAtSeconds)
            {
                return ShopBuyResult.NotUnlocked;
            }
            var capReached = prop.maxPerPlayer > 0
                ? placedForProp >= prop.maxPerPlayer
                : sharedNormalDeployed >= ShopService.SharedNormalFacilityCap;
            if (capReached)
            {
                return ShopBuyResult.CapReached;
            }
            return wallet.Gold + refundCredit >= prop.price ? ShopBuyResult.Ok : ShopBuyResult.NoMoney;
        }

        /// <summary>抓起即购买：预检通过后立即扣款（未落地前所有路径退款）</summary>
        public ShopBuyResult Grab(PlayerWallet wallet, PropConfig prop,
            bool shopOpen, float matchElapsedSeconds, int placedForProp, int sharedNormalDeployed)
        {
            if (InHand)
            {
                return ShopBuyResult.CapReached; // 一手一件（本展示位已在手）
            }
            var result = PreviewBuy(prop, wallet, shopOpen, matchElapsedSeconds,
                placedForProp, sharedNormalDeployed);
            if (result != ShopBuyResult.Ok)
            {
                return result;
            }
            if (!wallet.TrySpend(prop.price))
            {
                return ShopBuyResult.NoMoney; // 双保险（预检已查，防并发改钱）
            }
            InHand = true;
            return ShopBuyResult.Ok;
        }

        /// <summary>直接换建筑：先按“余额+旧件退款”预检，成功才归位旧件并购买新件；失败旧件与余额均不动</summary>
        public ShopBuyResult GrabReplacing(PlayerWallet wallet, PropConfig prop,
            ShopStandSession previous, PropConfig previousProp,
            bool shopOpen, float matchElapsedSeconds, int placedForProp, int sharedNormalDeployed)
        {
            var refundCredit = previous != null && previous.InHand && previousProp != null
                ? previousProp.price
                : 0;
            var result = PreviewBuy(prop, wallet, shopOpen, matchElapsedSeconds,
                placedForProp, sharedNormalDeployed, refundCredit);
            if (result != ShopBuyResult.Ok)
            {
                return result;
            }
            previous?.Dock(wallet, previousProp);
            return Grab(wallet, prop, shopOpen, matchElapsedSeconds, placedForProp, sharedNormalDeployed);
        }

        /// <summary>归位（拖回展示位/追踪丢失自动回位）：未落地，全额退款</summary>
        public void Dock(PlayerWallet wallet, PropConfig prop)
        {
            if (InHand)
            {
                wallet.Add(prop.price);
            }
            InHand = false;
        }

        /// <summary>扔掉（张掌前推）：无库存概念，未落地同归位——全额退款</summary>
        public void Discard(PlayerWallet wallet, PropConfig prop)
        {
            Dock(wallet, prop);
        }

        /// <summary>放下进放置预览：账务移交 BuildManager（取消/封路回滚由其退款），会话清空</summary>
        public void Dropped()
        {
            InHand = false;
        }
    }

    /// <summary>
    /// 场景实体商店展示位（docs/07 2026-11-05 范式终稿 + 去库存修订：废除腕部菜单商店/仓库 UI，
    /// 物理"拿起来放下去"，Meta/Vision Pro 直接触碰抓取）：
    /// **悬停**：右手手腕/食指尖/中指尖就近点入 8cm 悬停区 → 模型微发光 + 浮空“捏合拿起”
    /// （置灰态也发光=可交互提示；直接读关节，绕过 belowViewDropMeters“放下”启发式）；
    /// **抓起**（右手捏合上升沿，或悬停中捏合保持 0.25s 兜"先捏后入区"的边沿错过）= 购买
    /// （钱够即抓，扣款），模型克隆体吸附跟手；**放下**（再捏合）→
    /// BuildManager 放置预览（账务移交）；**归位** = 拖回展示位 15cm 圈（抓起后须先离圈才武装，
    /// 防同点瞬归）；**扔掉** = 右手张掌前推；**追踪丢失** → 自动归位——未落地路径全部退款。
    /// 展示位常驻（战斗中可补建筑）；置灰 = 钱不够/达上限，捏合给 Toast 失败原因（1s 冷却防刷屏）。
    /// 凝视仅做悬停高亮辅助，不做主操作。编辑器回归：grabKey 抓/放，Q 归位，V 扔掉。
    /// </summary>
    public class ShopStand : MonoBehaviour
    {
        [Header("引用（MoriSceneSetup 注入）")]
        public PicoGestureHub hub;
        public PropConfig prop;
        [Tooltip("共享普通设施上限的同组道具（maxPerPlayer=0 互算上限，含自身）；单方上限道具留空")]
        public PropConfig[] sharedGroup;
        [Tooltip("展示模型根（悬停发光/置灰对象；抓取时克隆跟手，原位模型常驻如货架）")]
        public Transform modelRoot;
        [Tooltip("名称价格标签（billboard 由本组件驱动）")]
        public TMP_Text nameLabel;
        public Transform labelRoot;
        public Material hoverMaterial;
        public Material greyMaterial;
        [Tooltip("仅悬停时显示的浮空“捏合拿起”提示")]
        public GameObject hoverHint;
        [Tooltip("悬停判定半径（米）——手腕/食指尖/中指尖到模型中心的最小距离")]
        public float hoverRadiusMeters = 0.08f;
        [Tooltip("归位判定半径（米）——抓起后须先离开此圈才允许放回（防同点瞬归）")]
        public float dockRadiusMeters = 0.15f;

        [Header("编辑器回归键")]
        public KeyCode grabKey = KeyCode.None;

        /// <summary>当前在手展示位；抓新建筑会全额退款归位旧件，不再拦截</summary>
        public static ShopStand Carrying { get; private set; }

        /// <summary>任意展示位成功抓起累计（M2-T0 引导步骤②信号源）</summary>
        public static int TotalGrabCount { get; private set; }

        private static readonly System.Collections.Generic.List<ShopStand> Active =
            new System.Collections.Generic.List<ShopStand>();

        private void OnEnable()
        {
            Active.Add(this);
        }

        /// <summary>右手腕此刻是否落在任一展示位悬停区内（含 4cm 余量）——BuildManager 放置确认
        /// 据此让位（同一捏合不同时抓起+确认；几何判定与脚本执行顺序无关）。</summary>
        public static bool RightWristNearAnyStand()
        {
            for (var i = 0; i < Active.Count; i++)
            {
                var s = Active[i];
                if (s != null && s.modelRoot != null && s.TryGetRightWrist(out var wrist) &&
                    (wrist - s.modelRoot.position).magnitude <= s.hoverRadiusMeters + 0.04f)
                {
                    return true;
                }
            }
            return false;
        }

        private const int WristJointIndex = (int)HandJoint.JointWrist;

        private readonly ShopStandSession session = new ShopStandSession();
        private bool carried;
        private GameObject carriedGo;
        private Renderer[] modelRenderers;
        private Material[] originalMaterials;
        private bool leftDockZone = true;
        private bool greyedOut;
        private bool hoverLit;

        private void Awake()
        {
            if (modelRoot != null)
            {
                modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
                originalMaterials = new Material[modelRenderers.Length];
                for (var i = 0; i < modelRenderers.Length; i++)
                {
                    originalMaterials[i] = modelRenderers[i].sharedMaterial;
                }
            }
        }

        private void OnDestroy()
        {
            if (Carrying == this)
            {
                Carrying = null;
            }
        }

        private void OnDisable()
        {
            Active.Remove(this);
            SetHover(false);
            CleanupCarry(); // 对局结束禁用等：视觉回收，账务按"扔掉"语义（账上不动）
        }

        private void Update()
        {
            var match = MatchBootstrap.Instance;
            RefreshDisplay(match);

            var wristOk = TryGetRightWrist(out var wrist);
            // 状态机 if/else 单帧只跑一支：抓起/放下当帧不被下一支消费同一捏合边沿
            if (!carried)
            {
                TickIdle(match, wristOk, wrist);
            }
            else
            {
                TickCarried(match, wristOk, wrist);
            }
        }

        private void LateUpdate()
        {
            if (labelRoot == null)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            var toCam = cam.transform.position - labelRoot.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.0001f)
            {
                labelRoot.rotation = Quaternion.LookRotation((-toCam).normalized); // Canvas 正面 -Z
            }
        }

        // ---------- 待机：悬停发光 + 捏合抓起 ----------

        private void TickIdle(MatchBootstrap match, bool wristOk, Vector3 wrist)
        {
            var hoveredNow = wristOk && modelRoot != null &&
                             HandDistanceToModel(wrist) <= hoverRadiusMeters;
            if (hoveredNow != hoverLit)
            {
                SetHover(hoveredNow);
                if (hoveredNow)
                {
                    ShowHoverHintOnce();
                }
            }

            if (match == null || prop == null)
            {
                hoverPinchHold = 0f;
                grabCooldown = 0f;
                return;
            }
            grabCooldown = Mathf.Max(0f, grabCooldown - Time.deltaTime);

            var trackedPinching = wristOk && hub != null &&
                                  hub.IsTracked(GestureHand.Right) && hub.Pinching(GestureHand.Right);
            // 捏合保持 0.25s 容错：边伸手边捏时上升沿在入悬停区前已消耗（真机"捏了没反应"根因），
            // 悬停中持续捏住照样抓；到位后再捏的仍走上升沿即时抓
            if (hoveredNow && trackedPinching)
            {
                hoverPinchHold += Time.deltaTime;
            }
            else
            {
                hoverPinchHold = 0f;
            }
            var pinchGrab = hoveredNow && hub != null &&
                            hub.IsTracked(GestureHand.Right) && hub.PinchDown(GestureHand.Right) ||
                            hoverPinchHold >= PinchHoldGrabSeconds;
            if (pinchGrab || Input.GetKeyDown(grabKey))
            {
                DoGrab(match);
            }
        }

        /// <summary>悬停中捏合保持此时长即抓取（秒）——兜住"先捏后入区"的边沿错过</summary>
        private const float PinchHoldGrabSeconds = 0.25f;

        private float hoverPinchHold;
        private float grabCooldown;

        /// <summary>手到模型中心就近距离：手腕/食指尖/中指尖取最小——伸手够模型时指尖在前</summary>
        private float HandDistanceToModel(Vector3 wrist)
        {
            var best = (wrist - modelRoot.position).magnitude;
            if (hub != null && hub.rightHand != null && hub.rightHand.handJoints != null)
            {
                best = Mathf.Min(best, JointDistance(HandJoint.JointIndexTip));
                best = Mathf.Min(best, JointDistance(HandJoint.JointMiddleTip));
            }
            return best;
        }

        private float JointDistance(HandJoint joint)
        {
            var index = (int)joint;
            var joints = hub.rightHand.handJoints;
            if (index >= joints.Count || joints[index] == null)
            {
                return float.MaxValue;
            }
            return (joints[index].position - modelRoot.position).magnitude;
        }

        private static bool hoverHintShown;

        /// <summary>首次悬停教学（每局每设备一次）：伸手发光→捏合抓起</summary>
        private static void ShowHoverHintOnce()
        {
            if (hoverHintShown)
            {
                return;
            }
            hoverHintShown = true;
            Toast("伸手靠近模型发光后捏合抓起");
        }

        /// <summary>结算流水：抓起净额按方向归类（+退款 / −购买；M2-T6 金币收支分类）</summary>
        private static void RecordGoldDelta(MatchBootstrap match, int delta)
        {
            if (match?.Recorder == null || delta == 0)
            {
                return;
            }
            match.Recorder.RecordGold(delta > 0 ? GoldLedgerCategory.Refund : GoldLedgerCategory.Build, delta);
        }

        private void DoGrab(MatchBootstrap match)
        {
            if (grabCooldown > 0f)
            {
                return; // 失败后 1s 冷却：捏合保持通道会持续重试，防 Toast 刷屏
            }
            var previous = Carrying != null && Carrying != this ? Carrying : null;
            var goldBefore = match.Wallet.Gold;
            var result = session.GrabReplacing(match.Wallet, prop,
                previous != null ? previous.session : null, previous != null ? previous.prop : null,
                match.ShopOpen, match.MatchElapsedSeconds, PlacedCountFor(prop), SharedDeployed(match));
            if (result != ShopBuyResult.Ok)
            {
                grabCooldown = 1f;
                Toast(Translate(result), true);
                MoriGame.Audio.SfxBus.Ui(MoriGame.Audio.SfxBus.Lib != null ? MoriGame.Audio.SfxBus.Lib.fail : null);
                return;
            }
            RecordGoldDelta(match, match.Wallet.Gold - goldBefore); // 换件=退款+扣款净额，按方向归类
            previous?.CleanupCarry(); // GrabReplacing 已退旧件；这里只回收旧跟手模型并清除全局在手标记
            carried = true;
            Carrying = this;
            leftDockZone = false;
            TotalGrabCount++;
            BuildManager.Instance?.CancelPlacement(); // 抓新件时旧放置预览让位（取消件由 BuildManager 退款）
            Object.FindFirstObjectByType<Combat.WeaponHolster>()?.ReturnToSlot(); // 枪与建筑互斥，购买失败不影响持枪
            SetHover(false);
            if (modelRoot != null)
            {
                carriedGo = Instantiate(modelRoot.gameObject);
                carriedGo.name = "Carried_" + prop.propId;
                carriedGo.transform.localScale = modelRoot.localScale; // 手持=悬浮展示同尺寸（用户 2026-11-12：全尺寸挡视野）；落地由 BuildManager 生成全尺寸实体
                foreach (var c in carriedGo.GetComponentsInChildren<Collider>(true))
                {
                    Destroy(c);
                }
            }
            var lib = MoriGame.Audio.SfxBus.Lib;
            MoriGame.Audio.SfxBus.Ui(lib != null ? lib.grab : null);
            MoriGame.Audio.SfxBus.Ui(lib != null ? lib.purchaseSuccess : null); // 抓起即购买：抓取声 + 成功叮
            Vfx.VfxBus.Play(l => l.purchaseSuccess, transform.position + Vector3.up * 0.3f); // 购买成功特效（2026-11-11）
            Toast($"已购 {prop.displayName}（余 {match.Wallet.Gold} 金币）——看向地面捏合放下");
            Debug.Log($"[MoriGame] 展示位抓起 {prop.displayName}（扣款，余 {match.Wallet.Gold} 金币）");
        }

        // ---------- 在手：跟手 + 放下/归位/扔掉/追踪丢失 ----------

        private void TickCarried(MatchBootstrap match, bool wristOk, Vector3 wrist)
        {
            if (!wristOk)
            {
                DockIt(match, "追踪丢失，已自动放回"); // 防悬空（docs/07：抓取失败/追踪丢失→自动回原位）
                return;
            }
            if (carriedGo != null)
            {
                carriedGo.transform.position = wrist + Vector3.up * 0.06f;
            }
            var dockDist = modelRoot != null ? (wrist - modelRoot.position).magnitude : float.MaxValue;
            if (dockDist > dockRadiusMeters)
            {
                leftDockZone = true; // 离圈后才武装归位（抓起瞬间手就在圈内，否则当帧瞬归）
            }
            if (leftDockZone && dockDist <= dockRadiusMeters)
            {
                DockIt(match, "已放回展示位");
                return;
            }
            var dropPinch = hub != null && hub.IsTracked(GestureHand.Right) &&
                            hub.PinchDown(GestureHand.Right);
            if (dropPinch || Input.GetKeyDown(grabKey))
            {
                DropIt();
                return;
            }
            if (hub != null && hub.PalmPushDown(GestureHand.Right) || Input.GetKeyDown(KeyCode.V))
            {
                DiscardIt();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Q))
            {
                DockIt(match, "已放回展示位");
            }
        }

        /// <summary>放下：移交 BuildManager 放置预览（已扣款，取消/封路回滚由 BuildManager 退款）</summary>
        private void DropIt()
        {
            if (BuildManager.Instance == null)
            {
                Debug.LogWarning("[MoriGame] 无 BuildManager，展示位放下按归位处理");
                DockIt(MatchBootstrap.Instance, "已放回展示位");
                return;
            }
            session.Dropped();
            CleanupCarry();
            BuildManager.Instance.BeginPlacement(prop);
            Toast("凝视地面：绿=可放 青绿=扩格 红=不可放；捏合落地，张掌取消");
            Debug.Log($"[MoriGame] 展示位放下 {prop.displayName} → 放置预览");
        }

        /// <summary>跨交互归位入口（抓枪/其他系统让建筑离手）：未落地全额退款，重复调用无副作用</summary>
        public void ReturnToStand()
        {
            if (carried)
            {
                DockIt(MatchBootstrap.Instance, "已放回展示位");
            }
        }

        /// <summary>归位：未落地全额退款</summary>
        private void DockIt(MatchBootstrap match, string message)
        {
            if (match != null && prop != null)
            {
                session.Dock(match.Wallet, prop);
                match.Recorder?.RecordGold(GoldLedgerCategory.Refund, prop.price);
            }
            CleanupCarry();
            var sfxLib = MoriGame.Audio.SfxBus.Lib;
            MoriGame.Audio.SfxBus.Ui(sfxLib != null ? sfxLib.dock : null);
            MoriGame.Audio.SfxBus.Coin(prop.price); // 归位/扔掉=全额退款，小额入账音
            Toast(match != null ? $"{message}（退款 {prop.price} 金币）" : message);
            Debug.Log($"[MoriGame] 展示位归位 {prop.displayName}（退款 {prop.price} 金币）");
        }

        /// <summary>扔掉：无库存概念，未落地同归位——全额退款</summary>
        private void DiscardIt()
        {
            var match = MatchBootstrap.Instance;
            if (match != null && prop != null)
            {
                session.Discard(match.Wallet, prop);
                match.Recorder?.RecordGold(GoldLedgerCategory.Refund, prop.price);
            }
            CleanupCarry();
            Toast(match != null ? $"已扔掉（退款 {prop.price} 金币）" : "已扔掉");
            Debug.Log($"[MoriGame] 展示位扔掉 {prop.displayName}（退款 {prop.price} 金币）");
        }

        private void CleanupCarry()
        {
            SetHover(false);
            if (carriedGo != null)
            {
                Destroy(carriedGo);
                carriedGo = null;
            }
            carried = false;
            leftDockZone = true;
            session.Dropped(); // 幂等复位会话不放钱（Dock/Discard 已退款则 InHand 已 false；OnDisable 对局拆除路径无从退款）
            if (Carrying == this)
            {
                Carrying = null;
            }
        }

        // ---------- 显示：置灰（钱不够/达上限）+ 悬停发光 ----------

        private void RefreshDisplay(MatchBootstrap match)
        {
            if (match == null || prop == null)
            {
                return;
            }
            var previous = Carrying != null && Carrying != this ? Carrying : null;
            var refundCredit = previous != null && previous.session.InHand ? previous.prop.price : 0;
            var grey = !ShopStandSession.CanGrab(prop, match.Wallet, match.ShopOpen,
                match.MatchElapsedSeconds, PlacedCountFor(prop), SharedDeployed(match), refundCredit);
            if (grey != greyedOut)
            {
                greyedOut = grey;
                ApplyModelMaterials();
                var labelColor = grey ? new Color(0.55f, 0.56f, 0.6f) : Color.white;
                if (nameLabel != null)
                {
                    nameLabel.color = labelColor;
                }
            }
        }

        private void SetHover(bool hovered)
        {
            hoverLit = hovered;
            if (hoverHint != null)
            {
                hoverHint.SetActive(hovered);
            }
            ApplyModelMaterials(); // 悬停优先于置灰：可交互反馈不因金币不足消失
        }

        private void ApplyModelMaterials()
        {
            if (modelRenderers == null)
            {
                return;
            }
            for (var i = 0; i < modelRenderers.Length; i++)
            {
                if (modelRenderers[i] == null)
                {
                    continue;
                }
                modelRenderers[i].sharedMaterial =
                    hoverLit && hoverMaterial != null ? hoverMaterial // 悬停优先：置灰态也发光（可交互提示）
                    : greyedOut && greyMaterial != null ? greyMaterial
                    : originalMaterials[i];
            }
        }

        private int PlacedCountFor(PropConfig p)
        {
            return BuildManager.Instance != null ? BuildManager.Instance.PlacedCount(p.propId) : 0;
        }

        /// <summary>共享普通设施（maxPerPlayer=0）已落地合计（docs/02 共享上限 30）</summary>
        private int SharedDeployed(MatchBootstrap match)
        {
            var total = 0;
            if (sharedGroup == null)
            {
                return total;
            }
            foreach (var p in sharedGroup)
            {
                if (p == null)
                {
                    continue;
                }
                total += PlacedCountFor(p);
            }
            return total;
        }

        /// <summary>右手腕原始关节位置（绕过 belowViewDropMeters——展示位低至 0.5 米，
        /// 抓握手腕必然低于"放下"启发式阈值；Computed 真追踪态下关节数据可信）</summary>
        private bool TryGetRightWrist(out Vector3 wrist)
        {
            wrist = default;
            if (hub == null || !hub.IsTracked(GestureHand.Right) || hub.rightHand == null ||
                hub.rightHand.handJoints == null || hub.rightHand.handJoints.Count <= WristJointIndex ||
                hub.rightHand.handJoints[WristJointIndex] == null)
            {
                return false;
            }
            wrist = hub.rightHand.handJoints[WristJointIndex].position;
            return true;
        }

        private static void Toast(string message, bool failed = false)
        {
            if (WaveDirector.Instance != null)
            {
                WaveDirector.Instance.ShowToast(message,
                    failed ? new Color(1f, 0.45f, 0.4f) : (Color?)null);
            }
            else
            {
                Debug.Log($"[MoriGame] 展示位提示：{message}");
            }
        }

        private static string Translate(ShopBuyResult r)
        {
            switch (r)
            {
                case ShopBuyResult.ShopClosed: return "商店停用（倒地或中控瘫痪）";
                case ShopBuyResult.NotUnlocked: return "尚未解锁";
                case ShopBuyResult.NoMoney: return "金币不足";
                case ShopBuyResult.CapReached: return "已达部署上限";
                default: return r.ToString();
            }
        }
    }
}
