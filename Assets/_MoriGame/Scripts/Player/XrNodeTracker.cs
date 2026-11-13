using UnityEngine;
using UnityEngine.XR;

namespace MoriGame.Player
{
    /// <summary>
    /// 把 XRNode（头 / 左手 / 右手）追踪位姿应用到本 Transform（tracking space 本地位姿）。
    /// 直接轮询 UnityEngine.XR，不依赖 XRI，兼容任意 loader（含 PXR_Loader）。
    /// 渲染前（onBeforeRender）与 LateUpdate 双通道更新，降低头动锯齿。
    /// </summary>
    public class XrNodeTracker : MonoBehaviour
    {
        public XRNode node = XRNode.Head;

        private int lastValidFrame = -9999;
        private bool visualsHidden;

        private void OnEnable()
        {
            Application.onBeforeRender += ApplyPose;
            ApplyPose();
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyPose;
        }

        private void LateUpdate()
        {
            ApplyPose();
        }

        private void ApplyPose()
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
            {
                // 纯手势模式下控制器无效：约半秒后隐藏手柄锚点视觉，避免悬在视野中央
                if (!visualsHidden && Time.frameCount - lastValidFrame > 30)
                {
                    visualsHidden = true;
                    SetVisuals(false);
                }
                return;
            }
            lastValidFrame = Time.frameCount;
            if (visualsHidden)
            {
                visualsHidden = false;
                SetVisuals(true);
            }
            if (device.TryGetFeatureValue(CommonUsages.devicePosition, out var position))
            {
                transform.localPosition = position;
            }
            if (device.TryGetFeatureValue(CommonUsages.deviceRotation, out var rotation))
            {
                transform.localRotation = rotation;
            }
        }

        private void SetVisuals(bool on)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = on;
            }
        }
    }
}
