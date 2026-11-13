#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.XR;

namespace MoriGame.Player
{
    /// <summary>站桩制开发转向：桌面 Q/E，VR 左摇杆左右 45° 快转；不提供任何平移。</summary>
    public class DevLocomotion : MonoBehaviour
    {
        [SerializeField] private float snapTurnDegrees = 45f;

        private Transform head;
        private bool snapReady = true;

        private void Update()
        {
            EnsureHead();
            if (head == null)
            {
                return;
            }
            DesktopTurn();
            VrSnapTurn();
        }

        private void EnsureHead()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    head = cam.transform;
                }
            }
        }

        private void DesktopTurn()
        {
            var turn = (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
            if (turn != 0)
            {
                transform.Rotate(0f, turn * 90f * Time.deltaTime, 0f);
            }
        }

        private void VrSnapTurn()
        {
            if (!TryGetStick(XRNode.LeftHand, out var stick))
            {
                return;
            }
            if (Mathf.Abs(stick.x) > 0.6f && snapReady)
            {
                snapReady = false;
                var headPivot = new Vector3(head.position.x, transform.position.y, head.position.z);
                transform.RotateAround(headPivot, Vector3.up,
                    stick.x > 0f ? snapTurnDegrees : -snapTurnDegrees);
            }
            else if (Mathf.Abs(stick.x) < 0.3f)
            {
                snapReady = true;
            }
        }

        private static bool TryGetStick(XRNode hand, out Vector2 stick)
        {
            stick = Vector2.zero;
            var device = InputDevices.GetDeviceAtXRNode(hand);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick);
        }
    }
}
#endif
