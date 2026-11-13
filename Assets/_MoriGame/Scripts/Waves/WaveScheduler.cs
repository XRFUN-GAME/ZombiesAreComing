using System;
using System.Collections.Generic;

namespace MoriGame.Waves
{
    /// <summary>阶段数据快照（由 MatchPhaseConfig 资产转录，纯逻辑不依赖 Unity）</summary>
    public class PhaseSpec
    {
        public string phaseId;
        public string displayName;
        public float startSeconds;
        public float endSeconds;
        public int threatBudgetPerInterval;
        public int arrivalGoldReward;
        public int initialAliveCount; // 阶段入场存活下限（2026-11-09 阶梯：5/10/13/16/20）
        public List<float> typeWeights = new List<float>(); // 类型占比权重（与 allowedZombieIds 对齐；空=等概率）
        public int waveSizeCap;       // 拍波封顶（2026-11-10 三次调压：前三阶段 15、后两 20）
        public int aliveCap;          // 本阶段同屏上限（0=全局默认；2026-11-10：P1—P4=20、P5=25）
        public List<string> allowedZombieIds = new List<string>();
    }

    /// <summary>特殊事件快照（docs/01：07:12 督战者等，纯时间点分发）</summary>
    public struct SpecialEventSpec
    {
        public float atSeconds;
        public string eventId;
        public string parameter;
    }

    /// <summary>
    /// 波次调度器纯逻辑（docs/01 阶段表 + docs/04 波次预算）：时钟由外部 Tick 推进。
    /// 预算以威胁点计，阶段入口立即发放第一批、之后每 interval 一批；未消耗预算累积，
    /// 最多积 carryRounds 轮；阶段跨越触发 PhaseEntered（含到达奖励，对局结束时点的奖励照发）；
    /// 到 matchEndSeconds 触发 MatchEnded，之后不再发放也不可消耗。
    /// 特殊事件（07:12 督战者等）按时间点恰好触发一次。
    /// </summary>
    public class WaveScheduler
    {
        private readonly List<PhaseSpec> phases;
        private readonly List<SpecialEventSpec> specialEvents;
        private readonly float budgetInterval;
        private readonly float matchEndSeconds;
        private readonly int maxCarryRounds;
        private readonly float slowWindowSeconds;

        private bool started;
        private float nextBudgetSeconds;
        private int nextSpecialEventIndex;

        public float ElapsedSeconds { get; private set; }
        public int PhaseIndex { get; private set; } = -1;
        public int CarriedBudget { get; private set; }
        public bool Ended { get; private set; }
        public PhaseSpec CurrentPhase => PhaseIndex >= 0 && PhaseIndex < phases.Count ? phases[PhaseIndex] : null;
        public PhaseSpec NextPhase => PhaseIndex + 1 < phases.Count ? phases[PhaseIndex + 1] : null;
        public float RemainingSeconds => Math.Max(0f, matchEndSeconds - ElapsedSeconds);

        /// <summary>阶段切换前 slowWindowSeconds 秒内生成速率打折（2026-11-04 T1 定案：切换前 10 秒 -50%，给玩家收线时间）</summary>
        public bool InSlowWindow
        {
            get
            {
                var next = NextPhase;
                if (next == null)
                {
                    return false;
                }
                var untilSwitch = next.startSeconds - ElapsedSeconds;
                return untilSwitch > 0f && untilSwitch <= slowWindowSeconds;
            }
        }

        public event Action<PhaseSpec> PhaseEntered;
        public event Action MatchEnded;
        public event Action<SpecialEventSpec> SpecialEventReached;

        public WaveScheduler(List<PhaseSpec> phases, float budgetIntervalSeconds, float matchEndSeconds,
            int maxCarryRounds, float slowWindowSeconds, List<SpecialEventSpec> specialEvents = null)
        {
            this.phases = phases;
            budgetInterval = budgetIntervalSeconds;
            this.matchEndSeconds = matchEndSeconds;
            this.maxCarryRounds = maxCarryRounds;
            this.slowWindowSeconds = slowWindowSeconds;
            this.specialEvents = specialEvents ?? new List<SpecialEventSpec>();
            this.specialEvents.Sort((a, b) => a.atSeconds.CompareTo(b.atSeconds));
        }

        public void Tick(float deltaTime)
        {
            if (!started)
            {
                started = true;
                EnterPhase(0);
            }
            if (Ended)
            {
                return;
            }
            ElapsedSeconds += deltaTime;

            // 阶段跨越先于结束判定：结束时刻若同时是阶段入口，到达奖励仍要发出
            while (PhaseIndex + 1 < phases.Count && ElapsedSeconds >= phases[PhaseIndex + 1].startSeconds)
            {
                EnterPhase(PhaseIndex + 1);
            }

            // 特殊事件按时间点恰好触发一次（先于结束判定：整点事件不丢）
            while (nextSpecialEventIndex < specialEvents.Count &&
                   ElapsedSeconds >= specialEvents[nextSpecialEventIndex].atSeconds)
            {
                SpecialEventReached?.Invoke(specialEvents[nextSpecialEventIndex]);
                nextSpecialEventIndex++;
            }

            if (ElapsedSeconds >= matchEndSeconds)
            {
                ElapsedSeconds = matchEndSeconds;
                Ended = true;
                MatchEnded?.Invoke();
                return;
            }

            var phase = CurrentPhase;
            var cap = phase.threatBudgetPerInterval * (1 + maxCarryRounds);
            while (nextBudgetSeconds <= ElapsedSeconds)
            {
                CarriedBudget = Math.Min(CarriedBudget + phase.threatBudgetPerInterval, cap);
                nextBudgetSeconds += budgetInterval;
            }
        }

        /// <summary>消耗威胁点生成僵尸；预算不足或对局已结束返回 false。</summary>
        public bool TrySpend(int threatPoints)
        {
            if (Ended || threatPoints <= 0 || CarriedBudget < threatPoints)
            {
                return false;
            }
            CarriedBudget -= threatPoints;
            return true;
        }

        private void EnterPhase(int index)
        {
            PhaseIndex = index;
            var phase = phases[index];
            // 阶段入口立即发第一批预算（准备期为 0，自然不发）
            CarriedBudget = Math.Min(
                CarriedBudget + phase.threatBudgetPerInterval,
                phase.threatBudgetPerInterval * (1 + maxCarryRounds));
            nextBudgetSeconds = phase.startSeconds + budgetInterval;
            PhaseEntered?.Invoke(phase);
        }
    }
}
