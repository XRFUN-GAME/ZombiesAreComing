using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    public class MatchResultStoreTests
    {
        private string directory;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
            directory = null;
        }

        [Test]
        public void 结果存档_JSON保留评分输入与战斗明细()
        {
            directory = NewTempDirectory();
            var recorder = new MatchRecorder();
            recorder.RecordKill("Normal", 1);
            recorder.RecordKill("Normal", 1);
            recorder.RecordKill("Brute", 8);
            recorder.RecordArea(18f);
            recorder.RecordArea(12f);
            recorder.RecordCoreParalyze(2);
            recorder.RecordDowned();
            recorder.RecordBuildingDestroyed();
            recorder.RecordGold(GoldLedgerCategory.Kill, 30);
            recorder.RecordGold(GoldLedgerCategory.Build, -300);
            recorder.Finalize(1200f, 0.75f);
            var score = ScoreCalculator.Calculate(recorder.Snapshot());

            var path = MatchResultStore.Save(recorder, score, directory);

            Assert.IsNotNull(path);
            Assert.IsTrue(File.Exists(path));
            var loaded = JsonUtility.FromJson<MatchResultData>(File.ReadAllText(path));
            Assert.AreEqual(MatchResultStore.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.AreEqual(score.Total, loaded.TotalScore);
            Assert.AreEqual(score.Medal.ToString(), loaded.Medal);
            Assert.AreEqual(score.Territory, loaded.Territory);
            Assert.AreEqual(score.Combat, loaded.Combat);
            Assert.AreEqual(score.DefenseCore, loaded.DefenseCore);
            Assert.AreEqual(score.DefensePhases, loaded.DefensePhases);
            Assert.AreEqual(score.Survival, loaded.Survival);
            Assert.AreEqual(1200f, loaded.MatchDurationSeconds);
            Assert.AreEqual(12f, loaded.FinalAreaSqm);
            Assert.AreEqual(18f, loaded.PeakAreaSqm);
            Assert.AreEqual(10, loaded.CombatScore);
            Assert.AreEqual(3, loaded.KillCount);
            Assert.AreEqual(0.75f, loaded.FinalCoreHpRatio);
            Assert.IsTrue(loaded.PhaseParalyzed[2]);
            Assert.AreEqual(1, loaded.CoreParalyzeCount);
            Assert.AreEqual(1, loaded.DownedCount);
            Assert.AreEqual(1, loaded.BuildingsDestroyed);
            Assert.AreEqual(2, loaded.ZombieCombat.Length);
            var normal = Array.Find(loaded.ZombieCombat, entry => entry.ZombieId == "Normal");
            Assert.AreEqual(2, normal.Kills);
            Assert.AreEqual(2, normal.CombatScore);
            var build = Array.Find(loaded.GoldLedger, entry => entry.Category == "Build");
            Assert.AreEqual(-300, build.Delta);

            var recalculated = ScoreCalculator.Calculate(new MatchRecord
            {
                FinalAreaSqm = loaded.FinalAreaSqm,
                CombatScore = loaded.CombatScore,
                CoreHpRatio = loaded.FinalCoreHpRatio,
                PhaseParalyzed = loaded.PhaseParalyzed,
                DownedCount = loaded.DownedCount
            });
            Assert.AreEqual(loaded.TotalScore, recalculated.Total, "存档必须支持按 docs/08 复算总分");
        }

        [Test]
        public void 结果存档_每局生成独立文件()
        {
            directory = NewTempDirectory();
            var recorder = new MatchRecorder();
            recorder.Finalize(1200f, 1f);
            var score = ScoreCalculator.Calculate(recorder.Snapshot());

            var first = MatchResultStore.Save(recorder, score, directory);
            var second = MatchResultStore.Save(recorder, score, directory);

            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(2, Directory.GetFiles(directory, "*.json").Length);
        }

        private static string NewTempDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "MoriGameResultTests-" + Guid.NewGuid().ToString("N"));
        }
    }
}
