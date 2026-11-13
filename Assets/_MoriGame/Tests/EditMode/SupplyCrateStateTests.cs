using MoriGame.Economy;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    public class SupplyCrateStateTests
    {
        [Test]
        public void 持续捏合一点五秒开箱_奖励400到600()
        {
            var crate = new SupplyCrateState(7);
            Assert.IsTrue(crate.Available);
            Assert.AreEqual(0, crate.Tick(true, 1.49f, 0f));
            Assert.AreEqual(0, crate.Tick(true, 0.009f, 1.49f));
            var reward = crate.Tick(true, 0.001f, 1.499f);
            Assert.That(reward, Is.InRange(400, 600));
            Assert.IsFalse(crate.Available);
        }

        [Test]
        public void 松开捏合重置开箱进度()
        {
            var crate = new SupplyCrateState(7);
            crate.Tick(true, 1.2f, 0f);
            crate.Tick(false, 0.1f, 1.2f);
            Assert.AreEqual(0f, crate.OpenProgress);
            Assert.AreEqual(0, crate.Tick(true, 1.49f, 1.3f));
            Assert.IsTrue(crate.Available);
        }

        [Test]
        public void 开启后120秒刷新()
        {
            var crate = new SupplyCrateState(7);
            crate.Tick(true, 1.5f, 0f);
            Assert.IsFalse(crate.Available);
            crate.Tick(false, 119.9f, 119.9f);
            Assert.IsFalse(crate.Available);
            crate.Tick(false, 0.1f, 120f);
            Assert.IsTrue(crate.Available);
        }

        [Test]
        public void 终局前60秒停止刷新()
        {
            var crate = new SupplyCrateState(7);
            crate.Tick(true, 1.5f, 0f);
            crate.Tick(false, 120f, 421f);
            Assert.IsFalse(crate.Available);
        }

        [Test]
        public void 同种子奖励序列可复算且不越界()
        {
            var a = new SupplyCrateState(42);
            var b = new SupplyCrateState(42);
            for (var i = 0; i < 12; i++)
            {
                var rewardA = a.Tick(true, 1.5f, 0f);
                var rewardB = b.Tick(true, 1.5f, 0f);
                Assert.AreEqual(rewardA, rewardB);
                Assert.That(rewardA, Is.InRange(400, 600));
                a.Tick(false, 120f, 0f);
                b.Tick(false, 120f, 0f);
            }
        }
    }
}
