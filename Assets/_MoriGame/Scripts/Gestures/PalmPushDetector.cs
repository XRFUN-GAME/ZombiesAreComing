namespace MoriGame.Gestures
{
    /// <summary>
    /// 张掌前推检测（纯逻辑可单测）：手指伸直且沿注视方向速度超阈值，持续 holdSeconds 触发一次，
    /// 冷却 cooldownSeconds。Triggered 触发帧为 true，多订阅方可重复读取。
    /// </summary>
    public class PalmPushDetector
    {
        private readonly float speedThreshold;
        private readonly float holdSeconds;
        private readonly float cooldownSeconds;
        private float holdTimer;
        private float cooldownTimer;
        private bool triggered;

        public bool Triggered => triggered;

        public PalmPushDetector(float speedThreshold, float holdSeconds, float cooldownSeconds)
        {
            this.speedThreshold = speedThreshold;
            this.holdSeconds = holdSeconds;
            this.cooldownSeconds = cooldownSeconds;
        }

        public void Update(bool extended, float forwardSpeed, float deltaSeconds)
        {
            triggered = false;
            if (cooldownTimer > 0f)
            {
                cooldownTimer -= deltaSeconds;
            }
            if (extended && forwardSpeed > speedThreshold && cooldownTimer <= 0f)
            {
                holdTimer += deltaSeconds;
                if (holdTimer >= holdSeconds)
                {
                    triggered = true;
                    holdTimer = 0f;
                    cooldownTimer = cooldownSeconds;
                }
            }
            else
            {
                holdTimer = 0f;
            }
        }
    }
}
