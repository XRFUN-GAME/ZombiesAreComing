namespace MoriGame.Combat
{
    /// <summary>
    /// 武器收出枪状态机（docs/07 2026-11-02 抬手开火，纯 C# 可单测）：
    /// Stowed --手腕进胸前有效区 0.3s--→ Deployed；Deployed --手腕低于腰部持续 1s--→ Stowed。
    /// 离开有效区但未垂手（如平举指向）保持出枪；开火门控由调用方另查 InZone。
    /// </summary>
    public class WeaponDeployState
    {
        private const float TimingEpsilon = 0.0005f;

        public readonly float RaiseSeconds;
        public readonly float StowSeconds;

        public bool Deployed { get; private set; }

        private float raiseElapsed;
        private float stowElapsed;

        public WeaponDeployState(float raiseSeconds = 0.3f, float stowSeconds = 1f)
        {
            RaiseSeconds = raiseSeconds;
            StowSeconds = stowSeconds;
        }

        public void Tick(float deltaSeconds, bool inZone, bool belowWaist)
        {
            if (!Deployed)
            {
                stowElapsed = 0f;
                raiseElapsed = inZone ? raiseElapsed + deltaSeconds : 0f;
                if (raiseElapsed >= RaiseSeconds - TimingEpsilon)
                {
                    Deployed = true;
                    raiseElapsed = 0f;
                }
                return;
            }
            raiseElapsed = 0f;
            stowElapsed = belowWaist ? stowElapsed + deltaSeconds : 0f;
            if (stowElapsed >= StowSeconds - TimingEpsilon)
            {
                Deployed = false;
                stowElapsed = 0f;
            }
        }

        /// <summary>强制出枪（2026-11-05 站桩制"抓起枪=立即装备"，跳过 0.3s 抬手计时）</summary>
        public void ForceDeploy()
        {
            Deployed = true;
            raiseElapsed = 0f;
            stowElapsed = 0f;
        }

        /// <summary>强制收枪（站桩制枪套锁/插槽归位）</summary>
        public void ForceStow()
        {
            Deployed = false;
            raiseElapsed = 0f;
            stowElapsed = 0f;
        }
    }
}
