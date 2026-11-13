using MoriGame.Gestures;
using MoriGame.Tutorial;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M2-T0 新手引导纯逻辑三件：清单状态机 / JIT 提示预算 / 图例淡出计数。
    /// 2026-11-05 站桩工作台制：四步=抓枪→射击→抓建筑→放置。
    /// 2026-11-06 转向移除：HurtNoTurn 提示与左图例用例删除；SnapTurnDetector 为留档组件照测。</summary>
    public class TutorialProgressTests
    {
        // ---- 引导清单：乱序完成 + 顺序引导 + 幂等 ----

        [Test]
        public void 清单_乱序完成四步_AllDone为真()
        {
            var c = new TutorialChecklist();
            Assert.IsFalse(c.AllDone);
            c.MarkDone(TutorialStep.FireOnce);  // 先射击（枪已在手的老玩家路径）
            c.MarkDone(TutorialStep.PlaceDown);
            c.MarkDone(TutorialStep.GrabWeapon);
            Assert.IsFalse(c.AllDone);
            c.MarkDone(TutorialStep.GrabStand); // 建筑最后才抓也补勾
            Assert.IsTrue(c.AllDone);
        }

        [Test]
        public void 清单_CurrentStep指向第一个未完成步_全完返回负一()
        {
            var c = new TutorialChecklist();
            Assert.AreEqual(0, c.CurrentStep);
            c.MarkDone(TutorialStep.GrabWeapon);
            Assert.AreEqual((int)TutorialStep.FireOnce, c.CurrentStep);
            c.MarkDone(TutorialStep.FireOnce);
            c.MarkDone(TutorialStep.GrabStand);
            Assert.AreEqual((int)TutorialStep.PlaceDown, c.CurrentStep);
            c.MarkDone(TutorialStep.PlaceDown);
            Assert.AreEqual(-1, c.CurrentStep);
        }

        [Test]
        public void 清单_重复打勾幂等_首次完成才返回真()
        {
            var c = new TutorialChecklist();
            Assert.IsTrue(c.MarkDone(TutorialStep.GrabWeapon));
            Assert.IsFalse(c.MarkDone(TutorialStep.GrabWeapon)); // 二次抓枪不重复计
        }

        // ---- JIT 提示预算：每类每局最多 2 次，类间独立 ----

        [Test]
        public void JIT_同类第三次拒发_前两次放行()
        {
            var b = new JitHintBudget();
            Assert.IsTrue(b.TryConsume(JitHintKind.FirstWave));
            Assert.IsTrue(b.TryConsume(JitHintKind.FirstWave));
            Assert.IsFalse(b.TryConsume(JitHintKind.FirstWave));
            Assert.AreEqual(JitHintBudget.MaxPerMatch, b.Used(JitHintKind.FirstWave));
        }

        [Test]
        public void JIT_类间独立计数_一类用尽不影响其他()
        {
            var b = new JitHintBudget();
            b.TryConsume(JitHintKind.FirstWave);
            b.TryConsume(JitHintKind.FirstWave); // 首波类用尽
            Assert.IsTrue(b.TryConsume(JitHintKind.StandIdle));
            Assert.AreEqual(1, b.Used(JitHintKind.StandIdle));
        }

        // ---- 图例淡出：操作满 3 次，右条需开火与确认都满 ----

        [Test]
        public void 图例_开火满而确认未满_右条仍可见()
        {
            var t = new LegendFadeTracker();
            for (var i = 0; i < LegendFadeTracker.FadeThreshold; i++)
            {
                t.Increment(TutorialAction.Fire);
            }
            Assert.IsTrue(t.RightVisible); // 确认还差着
            t.Increment(TutorialAction.Confirm);
            t.Increment(TutorialAction.Confirm);
            Assert.IsTrue(t.RightVisible);
            t.Increment(TutorialAction.Confirm);
            Assert.IsFalse(t.RightVisible); // 两个操作都满 3 → 右条淡出
        }

        [Test]
        public void 图例_计数封顶不无限增长()
        {
            var t = new LegendFadeTracker();
            for (var i = 0; i < 10; i++)
            {
                t.Increment(TutorialAction.Fire);
            }
            Assert.AreEqual(LegendFadeTracker.FadeThreshold, t.Count(TutorialAction.Fire));
        }

        // ---- 侧推转向检测器（站桩制新纯逻辑）----

        [Test]
        public void 转向_张掌侧推达阈值持续_hold_触发一次()
        {
            var d = new SnapTurnDetector(0.9f, 0.12f, 0.5f);
            Assert.IsFalse(d.Update(true, 0.5f, 0.05f)); // 分量不足
            Assert.IsFalse(d.Update(true, 1.2f, 0.05f)); // 持续 0.05s
            Assert.IsFalse(d.Update(true, 1.2f, 0.05f)); // 持续 0.10s，未到 0.12s
            Assert.IsTrue(d.Update(true, 1.2f, 0.05f));  // 累计 0.15s ≥ 0.12s → 触发
        }

        [Test]
        public void 转向_冷却期内不重复触发_冷却后可再触发()
        {
            var d = new SnapTurnDetector(0.9f, 0.12f, 0.5f);
            FillTrigger(d);
            Assert.IsFalse(d.Update(true, 1.2f, 0.1f));  // 冷却中（0.5s 未到）
            Assert.IsFalse(d.Update(true, 1.2f, 0.3f));
            Assert.IsTrue(d.Update(true, 1.2f, 0.2f));   // 冷却过完 + 重新累计 hold → 再触发
        }

        [Test]
        public void 转向_收掌断计_重新张掌重数hold()
        {
            var d = new SnapTurnDetector(0.9f, 0.12f, 0.5f);
            Assert.IsFalse(d.Update(true, 1.2f, 0.1f));  // 累计 0.1s
            Assert.IsFalse(d.Update(false, 1.2f, 0.05f)); // 收掌清零
            Assert.IsFalse(d.Update(true, 1.2f, 0.1f));  // 重新累计
            Assert.IsTrue(d.Update(true, 1.2f, 0.05f));  // 0.15s 后触发
        }

        [Test]
        public void 转向_缩放参数_90度连推节奏由冷却与hold决定()
        {
            // 决策参数：速度 0.9 / hold 0.12 / 冷却 0.5 —— 单次 45°，0.5s 后可再推叠到 90°
            var d = new SnapTurnDetector(0.9f, 0.12f, 0.5f);
            FillTrigger(d); // 第一次 45°
            var second = false;
            for (var i = 0; i < 12; i++)
            {
                second |= d.Update(true, 1.2f, 0.1f); // 持续推 1.2s 内冷却结束必然再触发一次
            }
            Assert.IsTrue(second); // 连推叠加成立（消费方叠加角度得 90°）
        }

        private static void FillTrigger(SnapTurnDetector d)
        {
            for (var i = 0; i < 20; i++) // 上限防呆：逻辑坏时快速失败而非挂死测试
            {
                if (d.Update(true, 1.2f, 0.05f))
                {
                    return;
                }
            }
            Assert.Fail("侧推检测器持续达阈未触发");
        }
    }
}
