using ByteDance.PICO.XR;
using UnityEngine;

namespace MoriGame.Gestures
{
    public enum GestureHand : byte
    {
        Left = 0,
        Right = 1
    }

    /// <summary>
    /// M1-T3.6 手势中枢：包装 PICO 原生手部追踪（PXR_Hand，官方 HandLeft/HandRight prefab）为手势事件源。
    /// 范式（CLAUDE.md 交互变更）：右手捏合=确认/放置，左手捏合=传送移动，张掌前推=取消/拆除。
    /// 编辑器无手势追踪时各状态恒 false，链路自动回退键鼠/手柄。
    /// </summary>
    public class PicoGestureHub : MonoBehaviour
    {
        public PXR_Hand leftHand;
        public PXR_Hand rightHand;

        [Header("张掌前推判定")]
        [Tooltip("中指尖离腕超过此距离视为手指伸直（米）")]
        public float extendedTipMeters = 0.11f;

        [Tooltip("沿头部朝向的前推速度阈值（米/秒）")]
        public float pushSpeedMeters = 1.1f;

        [Tooltip("伸直+前推需持续的时间（秒）")]
        public float pushHoldSeconds = 0.12f;

        [Tooltip("触发后的冷却（秒）")]
        public float pushCooldownSeconds = 0.7f;

        [Header("手部隐藏（2026-11-02 修订：提前隐藏，防悬空手闪现）")]
        [Tooltip("追踪丢失/放下 → 立即隐藏（零宽限，SDK 关节冻结姿态绝不外露）；恢复显示需持续此时长防闪现")]
        public float showDelaySeconds = 0.15f;
        [Tooltip("手腕低于头部此深度视为已放下——仅作用于 TryGetWristPosition（冻结姿态位置不可信）。"
                 + "手势事件与手模显隐不看此值（2026-11-02 真机教训：坐姿指向地面放置信标被误杀）")]
        public float belowViewDropMeters = 0.65f;

        private readonly GestureEdgeDetector[] pinchEdges = { new GestureEdgeDetector(), new GestureEdgeDetector() };
        private readonly PalmPushDetector[] pushDetectors = new PalmPushDetector[2];
        private readonly float[] showHeldSeconds = new float[2];
        private readonly Vector3[] lastWristPos = new Vector3[2];
        private readonly bool[] hasLastPos = new bool[2];
        private readonly bool[] hiddenByHub = new bool[2];
        private readonly bool[] handVisible = new bool[2];
        private readonly bool[] everComputed = new bool[2];
        private Renderer[][] handRenderers = new Renderer[2][];
        private Transform head;

        /// <summary>手模当前是否显示（含 0.15s 防闪现迟滞）——武器显隐与此同步（2026-11-02 实测）</summary>
        public bool IsHandVisible(GestureHand hand) => handVisible[(int)hand];

        /// <summary>会话内该手是否出现过有效追踪——编辑器恒 false，武器显隐同步只在真机生效</summary>
        public bool TrackingEverActive(GestureHand hand) => everComputed[(int)hand];

        public bool PinchDown(GestureHand hand) => pinchEdges[(int)hand].Rising;
        public bool PalmPushDown(GestureHand hand) => pushDetectors[(int)hand].Triggered;
        public bool Pinching(GestureHand hand) { var h = GetHand(hand); return h != null && h.Pinch; }

        public bool IsTracked(GestureHand hand)
        {
            var h = GetHand(hand);
            return h != null && h.Computed;
        }

        /// <summary>官方手部追踪激活后的手腕世界位置（商店触碰悬停等用，2026-11-02 手部体系统一）。
        /// 手腕低于视野下缘（已放下）按未追踪处理。</summary>
        public bool TryGetWristPosition(GestureHand hand, out Vector3 position)
        {
            position = default;
            var h = GetHand(hand);
            if (h == null || !h.Computed)
            {
                return false;
            }
            var wrist = Joint(h, HandJoint.JointWrist);
            if (wrist == null)
            {
                return false;
            }
            if (head != null && wrist.position.y < head.position.y - belowViewDropMeters)
            {
                return false; // 手已放下：位置数据不可信（追踪冻结）
            }
            position = wrist.position;
            return true;
        }

        /// <summary>食指伸直判定（docs/07 2026-11-03"食指伸直=开火"；2026-11-04 真机修正"根"=食指近节）：
        /// 腕/食指近节/指尖三关节喂 IndexExtensionDetector 夹角法；阈值由消费方传入（Inspector 可调）。
        /// 根关节用近节而非掌指根（metacarpal）：掌骨沿掌基走向，以它为根时自然伸直夹角实测 30—40°，
        /// 要指尖上翘才触发；近节根下自然放松伸直夹角 10—20°，30° 阈值直接命中。
        /// 只看真实追踪态 Computed，不做"放下"启发式过滤（有效区门由 WeaponController 把）。</summary>
        public bool TryGetIndexExtended(GestureHand hand, float maxAngleDeg, out bool extended)
        {
            extended = false;
            var h = GetHand(hand);
            if (h == null || !h.Computed)
            {
                return false;
            }
            var wrist = Joint(h, HandJoint.JointWrist);
            var root = Joint(h, HandJoint.JointIndexProximal);
            var tip = Joint(h, HandJoint.JointIndexTip);
            if (wrist == null || root == null || tip == null)
            {
                return false;
            }
            extended = IndexExtensionDetector.IsExtended(wrist.position, root.position, tip.position, maxAngleDeg);
            return true;
        }

        private PXR_Hand GetHand(GestureHand hand) => hand == GestureHand.Left ? leftHand : rightHand;

        /// <summary>掌心外法线（2026-11-03 传送防误触：左手掌心朝上判定，方向由调用方比较）：
        /// 几何法 Cross(食指根-腕, 小指根-腕)，左手此序指向掌外侧；右手镜像需反序。</summary>
        public bool TryGetPalmNormal(GestureHand hand, out Vector3 normal)
        {
            normal = default;
            var h = GetHand(hand);
            if (h == null || !h.Computed)
            {
                return false;
            }
            var wrist = Joint(h, HandJoint.JointWrist);
            var index = Joint(h, HandJoint.JointIndexMetacarpal);
            var little = Joint(h, HandJoint.JointLittleMetacarpal);
            if (wrist == null || index == null || little == null)
            {
                return false;
            }
            var a = index.position - wrist.position;
            var b = little.position - wrist.position;
            normal = (hand == GestureHand.Left ? Vector3.Cross(a, b) : Vector3.Cross(b, a)).normalized;
            return true;
        }

        private void Awake()
        {
            for (var i = 0; i < 2; i++)
            {
                pushDetectors[i] = new PalmPushDetector(pushSpeedMeters, pushHoldSeconds, pushCooldownSeconds);
            }
        }

        private void Update()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    head = cam.transform;
                }
            }
            var forward = head != null
                ? new Vector3(head.forward.x, 0f, head.forward.z).normalized
                : Vector3.forward;

            for (var i = 0; i < 2; i++)
            {
                var hand = GetHand((GestureHand)i);
                if (hand == null)
                {
                    pinchEdges[i].Update(false);
                    pushDetectors[i].Update(false, 0f, Time.deltaTime);
                    continue;
                }

                if (hand.Computed)
                {
                    everComputed[i] = true;
                }

                var wrist = Joint(hand, HandJoint.JointWrist);
                // 手势事件（捏合/张掌）只看真实追踪态 Computed——2026-11-02 真机教训：
                // 建造放置时手要指向地面（坐姿下腕部低于"放下"启发式阈值），事件被误杀 → 放置确认/取消全失灵。
                // Computed 冻结期间 Pinch 冻结 → 边沿不触发；腕速度冻结为零 → 张掌不触发，天然安全。
                pinchEdges[i].Update(hand.Computed && hand.Pinch);

                var middleTip = Joint(hand, HandJoint.JointMiddleTip);
                var extended = hand.Computed && wrist != null && middleTip != null &&
                               Vector3.Distance(middleTip.position, wrist.position) > extendedTipMeters &&
                               !hand.Pinch;

                var forwardSpeed = 0f;
                if (wrist != null)
                {
                    if (hasLastPos[i])
                    {
                        var velocity = (wrist.position - lastWristPos[i]) / Mathf.Max(Time.deltaTime, 0.0001f);
                        forwardSpeed = Vector3.Dot(velocity, forward);
                    }
                    lastWristPos[i] = wrist.position;
                    hasLastPos[i] = true;
                }
                pushDetectors[i].Update(extended, forwardSpeed, Time.deltaTime);

                // 显隐同只看 Computed：指向地面的有效手不能藏（坐姿建造手模消失=失控感）；
                // 丢失零宽限立即藏（SDK 冻结姿态不外露），恢复需持续 showDelaySeconds 才重现，防恢复抖动闪现
                showHeldSeconds[i] = hand.Computed ? showHeldSeconds[i] + Time.deltaTime : 0f;
                var visible = hand.Computed && showHeldSeconds[i] >= showDelaySeconds;
                handVisible[i] = visible;
                if (hiddenByHub[i] != !visible)
                {
                    hiddenByHub[i] = !visible;
                    SetHandRenderers(i, visible);
                }
            }
        }

        /// <summary>管理手模全部渲染体，但**排除 SDK 自管的 RayPose 子树**（SDK 按 RayValid SetActive 射线，我们别打架）</summary>
        private void SetHandRenderers(int slot, bool visible)
        {
            var hand = GetHand((GestureHand)slot);
            if (hand == null)
            {
                return;
            }
            if (handRenderers[slot] == null)
            {
                var excluded = hand.transform.Find("RayPose");
                var kept = new System.Collections.Generic.List<Renderer>();
                foreach (var r in hand.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || (excluded != null && r.transform.IsChildOf(excluded)))
                    {
                        continue;
                    }
                    kept.Add(r);
                }
                handRenderers[slot] = kept.ToArray();
            }
            var renderers = handRenderers[slot];
            for (var r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] != null)
                {
                    renderers[r].enabled = visible;
                }
            }
        }

        private static Transform Joint(PXR_Hand hand, HandJoint joint)
        {
            var index = (int)joint;
            return index < hand.handJoints.Count ? hand.handJoints[index] : null;
        }
    }
}
