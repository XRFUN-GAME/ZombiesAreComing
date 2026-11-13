using System;

namespace MoriGame.Player
{
    public enum VitalsState : byte
    {
        Alive = 0,
        Downed = 1,
        RespawnProtection = 2
    }

    /// <summary>
    /// 玩家生命状态机（docs/07 4.1，纯 C# 可单测）：
    /// Alive → Downed(8s，期间免疫) → RespawnProtection(3s，无敌) → Alive（满血）。
    /// 重生时发事件恢复工作台固定位姿；主动射击提前结束保护由 WeaponController 触发。
    /// </summary>
    public class VitalsStateMachine
    {
        private const float TimingEpsilon = 0.0005f; // 浮点累加边界容差（0.5ms）

        public readonly int MaxHp;
        public readonly float DownedSeconds;
        public readonly float RespawnProtectionSeconds;

        public VitalsState State { get; private set; } = VitalsState.Alive;
        public int Hp { get; private set; }
        public float StateElapsedSeconds { get; private set; }

        /// <summary>倒地触发（金币罚金由订阅方处理）</summary>
        public event Action Downed;

        /// <summary>重生瞬间触发（传送由订阅方处理），随后进入重生保护</summary>
        public event Action Respawned;

        public VitalsStateMachine(int maxHp, float downedSeconds, float respawnProtectionSeconds)
        {
            MaxHp = maxHp;
            DownedSeconds = downedSeconds;
            RespawnProtectionSeconds = respawnProtectionSeconds;
            Hp = maxHp;
        }

        /// <summary>受击；倒地/重生保护期间免疫。返回是否实际扣血。</summary>
        public bool TakeDamage(int amount)
        {
            if (amount <= 0 || State != VitalsState.Alive)
            {
                return false;
            }
            Hp -= amount;
            if (Hp <= 0)
            {
                Hp = 0;
                State = VitalsState.Downed;
                StateElapsedSeconds = 0f;
                Downed?.Invoke();
            }
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (State == VitalsState.Alive)
            {
                return;
            }
            StateElapsedSeconds += deltaSeconds;
            switch (State)
            {
                case VitalsState.Downed:
                    if (StateElapsedSeconds >= DownedSeconds - TimingEpsilon)
                    {
                        // 起身即满血（docs/07 未定义重生血量，保护期内需正常活动）
                        Hp = MaxHp;
                        State = VitalsState.RespawnProtection;
                        StateElapsedSeconds = 0f;
                        Respawned?.Invoke();
                    }
                    break;
                case VitalsState.RespawnProtection:
                    if (StateElapsedSeconds >= RespawnProtectionSeconds - TimingEpsilon)
                    {
                        State = VitalsState.Alive;
                        StateElapsedSeconds = 0f;
                    }
                    break;
            }
        }

        /// <summary>主动射击提前结束保护（docs/07）</summary>
        public void BreakProtection()
        {
            if (State == VitalsState.RespawnProtection)
            {
                State = VitalsState.Alive;
                StateElapsedSeconds = 0f;
            }
        }
    }
}
