using System;
using UnityEngine;

namespace MoriGame.Devices
{
    public enum CoreState : byte
    {
        Normal = 0,
        Disabled = 1
    }

    /// <summary>
    /// 中控状态机（docs/07 4.5，纯 C# 可单测）：
    /// Normal →（生命归 0）→ Disabled(10s：免疫伤害、商店停用、地盘与信标保持) → 自动恢复 40%（480）→ Normal。
    /// 瘫痪触发时只发事件，金币罚金由订阅方处理。
    /// </summary>
    public class CoreDeviceState
    {
        private const float TimingEpsilon = 0.0005f; // 浮点累加边界容差（0.5ms）

        public readonly int MaxHp;
        public readonly float ParalyzeSeconds;
        public readonly int RecoverHp;

        public CoreState State { get; private set; } = CoreState.Normal;
        public int Hp { get; private set; }
        public float ParalyzeRemainingSeconds { get; private set; }

        /// <summary>瘫痪触发（扣金币罚金由订阅方处理）</summary>
        public event Action Disabled;

        /// <summary>瘫痪结束自动恢复</summary>
        public event Action Recovered;

        public CoreDeviceState(int maxHp, float paralyzeSeconds, int recoverHp)
        {
            MaxHp = maxHp;
            ParalyzeSeconds = paralyzeSeconds;
            RecoverHp = recoverHp;
            Hp = maxHp;
        }

        /// <summary>瘫痪期间免疫（"期间核心不再受伤"）。返回是否实际扣血。</summary>
        public bool TakeDamage(int amount)
        {
            if (amount <= 0 || State != CoreState.Normal)
            {
                return false;
            }
            Hp -= amount;
            if (Hp <= 0)
            {
                Hp = 0;
                State = CoreState.Disabled;
                ParalyzeRemainingSeconds = ParalyzeSeconds;
                Disabled?.Invoke();
            }
            return true;
        }

        /// <summary>维修回血（仅正常态；钳到 MaxHp）。返回是否实际回血（2026-11-11）。</summary>
        public bool Heal(int amount)
        {
            if (amount <= 0 || State != CoreState.Normal || Hp >= MaxHp)
            {
                return false;
            }
            Hp = Mathf.Min(MaxHp, Hp + amount);
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (State != CoreState.Disabled)
            {
                return;
            }
            ParalyzeRemainingSeconds -= deltaSeconds;
            if (ParalyzeRemainingSeconds <= TimingEpsilon)
            {
                State = CoreState.Normal;
                Hp = RecoverHp;
                ParalyzeRemainingSeconds = 0f;
                Recovered?.Invoke();
            }
        }
    }
}
