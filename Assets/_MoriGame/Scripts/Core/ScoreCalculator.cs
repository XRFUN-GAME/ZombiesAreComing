using System;

namespace MoriGame
{
    public enum Medal : byte
    {
        None = 0,
        Bronze = 1,
        Silver = 2,
        Gold = 3
    }

    /// <summary>评分结果（各项保留一位小数，总分四舍五入取整——docs/08 口径）。</summary>
    public sealed class ScoreResult
    {
        public float Territory;     // /400
        public float Combat;        // /300
        public float DefenseCore;   // 核心完整度 /100
        public float DefensePhases; // Σ 阶段分 /100
        public float Defense => DefenseCore + DefensePhases; // /200
        public float Survival;      // /100
        public int Total;
        public Medal Medal;
    }

    /// <summary>
    /// 终局四项评分（docs/08 终局综合评分，总分 1000，纯函数可复算）：
    /// 地盘 400×min(面积/24㎡,1)；战斗 300×min(积分/240,1)；
    /// 防守 100×核心生命比例 + Σ 阶段分（每阶段 20，该阶段瘫痪过即不得——bool 置位天然不重复扣）；
    /// 生存 max(100-20×倒地次数,0)。奖牌线：铜 400 / 银 600 / 金 800。
    /// </summary>
    public static class ScoreCalculator
    {
        public const float TerritoryFullAreaSqm = 24f;
        public const int TerritoryMaxScore = 400;
        public const int CombatFullScore = 240;
        public const int CombatMaxScore = 300;
        public const int DefenseCoreMaxScore = 100;
        public const int PhaseScoreValue = 20;
        public const int SurvivalMaxScore = 100;
        public const int DownedScorePenalty = 20;
        public const int BronzeScore = 400;
        public const int SilverScore = 600;
        public const int GoldScore = 800;

        public static ScoreResult Calculate(MatchRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            var result = new ScoreResult
            {
                Territory = Round1(TerritoryMaxScore * Math.Min(record.FinalAreaSqm / TerritoryFullAreaSqm, 1.0)),
                Combat = Round1(CombatMaxScore * Math.Min(record.CombatScore / (double)CombatFullScore, 1.0)),
                Survival = Math.Max(SurvivalMaxScore - DownedScorePenalty * record.DownedCount, 0)
            };
            var phases = 0;
            for (var i = 0; i < MatchRecorder.PhaseCount; i++)
            {
                if (record.PhaseParalyzed == null || i >= record.PhaseParalyzed.Length ||
                    !record.PhaseParalyzed[i])
                {
                    phases += PhaseScoreValue;
                }
            }
            result.DefensePhases = phases;
            result.DefenseCore = Round1(DefenseCoreMaxScore * Clamp01(record.CoreHpRatio));
            var sum = result.Territory + result.Combat + result.DefenseCore + result.DefensePhases + result.Survival;
            result.Total = (int)Math.Round(sum, 0, MidpointRounding.AwayFromZero);
            result.Medal = result.Total >= GoldScore ? Medal.Gold
                : result.Total >= SilverScore ? Medal.Silver
                : result.Total >= BronzeScore ? Medal.Bronze
                : Medal.None;
            return result;
        }

        private static double Clamp01(double value)
        {
            return value < 0 ? 0 : value > 1 ? 1 : value;
        }

        private static float Round1(double value)
        {
            return (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);
        }
    }
}
