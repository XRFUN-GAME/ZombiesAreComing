using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MoriGame
{
    [Serializable]
    public sealed class ZombieCombatResult
    {
        public string ZombieId;
        public int Kills;
        public int CombatScore;
    }

    [Serializable]
    public sealed class GoldLedgerResult
    {
        public string Category;
        public int Delta;
    }

    [Serializable]
    public sealed class MatchResultData
    {
        public int SchemaVersion;
        public string SavedAtUtc;
        public float MatchDurationSeconds;
        public int TotalScore;
        public string Medal;
        public float Territory;
        public float Combat;
        public float DefenseCore;
        public float DefensePhases;
        public float Survival;
        public float FinalAreaSqm;
        public float PeakAreaSqm;
        public int CombatScore;
        public int KillCount;
        public float FinalCoreHpRatio;
        public bool[] PhaseParalyzed;
        public int CoreParalyzeCount;
        public int DownedCount;
        public int BuildingsDestroyed;
        public ZombieCombatResult[] ZombieCombat;
        public GoldLedgerResult[] GoldLedger;
    }

    /// <summary>正式结算本地存档：每局独立 JSON，保留完整评分输入与战斗明细。</summary>
    public static class MatchResultStore
    {
        public const int CurrentSchemaVersion = 1;

        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, "MatchResults");

        public static string Save(MatchRecorder recorder, ScoreResult score)
        {
            return Save(recorder, score, DefaultDirectory);
        }

        public static string Save(MatchRecorder recorder, ScoreResult score, string directory)
        {
            if (recorder == null)
            {
                throw new ArgumentNullException(nameof(recorder));
            }
            if (score == null)
            {
                throw new ArgumentNullException(nameof(score));
            }

            var data = CreateData(recorder, score);
            var fileName = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) +
                           "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
            var path = Path.Combine(directory, fileName);
            var tempPath = path + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, JsonUtility.ToJson(data, true), Encoding.UTF8);
                File.Move(tempPath, path);
                Debug.Log("[MoriGame] 结算结果已保存：" + path);
                return path;
            }
            catch (Exception e)
            {
                Debug.LogError("[MoriGame] 结算结果保存失败：" + path + "； " + e.Message);
                return null;
            }
        }

        private static MatchResultData CreateData(MatchRecorder recorder, ScoreResult score)
        {
            var zombieCombat = new List<ZombieCombatResult>();
            foreach (var pair in recorder.KillsByType)
            {
                zombieCombat.Add(new ZombieCombatResult
                {
                    ZombieId = pair.Key,
                    Kills = pair.Value,
                    CombatScore = recorder.ScoreByType.TryGetValue(pair.Key, out var value) ? value : 0
                });
            }
            var goldLedger = new List<GoldLedgerResult>();
            foreach (GoldLedgerCategory category in Enum.GetValues(typeof(GoldLedgerCategory)))
            {
                goldLedger.Add(new GoldLedgerResult
                {
                    Category = category.ToString(),
                    Delta = recorder.GoldDelta(category)
                });
            }
            return new MatchResultData
            {
                SchemaVersion = CurrentSchemaVersion,
                SavedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                MatchDurationSeconds = recorder.MatchDurationSeconds,
                TotalScore = score.Total,
                Medal = score.Medal.ToString(),
                Territory = score.Territory,
                Combat = score.Combat,
                DefenseCore = score.DefenseCore,
                DefensePhases = score.DefensePhases,
                Survival = score.Survival,
                FinalAreaSqm = recorder.FinalAreaSqm,
                PeakAreaSqm = recorder.PeakAreaSqm,
                CombatScore = recorder.CombatScore,
                KillCount = recorder.KillCount,
                FinalCoreHpRatio = recorder.FinalCoreHpRatio,
                PhaseParalyzed = recorder.Snapshot().PhaseParalyzed,
                CoreParalyzeCount = recorder.CoreParalyzeCount,
                DownedCount = recorder.DownedCount,
                BuildingsDestroyed = recorder.BuildingsDestroyed,
                ZombieCombat = zombieCombat.ToArray(),
                GoldLedger = goldLedger.ToArray()
            };
        }
    }
}
