namespace MoriGame.Combat
{
    /// <summary>
    /// 拍击弹匣换弹检测（docs/07 2026-11-02 定案，纯 C# 可单测）：
    /// 另一只手手腕进入弹匣 0.15 米内且瞬时速度超阈值 = "拍击"（速度区分停留/拍击），
    /// 触发后冷却防连发；离开范围才允许再次触发。
    /// </summary>
    public class MagSlapDetector
    {
        private readonly float speedThreshold;
        private readonly float cooldownSeconds;

        private bool wasInside;
        private float cooldownRemaining;

        public MagSlapDetector(float speedThresholdMps = 1.2f, float cooldownSeconds = 0.8f)
        {
            speedThreshold = speedThresholdMps;
            this.cooldownSeconds = cooldownSeconds;
        }

        /// <summary>每帧喂入手腕与弹匣的距离状态与速度；命中拍击边沿返回 true（单帧）</summary>
        public bool Update(bool withinRadius, float wristSpeedMps, float deltaSeconds)
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining -= deltaSeconds;
            }
            var triggered = false;
            if (withinRadius && !wasInside && wristSpeedMps >= speedThreshold && cooldownRemaining <= 0f)
            {
                triggered = true;
                cooldownRemaining = cooldownSeconds;
            }
            wasInside = withinRadius;
            return triggered;
        }
    }
}
