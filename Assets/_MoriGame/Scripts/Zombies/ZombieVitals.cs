using System;

namespace MoriGame.Zombies
{
    /// <summary>
    /// 僵尸生命体（docs/04，纯 C# 可单测）：承伤、弱点倍率（docs/03 5.1：倍率属僵尸类型）、
    /// 死亡事件（击杀金币/后续战斗积分由订阅方结算，单人独占 docs/04 6.4）。
    /// </summary>
    public class ZombieVitals
    {
        public readonly int MaxHp;
        public readonly int MaxFrontShieldHp;
        public readonly float WeakpointMultiplier;
        public readonly int KillGold;
        public readonly int CombatScore;

        public int Hp { get; private set; }
        public int FrontShieldHp { get; private set; }
        public bool IsAlive => Hp > 0;
        public float HealthBarFraction => IsAlive
            ? (Hp + FrontShieldHp) / (float)(MaxHp + MaxFrontShieldHp)
            : 0f;

        public event Action Died;

        public ZombieVitals(int maxHp, float weakpointMultiplier, int killGold, int combatScore,
            int frontShieldHealth = 0)
        {
            MaxHp = maxHp;
            MaxFrontShieldHp = Math.Max(0, frontShieldHealth);
            WeakpointMultiplier = weakpointMultiplier;
            KillGold = killGold;
            CombatScore = combatScore;
            Hp = maxHp;
            FrontShieldHp = MaxFrontShieldHp;
        }

        public int TakeDamage(int amount, bool hitWeakpoint)
        {
            return TakeDamage(amount, hitWeakpoint, false, false);
        }

        /// <summary>正面盾先吸收非弱点伤害；侧后/腿部与弱点直接伤本体，盾耗尽后溢出伤害进生命。</summary>
        public int TakeDamage(int amount, bool hitWeakpoint, bool frontalHit, bool bypassShield)
        {
            if (!IsAlive || amount <= 0)
            {
                return 0;
            }
            var applied = hitWeakpoint
                ? (int)(amount * WeakpointMultiplier + 0.5f)
                : amount;
            if (frontalHit && !hitWeakpoint && !bypassShield && FrontShieldHp > 0)
            {
                var absorbed = Math.Min(FrontShieldHp, applied);
                FrontShieldHp -= absorbed;
                applied -= absorbed;
            }
            if (applied <= 0)
            {
                return 0;
            }
            Hp -= applied;
            if (Hp <= 0)
            {
                Hp = 0;
                Died?.Invoke();
            }
            return applied;
        }
    }

    /// <summary>疾行者连续命中硬直：短时间内累计 N 次命中触发，超时未续击则重新计数。</summary>
    public class HitStaggerState
    {
        private readonly int hitsRequired;
        private readonly float staggerSeconds;
        private readonly float chainResetSeconds;
        private float sinceLastHit = float.MaxValue;

        public int ConsecutiveHits { get; private set; }
        public float Remaining { get; private set; }

        public HitStaggerState(int hitsToStagger = 3, float durationSeconds = 0.5f,
            float resetSeconds = 1.5f)
        {
            hitsRequired = Math.Max(1, hitsToStagger);
            staggerSeconds = Math.Max(0f, durationSeconds);
            chainResetSeconds = Math.Max(0.01f, resetSeconds);
        }

        public void Tick(float deltaTime)
        {
            Remaining = Math.Max(0f, Remaining - Math.Max(0f, deltaTime));
            if (ConsecutiveHits <= 0)
            {
                return;
            }
            sinceLastHit += Math.Max(0f, deltaTime);
            if (sinceLastHit > chainResetSeconds)
            {
                ConsecutiveHits = 0;
            }
        }

        public bool RegisterHit()
        {
            if (sinceLastHit > chainResetSeconds)
            {
                ConsecutiveHits = 0;
            }
            ConsecutiveHits++;
            sinceLastHit = 0f;
            if (ConsecutiveHits < hitsRequired)
            {
                return false;
            }
            ConsecutiveHits = 0;
            Remaining = staggerSeconds;
            return true;
        }
    }
}
