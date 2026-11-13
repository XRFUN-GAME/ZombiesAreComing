using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M2-T6 终局评分断言（docs/08 口径，可复算验收硬指标）：
    /// 满额局=1000 / 全零局=0 / 单阶段瘫痪扣 20 且不重复扣 / 奖牌边界 399/400/599/600/799/800 / 面积封顶不溢出。</summary>
    public class ScoreCalculatorTests
    {
        [Test]
        public void 满额局_总分1000()
        {
            var result = ScoreCalculator.Calculate(MakeRecord(
                area: 24f, score: 240, ratio: 1f, phases: NoneParalyzed(), downed: 0));
            Assert.AreEqual(400f, result.Territory);
            Assert.AreEqual(300f, result.Combat);
            Assert.AreEqual(200f, result.Defense);
            Assert.AreEqual(100f, result.Survival);
            Assert.AreEqual(1000, result.Total);
            Assert.AreEqual(Medal.Gold, result.Medal);
        }

        [Test]
        public void 全零局_总分0()
        {
            var result = ScoreCalculator.Calculate(MakeRecord(
                area: 0f, score: 0, ratio: 0f, phases: AllParalyzed(), downed: 6));
            Assert.AreEqual(0f, result.Territory);
            Assert.AreEqual(0f, result.Combat);
            Assert.AreEqual(0f, result.Defense);
            Assert.AreEqual(0f, result.Survival); // 100-120 → clamp 0
            Assert.AreEqual(0, result.Total);
            Assert.AreEqual(Medal.None, result.Medal);
        }

        [Test]
        public void 单阶段瘫痪_只扣该阶段20分_重复瘫痪不重复扣()
        {
            // 同阶段瘫痪两次：bool 置位只占一次，阶段分 4×20=80
            var recorder = new MatchRecorder();
            recorder.RecordCoreParalyze(1);
            recorder.RecordCoreParalyze(1);
            var result = ScoreCalculator.Calculate(MakeRecord(
                area: 0f, score: 0, ratio: 1f, phases: recorder.Snapshot().PhaseParalyzed, downed: 5));
            Assert.AreEqual(80f, result.DefensePhases);
            Assert.AreEqual(100f, result.DefenseCore);
            Assert.AreEqual(180f, result.Defense);
            Assert.AreEqual(180, result.Total);
        }

        [Test]
        public void 五阶段全瘫痪_阶段分清零()
        {
            var recorder = new MatchRecorder();
            for (var i = 0; i < MatchRecorder.PhaseCount; i++)
            {
                recorder.RecordCoreParalyze(i);
            }
            var result = ScoreCalculator.Calculate(MakeRecord(
                area: 0f, score: 0, ratio: 1f, phases: recorder.Snapshot().PhaseParalyzed, downed: 0));
            Assert.AreEqual(0f, result.DefensePhases);
            Assert.AreEqual(100f, result.Defense);
        }

        // ---- 奖牌边界：铜 400 / 银 600 / 金 800 ----

        [Test]
        public void 奖牌边界_399无牌_400铜牌()
        {
            // 面积 23.94㎡ → 400×0.9975 = 399（downed 5 → 生存 0，不掺入生存分）
            var justBelow = ScoreCalculator.Calculate(MakeRecord(area: 23.94f, downed: 5));
            Assert.AreEqual(399, justBelow.Total);
            Assert.AreEqual(Medal.None, justBelow.Medal);

            var atBronze = ScoreCalculator.Calculate(MakeRecord(area: 24f, downed: 5));
            Assert.AreEqual(400, atBronze.Total);
            Assert.AreEqual(Medal.Bronze, atBronze.Medal);
        }

        [Test]
        public void 奖牌边界_599铜牌_600银牌()
        {
            // 400(面积满) + 198.8(积分159→300×159/240) = 598.8 → 四舍五入 599（跨线进位，仍过 400 铜牌线）
            var justBelow = ScoreCalculator.Calculate(MakeRecord(area: 24f, score: 159, phases: AllParalyzed(), downed: 5));
            Assert.AreEqual(599, justBelow.Total);
            Assert.AreEqual(Medal.Bronze, justBelow.Medal);

            var atSilver = ScoreCalculator.Calculate(MakeRecord(area: 24f, score: 160, phases: AllParalyzed(), downed: 5));
            Assert.AreEqual(600, atSilver.Total);
            Assert.AreEqual(Medal.Silver, atSilver.Medal);
        }

        [Test]
        public void 奖牌边界_799银牌_800金牌()
        {
            // 400+300+99(核心 0.99，全阶段瘫痪) = 799
            var justBelow = ScoreCalculator.Calculate(MakeRecord(
                area: 24f, score: 240, ratio: 0.99f, phases: AllParalyzed(), downed: 5));
            Assert.AreEqual(799, justBelow.Total);
            Assert.AreEqual(Medal.Silver, justBelow.Medal);

            var atGold = ScoreCalculator.Calculate(MakeRecord(
                area: 24f, score: 240, ratio: 1f, phases: AllParalyzed(), downed: 5));
            Assert.AreEqual(800, atGold.Total);
            Assert.AreEqual(Medal.Gold, atGold.Medal);
        }

        [Test]
        public void 面积超满额线_封顶400分不溢出()
        {
            var result = ScoreCalculator.Calculate(MakeRecord(area: 240f, downed: 5));
            Assert.AreEqual(400f, result.Territory); // 400×min(240/24,1) = 400，不乘出 4000
            Assert.AreEqual(400, result.Total);
        }

        [Test]
        public void 生存分_倒地五次归零_四次仍得分()
        {
            var zero = ScoreCalculator.Calculate(MakeRecord(downed: 5));
            Assert.AreEqual(0f, zero.Survival); // 100-100=0
            var four = ScoreCalculator.Calculate(MakeRecord(downed: 4));
            Assert.AreEqual(20f, four.Survival);
        }

        // ---- 构造器 ----

        private static bool[] AllParalyzed() => new[] { true, true, true, true, true };

        private static bool[] NoneParalyzed() => new[] { false, false, false, false, false };

        private static MatchRecord MakeRecord(
            float area = 0f, int score = 0, float ratio = 0f, bool[] phases = null, int downed = 0)
        {
            return new MatchRecord
            {
                FinalAreaSqm = area,
                CombatScore = score,
                CoreHpRatio = ratio,
                PhaseParalyzed = phases ?? AllParalyzed(),
                DownedCount = downed
            };
        }
    }
}
