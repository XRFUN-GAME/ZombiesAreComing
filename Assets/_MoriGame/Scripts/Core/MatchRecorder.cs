using System.Collections.Generic;

namespace MoriGame
{
    /// <summary>金币流水分类（docs/08 结算展示：收入/支出分类）。负 delta=支出、正 delta=收入。</summary>
    public enum GoldLedgerCategory : byte
    {
        Kill = 0,           // 收入：击杀金币
        PhaseReward = 1,    // 收入：阶段到达奖励
        Supply = 2,         // 收入：补给箱
        Refund = 3,         // 收入：建筑退款（归位/扔掉/换件/取消回滚）
        Build = 4,          // 支出：建筑购买（换件时与退款同帧发生，各自记账）
        DownedPenalty = 5,  // 支出：倒地罚金
        ParalyzePenalty = 6 // 支出：瘫痪罚金
    }

    /// <summary>终局评分快照（docs/08 ScoreCalculator 输入，可复算验收载体）。</summary>
    public sealed class MatchRecord
    {
        public float FinalAreaSqm;
        public int CombatScore;
        public float CoreHpRatio;
        public bool[] PhaseParalyzed = new bool[5];
        public int DownedCount;
    }

    /// <summary>
    /// 对局事件采集器（M2-T6，docs/08）：击杀数与按类型战斗积分、峰值/终局地盘面积、终局核心生命比例、
    /// 五阶段瘫痪标记、倒地次数、建筑摧毁数、金币收支分类、对局时长。
    /// 挂 MatchBootstrap 生命周期（Awake new 实例=随局重置）；Finalize 后冻结，防止终局后脏写。
    /// 取代 MatchRunStats（倒地/瘫痪 API 原样保留，bool[5] 置位天然"不重复扣"）。
    /// </summary>
    public sealed class MatchRecorder
    {
        public const int PhaseCount = 5;

        private readonly bool[] coreParalyzedByPhase = new bool[PhaseCount];
        private readonly Dictionary<string, int> killsByType = new Dictionary<string, int>();
        private readonly Dictionary<string, int> scoreByType = new Dictionary<string, int>();
        private readonly Dictionary<GoldLedgerCategory, int> goldLedger = new Dictionary<GoldLedgerCategory, int>();

        public int KillCount { get; private set; }
        public int CombatScore { get; private set; }
        public int DownedCount { get; private set; }
        public int CoreParalyzeCount { get; private set; }
        public int BuildingsDestroyed { get; private set; }
        public float PeakAreaSqm { get; private set; }
        public float FinalAreaSqm { get; private set; }
        public float MatchDurationSeconds { get; private set; }
        public float FinalCoreHpRatio { get; private set; }
        public bool Finalized { get; private set; }

        public IReadOnlyDictionary<string, int> KillsByType => killsByType;
        public IReadOnlyDictionary<string, int> ScoreByType => scoreByType;
        public IReadOnlyDictionary<GoldLedgerCategory, int> GoldLedger => goldLedger;

        /// <summary>击杀：累计总数 + 按类型击杀数/战斗积分（积分表 docs/04，读 ZombieConfig.combatScore）</summary>
        public void RecordKill(string zombieId, int combatScore)
        {
            if (Finalized || string.IsNullOrEmpty(zombieId))
            {
                return;
            }
            KillCount++;
            CombatScore += combatScore;
            killsByType.TryGetValue(zombieId, out var kills);
            killsByType[zombieId] = kills + 1;
            scoreByType.TryGetValue(zombieId, out var score);
            scoreByType[zombieId] = score + combatScore;
        }

        /// <summary>地盘面积采样（Bootstrap.Update 每帧）：更新终局值与历史峰值</summary>
        public void RecordArea(float areaSqm)
        {
            if (Finalized)
            {
                return;
            }
            if (areaSqm > PeakAreaSqm)
            {
                PeakAreaSqm = areaSqm;
            }
            FinalAreaSqm = areaSqm;
        }

        public void RecordBuildingDestroyed()
        {
            if (!Finalized)
            {
                BuildingsDestroyed++;
            }
        }

        public void RecordDowned()
        {
            if (!Finalized)
            {
                DownedCount++;
            }
        }

        /// <summary>瘫痪标记：bool[5] 置位——同阶段反复瘫痪只占一次（阶段分不重复扣的实现载体）</summary>
        public void RecordCoreParalyze(int gameplayPhaseIndex)
        {
            if (Finalized)
            {
                return;
            }
            CoreParalyzeCount++;
            if (gameplayPhaseIndex >= 0 && gameplayPhaseIndex < PhaseCount)
            {
                coreParalyzedByPhase[gameplayPhaseIndex] = true;
            }
        }

        public bool IsCoreParalyzedInPhase(int gameplayPhaseIndex)
        {
            return gameplayPhaseIndex >= 0 && gameplayPhaseIndex < PhaseCount &&
                   coreParalyzedByPhase[gameplayPhaseIndex];
        }

        /// <summary>金币流水：正=收入、负=支出（金额即实扣实收数额，由调用方在钱包变动点旁路记录）</summary>
        public void RecordGold(GoldLedgerCategory category, int delta)
        {
            if (Finalized || delta == 0)
            {
                return;
            }
            goldLedger.TryGetValue(category, out var current);
            goldLedger[category] = current + delta;
        }

        public int GoldDelta(GoldLedgerCategory category)
        {
            return goldLedger.TryGetValue(category, out var value) ? value : 0;
        }

        /// <summary>终局冻结：时长与核心生命比例在终局点一次性写入，之后所有 Record 拒收</summary>
        public void Finalize(float durationSeconds, float coreHpRatio)
        {
            if (Finalized)
            {
                return;
            }
            Finalized = true;
            MatchDurationSeconds = durationSeconds;
            FinalCoreHpRatio = coreHpRatio;
        }

        public MatchRecord Snapshot()
        {
            return new MatchRecord
            {
                FinalAreaSqm = FinalAreaSqm,
                CombatScore = CombatScore,
                CoreHpRatio = FinalCoreHpRatio,
                PhaseParalyzed = (bool[])coreParalyzedByPhase.Clone(),
                DownedCount = DownedCount
            };
        }
    }
}
