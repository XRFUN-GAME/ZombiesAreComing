using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M2-T6 对局事件采集器：倒地/瘫痪标记沿用 MatchRunStats 语义（2026-11-06 升级为 MatchRecorder）；
    /// 新增击杀分类积分、金币收支、面积峰值、终局冻结的采集断言。</summary>
    public class MatchRecorderTests
    {
        [Test]
        public void 倒地次数逐次累计()
        {
            var stats = new MatchRecorder();
            stats.RecordDowned();
            stats.RecordDowned();
            Assert.AreEqual(2, stats.DownedCount);
        }

        [Test]
        public void 阶段瘫痪标记只记首次_总次数照算()
        {
            var stats = new MatchRecorder();
            stats.RecordCoreParalyze(1);
            stats.RecordCoreParalyze(1);
            Assert.AreEqual(2, stats.CoreParalyzeCount);
            Assert.IsTrue(stats.IsCoreParalyzedInPhase(1));
            Assert.IsFalse(stats.IsCoreParalyzedInPhase(0));
        }

        [Test]
        public void 准备期与越界阶段不写五阶段标记()
        {
            var stats = new MatchRecorder();
            stats.RecordCoreParalyze(-1);
            stats.RecordCoreParalyze(5);
            Assert.AreEqual(2, stats.CoreParalyzeCount);
            for (var i = 0; i < 5; i++)
            {
                Assert.IsFalse(stats.IsCoreParalyzedInPhase(i));
            }
        }

        [Test]
        public void 击杀按类型累计_总数与积分同步()
        {
            var r = new MatchRecorder();
            r.RecordKill("Normal", 1);
            r.RecordKill("Normal", 1);
            r.RecordKill("Brute", 8);
            Assert.AreEqual(3, r.KillCount);
            Assert.AreEqual(10, r.CombatScore);
            Assert.AreEqual(2, r.KillsByType["Normal"]);
            Assert.AreEqual(2, r.ScoreByType["Normal"]);
            Assert.AreEqual(1, r.KillsByType["Brute"]);
            Assert.AreEqual(8, r.ScoreByType["Brute"]);
        }

        [Test]
        public void 面积采样记录终局值与历史峰值()
        {
            var r = new MatchRecorder();
            r.RecordArea(6.25f);
            r.RecordArea(12.5f);
            r.RecordArea(9f); // 拆建筑缩地
            Assert.AreEqual(12.5f, r.PeakAreaSqm);
            Assert.AreEqual(9f, r.FinalAreaSqm);
        }

        [Test]
        public void 金币流水按分类累计()
        {
            var r = new MatchRecorder();
            r.RecordGold(GoldLedgerCategory.Kill, 40);
            r.RecordGold(GoldLedgerCategory.Kill, 40);
            r.RecordGold(GoldLedgerCategory.PhaseReward, 300);
            r.RecordGold(GoldLedgerCategory.Build, -220);
            Assert.AreEqual(80, r.GoldDelta(GoldLedgerCategory.Kill));
            Assert.AreEqual(300, r.GoldDelta(GoldLedgerCategory.PhaseReward));
            Assert.AreEqual(-220, r.GoldDelta(GoldLedgerCategory.Build));
            Assert.AreEqual(0, r.GoldDelta(GoldLedgerCategory.Supply));
        }

        [Test]
        public void 终局冻结后所有采集拒收()
        {
            var r = new MatchRecorder();
            r.RecordKill("Normal", 1);
            r.RecordArea(6.25f);
            r.Finalize(1200f, 0.5f);
            r.RecordKill("Normal", 1);
            r.RecordArea(99f);
            r.RecordGold(GoldLedgerCategory.Kill, 100);
            r.RecordDowned();
            Assert.AreEqual(1, r.KillCount);
            Assert.AreEqual(6.25f, r.FinalAreaSqm, 0.0001f); // 冻结前采样保留，冻结后 99 被拒收
            Assert.AreEqual(0, r.GoldDelta(GoldLedgerCategory.Kill));
            Assert.AreEqual(0, r.DownedCount);
            Assert.AreEqual(1200f, r.MatchDurationSeconds);
            Assert.AreEqual(0.5f, r.FinalCoreHpRatio);
            Assert.IsTrue(r.Finalized);
        }

        [Test]
        public void 快照携带评分输入且阶段标记隔离()
        {
            var r = new MatchRecorder();
            r.RecordKill("Normal", 1);
            r.RecordArea(30f);
            r.RecordCoreParalyze(2);
            r.RecordDowned();
            r.Finalize(600f, 0.4f);
            var snapshot = r.Snapshot();
            Assert.AreEqual(30f, snapshot.FinalAreaSqm);
            Assert.AreEqual(1, snapshot.CombatScore);
            Assert.AreEqual(0.4f, snapshot.CoreHpRatio);
            Assert.AreEqual(1, snapshot.DownedCount);
            Assert.IsFalse(snapshot.PhaseParalyzed[1]);
            Assert.IsTrue(snapshot.PhaseParalyzed[2]);
            snapshot.PhaseParalyzed[3] = true; // 改快照不影响采集器
            Assert.IsFalse(r.IsCoreParalyzedInPhase(3));
        }
    }
}
