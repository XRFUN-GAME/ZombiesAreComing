using UnityEngine;

namespace MoriGame.Gestures
{
    /// <summary>
    /// 侧推快速转向检测器（2026-11-05 站桩工作台制，纯逻辑可单测）：
    /// 张掌 + 手速在"触发方向"分量 ≥ 阈值持续 holdSeconds → 触发一次转向，冷却 cooldownSeconds。
    /// 触发方向由调用方按手别取分量（左手传 dot(手速, 玩家左向)，右手传 dot(手速, 玩家右向)）——
    /// detector 只看带符号分量是否过阈，方向语义在调用方。
    /// 转向角度/叠加规则在消费方（GestureLocomotion：单次 45°，冷却 0.5s 可连推叠加到 90°+）。
    /// </summary>
    public sealed class SnapTurnDetector
    {
        private const float TimingEpsilon = 0.0005f;

        private readonly float speedMps;
        private readonly float holdSeconds;
        private readonly float cooldownSeconds;

        private float held;
        private float cooldown;

        public SnapTurnDetector(float speedMps, float holdSeconds, float cooldownSeconds)
        {
            this.speedMps = speedMps;
            this.holdSeconds = holdSeconds;
            this.cooldownSeconds = cooldownSeconds;
        }

        /// <summary>返回本帧是否触发一次转向；palmExtended=false 或分量不足时计时清零</summary>
        public bool Update(bool palmExtended, float signedLateralSpeed, float deltaSeconds)
        {
            cooldown = Mathf.Max(0f, cooldown - deltaSeconds);
            if (!palmExtended || signedLateralSpeed < speedMps)
            {
                held = 0f;
                return false;
            }
            held += deltaSeconds;
            if (held >= holdSeconds - TimingEpsilon && cooldown <= 0f)
            {
                held = 0f;
                cooldown = cooldownSeconds;
                return true;
            }
            return false;
        }
    }
}
