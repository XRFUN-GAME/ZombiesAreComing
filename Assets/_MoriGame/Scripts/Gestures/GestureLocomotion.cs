using ByteDance.PICO.XR;
using MoriGame.Build;
using MoriGame.Territory;
using UnityEngine;

namespace MoriGame.Gestures
{
    /// <summary>
    /// **整组件已废弃（2026-11-06 用户决策）**：侧推转向移除（站桩制玩家完全固定，360° 受敌靠
    /// 眼睛/重心观察侧翼），MoriSceneSetup 不再挂载本组件；传送 2026-11-05 先行移除。
    /// 传送与转向实现完整留档（<see cref="TickTeleport"/> / 侧推检测），备移动玩法回挂。
    /// </summary>
    public class GestureLocomotion : MonoBehaviour
    {
        [Header("快速转向（已废弃留档）")]
        [Tooltip("单次侧推转向角度（度）；连推可叠加（45°→90°→…）")]
        public float turnDegrees = 45f;
        [Tooltip("侧推速度阈值（米/秒）——手速在玩家该手外侧方向的分量")]
        public float turnSpeedMps = 0.9f;
        [Tooltip("侧推需持续的时间（秒）")]
        public float turnHoldSeconds = 0.12f;
        [Tooltip("两次转向的最小间隔（秒）——连推叠加节奏")]
        public float turnCooldownSeconds = 0.5f;

        [Header("引用（MoriSceneSetup 注入）")]
        public PicoGestureHub hub;

        [Header("传送（已禁用，字段留档）")]
        public float maxTeleportMeters = 3f;
        public float cooldownSeconds = 2f;
        [Tooltip("掌心朝上判定：左手掌心法线与竖直向上的夹角阈值（度），小于才算朝上")]
        public float palmUpMaxAngle = 45f;

        /// <summary>会话内快速转向次数（M2-T0 引导图例淡出/被咬转向提示的信号源）</summary>
        public int TurnCount { get; private set; }

        // ---- 传送留档字段（禁用，TickTeleport 专用） ----
        /// <summary>CD 剩余比例（1=刚传完 → 0=就绪），供视野下方环形倒计时读取</summary>
        public float CooldownFraction =>
            cooldownSeconds <= 0f ? 0f : Mathf.Clamp01(cooldown / cooldownSeconds);

        /// <summary>会话内成功传送次数（站桩制后恒 0——TickTeleport 不再被调用）</summary>
        public int TeleportCount { get; private set; }
        public float arenaHalfSize = 13.4f;
        public Material markerReadyMaterial;    // 就绪蓝（呼吸）
        public Material markerBlockedMaterial;  // 非法红
        public Material markerCooldownMaterial; // CD 灰

        private const float BreathHz = 2.2f;
        private const float BreathScale = 0.12f;
        private const float MarkerDiameter = 0.6f;
        private const float PalmExtendedTipMeters = 0.11f; // 张掌口径与 PicoGestureHub 一致（中指尖离腕）

        private SnapTurnDetector leftTurn;
        private SnapTurnDetector rightTurn;
        private readonly Vector3[] lastWristPos = new Vector3[2];
        private readonly bool[] hasLastWrist = new bool[2];

        private Transform head;
        private Transform marker;
        private MeshRenderer markerRenderer;
        private float cooldown;

        private void Awake()
        {
            leftTurn = new SnapTurnDetector(turnSpeedMps, turnHoldSeconds, turnCooldownSeconds);
            rightTurn = new SnapTurnDetector(turnSpeedMps, turnHoldSeconds, turnCooldownSeconds);
        }

        private void Update()
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

            // 放置模式转向让位（左捏=旋转 / 张掌前推=取消，T3.5 语义沿用）
            if (BuildManager.IsPlacingActive)
            {
                TickTurnDetectors(false, 0f, false, 0f);
                hasLastWrist[0] = false;
                hasLastWrist[1] = false;
                return;
            }

            var turned = false;
            var angle = 0f;
            if (TryTurn(GestureHand.Left, -1f, leftTurn))
            {
                angle -= turnDegrees;
                turned = true;
            }
            if (TryTurn(GestureHand.Right, 1f, rightTurn))
            {
                angle += turnDegrees;
                turned = true;
            }
            if (turned)
            {
                TurnCount++;
                var pivot = new Vector3(head.position.x, transform.position.y, head.position.z);
                transform.RotateAround(pivot, Vector3.up, angle); // 绕头部原地转，工作台保持世界固定
            }
        }

        /// <summary>单手侧推转向检测。sideSign=-1 左手（触发方向=玩家左）、+1 右手。</summary>
        private bool TryTurn(GestureHand hand, float sideSign, SnapTurnDetector detector)
        {
            var extended = false;
            var lateral = 0f;
            var wrist = JointPosition(hand, HandJoint.JointWrist);
            if (wrist != null)
            {
                var middleTip = JointPosition(hand, HandJoint.JointMiddleTip);
                extended = middleTip != null &&
                           Vector3.Distance(middleTip.position, wrist.position) > PalmExtendedTipMeters &&
                           !IsPinching(hand);
                var slot = (int)hand;
                if (hasLastWrist[slot])
                {
                    var velocity = (wrist.position - lastWristPos[slot]) / Mathf.Max(Time.deltaTime, 0.0001f);
                    // 触发方向 = 玩家该手外侧（左手=玩家左 = -right；右手=玩家右 = +right）
                    lateral = sideSign * Vector3.Dot(velocity, transform.right);
                }
                lastWristPos[slot] = wrist.position;
                hasLastWrist[slot] = true;
            }
            else
            {
                hasLastWrist[(int)hand] = false;
            }
            return detector.Update(extended, lateral, Time.deltaTime);
        }

        /// <summary>追踪丢失/离手帧也要推进检测器状态（清计时），防恢复瞬间残留触发</summary>
        private void TickTurnDetectors(bool leftExtended, float leftLateral, bool rightExtended, float rightLateral)
        {
            leftTurn.Update(leftExtended, leftLateral, Time.deltaTime);
            rightTurn.Update(rightExtended, rightLateral, Time.deltaTime);
        }

        private Transform JointPosition(GestureHand hand, HandJoint joint)
        {
            if (hub == null)
            {
                return null;
            }
            var h = hand == GestureHand.Left ? hub.leftHand : hub.rightHand;
            if (h == null || !h.Computed || h.handJoints == null || h.handJoints.Count <= (int)joint)
            {
                return null;
            }
            return h.handJoints[(int)joint];
        }

        private bool IsPinching(GestureHand hand)
        {
            var h = hand == GestureHand.Left ? hub.leftHand : hub.rightHand;
            return h != null && h.Pinch;
        }

        // ================= 传送实现（已禁用，docs/07 站桩制废除；代码留档备回挂） =================

        /// <summary>原传送主循环（2026-11-03 防误触定案版，原样保留）。站桩制下 Update 不再调用；
        /// 回挂方式：Update 末尾恢复调用本方法 + MoriSceneSetup 恢复材质注入与 CreateCooldownRing。</summary>
        private void TickTeleport()
        {
            cooldown -= Time.deltaTime; // CD 无条件按真实时间推进（2026-11-03 修复：不随掌心朝向暂停）

            if (BuildManager.IsPlacingActive)
            {
                if (marker != null && marker.gameObject.activeSelf)
                {
                    marker.gameObject.SetActive(false);
                }
                return;
            }

            var palmUp = hub != null && hub.TryGetPalmNormal(GestureHand.Left, out var palmNormal) &&
                         Vector3.Angle(palmNormal, Vector3.up) < palmUpMaxAngle;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var devConfirm = Input.GetKeyDown(KeyCode.V); // 编辑器无手势：V 键直传（绕过掌心判定）
#else
            var devConfirm = false;
#endif
            if (!palmUp && !devConfirm)
            {
                if (marker != null && marker.gameObject.activeSelf)
                {
                    marker.gameObject.SetActive(false);
                }
                return;
            }

            var inRange = GazeUtil.TryGetGroundPoint(head, maxTeleportMeters, arenaHalfSize, out var point);
            var blocked = false;
            if (inRange)
            {
                var cell = TerritoryGrid.WorldToCell(point.x, point.z);
                var build = BuildManager.Instance;
                blocked = build != null && !build.IsTeleportTargetValid(cell);
            }
            var ready = inRange && !blocked && cooldown <= 0f;
            if (marker != null)
            {
                marker.gameObject.SetActive(inRange);
                if (inRange)
                {
                    marker.position = point + Vector3.up * 0.02f;
                    var breathe = 1f + BreathScale * (Mathf.Sin(Time.time * Mathf.PI * 2f * BreathHz) * 0.5f + 0.5f);
                    var scale = blocked || cooldown > 0f ? MarkerDiameter : MarkerDiameter * breathe;
                    marker.localScale = new Vector3(scale, 0.01f, scale);
                    markerRenderer.sharedMaterial = blocked
                        ? markerBlockedMaterial
                        : cooldown > 0f ? markerCooldownMaterial : markerReadyMaterial;
                }
            }
            if (!ready)
            {
                return;
            }
            if (devConfirm || hub != null && hub.IsTracked(GestureHand.Left) && hub.Pinching(GestureHand.Left))
            {
                cooldown = cooldownSeconds;
                TeleportCount++;
                var target = new Vector3(point.x, 0f, point.z);
                if (TeleportFade.Instance != null)
                {
                    TeleportFade.Instance.FadeOutIn(() => transform.position = target);
                }
                else
                {
                    transform.position = target;
                }
            }
        }

        private void CreateMarker()
        {
            if (markerReadyMaterial == null || markerBlockedMaterial == null)
            {
                Debug.LogError("[MoriGame] GestureLocomotion 缺少传送环材质（MoriSceneSetup 注入失败）");
                return;
            }
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            marker.name = "GazeTeleportMarker";
            Destroy(marker.GetComponent<Collider>());
            marker.localScale = new Vector3(MarkerDiameter, 0.01f, MarkerDiameter);
            markerRenderer = marker.GetComponent<MeshRenderer>();
            markerRenderer.sharedMaterial = markerReadyMaterial;
        }
    }
}
