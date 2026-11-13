using ByteDance.PICO.XR;
using MoriGame.Build;
using MoriGame.Gestures;
using MoriGame.Shop;
using UnityEngine;

namespace MoriGame.Combat
{
    /// <summary>
    /// 站桩工作台枪械插槽：右手靠近并捏合即把真枪挂回手腕、立即装备；
    /// 持枪后先离开再进入插槽吸附圈，真枪 root 自动回工作台固定锚点。
    /// 放置模式中抓枪让位；编辑器 6 键抓起/放回。
    /// </summary>
    public class WeaponHolster : MonoBehaviour
    {
        [Header("引用（MoriSceneSetup 注入）")]
        public PicoGestureHub hub;
        public WeaponController weapon;
        public Transform slotAnchor;
        public Material hoverMaterial;
        [Tooltip("仅悬停时显示的浮空“捏合拿起”提示，根节点兼作 billboard")]
        public GameObject hoverHint;
        public float hoverRadiusMeters = 0.08f;
        public float dockRadiusMeters = 0.18f;

        [Header("编辑器回归键")]
        public KeyCode toggleKey = KeyCode.Alpha6;

        public static int GrabCount { get; private set; }
        public bool Held { get; private set; }

        private const float PinchHoldGrabSeconds = 0.25f;
        private const int WristJointIndex = (int)HandJoint.JointWrist;
        private const int IndexTipJointIndex = (int)HandJoint.JointIndexTip;
        private const int MiddleTipJointIndex = (int)HandJoint.JointMiddleTip;

        private Transform handParent;
        private Vector3 handLocalPosition;
        private Quaternion handLocalRotation;
        private bool leftDockZone;
        private float hoverPinchHold;
        private float grabCooldown;
        private Renderer[] weaponRenderers;
        private Material[] originalMaterials;
        private bool hoverLit;

        private void Start()
        {
            if (weapon == null || slotAnchor == null)
            {
                return;
            }
            handParent = weapon.transform.parent;
            handLocalPosition = weapon.transform.localPosition;
            handLocalRotation = weapon.transform.localRotation;
            weaponRenderers = weapon.GetComponentsInChildren<Renderer>(true);
            originalMaterials = new Material[weaponRenderers.Length];
            for (var i = 0; i < weaponRenderers.Length; i++)
            {
                originalMaterials[i] = weaponRenderers[i].sharedMaterial;
            }
            SnapToSlot();
            weapon.SetHolstered(true);
        }

        private void Update()
        {
            if (weapon == null || slotAnchor == null || handParent == null)
            {
                return;
            }
            var wristOk = TryGetHandPoint(out var wrist);
            if (Held)
            {
                SetHover(false);
                TickHeld(wristOk, wrist);
            }
            else
            {
                TickHolstered(wristOk, wrist);
            }
        }

        private void LateUpdate()
        {
            if (hoverHint == null || !hoverHint.activeSelf)
            {
                return;
            }
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            var toCam = cam.transform.position - hoverHint.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.0001f)
            {
                hoverHint.transform.rotation = Quaternion.LookRotation((-toCam).normalized);
            }
        }

        private void OnDisable()
        {
            SetHover(false);
        }

        private void TickHolstered(bool wristOk, Vector3 wrist)
        {
            if (BuildManager.IsPlacingActive)
            {
                hoverPinchHold = 0f;
                SetHover(false);
                return;
            }

            var hovered = wristOk && HandDistanceToSlot(wrist) <= hoverRadiusMeters;
            SetHover(hovered);
            var trackedPinching = wristOk && hub != null &&
                                  hub.IsTracked(GestureHand.Right) && hub.Pinching(GestureHand.Right);
            hoverPinchHold = hovered && trackedPinching ? hoverPinchHold + Time.deltaTime : 0f;
            grabCooldown = Mathf.Max(0f, grabCooldown - Time.deltaTime);

            var pinchGrab = hovered && hub != null &&
                            hub.IsTracked(GestureHand.Right) && hub.PinchDown(GestureHand.Right);
            if ((pinchGrab || hoverPinchHold >= PinchHoldGrabSeconds) && grabCooldown <= 0f ||
                Input.GetKeyDown(toggleKey))
            {
                GrabIt();
            }
        }

        private void GrabIt()
        {
            ShopStand.Carrying?.ReturnToStand(); // 枪与建筑互斥：未落地建筑归位并全额退款
            SetHover(false);
            Held = true;
            leftDockZone = false;
            hoverPinchHold = 0f;
            GrabCount++;
            weapon.transform.SetParent(handParent, false);
            weapon.transform.localPosition = handLocalPosition;
            weapon.transform.localRotation = handLocalRotation;
            weapon.Unholster();
        }

        private void TickHeld(bool wristOk, Vector3 wrist)
        {
            if (Input.GetKeyDown(toggleKey))
            {
                ReturnIt();
                return;
            }
            if (!wristOk)
            {
                return;
            }
            var dockDist = (wrist - slotAnchor.position).magnitude;
            if (dockDist > dockRadiusMeters)
            {
                leftDockZone = true;
            }
            if (leftDockZone && dockDist <= dockRadiusMeters)
            {
                ReturnIt();
            }
        }

        /// <summary>跨交互归位入口：建筑抓起时调用；已归位/空引用时不动</summary>
        public void ReturnToSlot()
        {
            if (Held && weapon != null && slotAnchor != null)
            {
                ReturnIt();
            }
        }

        private void ReturnIt()
        {
            Held = false;
            leftDockZone = true;
            grabCooldown = 1f;
            hoverPinchHold = 0f;
            weapon.SetHolstered(true);
            SnapToSlot();
        }

        private void SnapToSlot()
        {
            weapon.transform.SetParent(slotAnchor, false);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
        }

        private void SetHover(bool hovered)
        {
            if (hoverLit == hovered)
            {
                return;
            }
            hoverLit = hovered;
            if (hoverHint != null)
            {
                hoverHint.SetActive(hovered);
            }
            if (weaponRenderers == null)
            {
                return;
            }
            for (var i = 0; i < weaponRenderers.Length; i++)
            {
                if (weaponRenderers[i] != null)
                {
                    weaponRenderers[i].sharedMaterial =
                        hovered && hoverMaterial != null ? hoverMaterial : originalMaterials[i];
                }
            }
        }

        private float HandDistanceToSlot(Vector3 wrist)
        {
            var best = (wrist - slotAnchor.position).magnitude;
            if (hub != null && hub.rightHand != null && hub.rightHand.handJoints != null)
            {
                best = Mathf.Min(best, JointDistance(IndexTipJointIndex));
                best = Mathf.Min(best, JointDistance(MiddleTipJointIndex));
            }
            return best;
        }

        private float JointDistance(int index)
        {
            var joints = hub.rightHand.handJoints;
            if (index >= joints.Count || joints[index] == null)
            {
                return float.MaxValue;
            }
            return (joints[index].position - slotAnchor.position).magnitude;
        }

        private bool TryGetHandPoint(out Vector3 wrist)
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
    }
}
