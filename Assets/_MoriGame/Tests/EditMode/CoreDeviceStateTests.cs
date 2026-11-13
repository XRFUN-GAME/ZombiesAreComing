using MoriGame.Devices;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    // docs/09 对局流程：核心瘫痪 → 商店关闭、地盘归属不变、10 秒后恢复 480（docs/07 4.5）
    public class CoreDeviceStateTests
    {
        private static CoreDeviceState New()
        {
            return new CoreDeviceState(1200, 10f, 480);
        }

        [Test]
        public void 生命归零进入瘫痪并触发事件()
        {
            var core = New();
            var disabledCount = 0;
            core.Disabled += () => disabledCount++;
            Assert.IsTrue(core.TakeDamage(1200));
            Assert.AreEqual(CoreState.Disabled, core.State);
            Assert.AreEqual(0, core.Hp);
            Assert.AreEqual(1, disabledCount);
        }

        [Test]
        public void 瘫痪期间免疫伤害_十秒后恢复480()
        {
            var core = New();
            core.TakeDamage(1200);
            var recoveredCount = 0;
            core.Recovered += () => recoveredCount++;

            Assert.IsFalse(core.TakeDamage(999), "瘫痪期间不再受伤");
            core.Tick(9.9f);
            Assert.AreEqual(CoreState.Disabled, core.State, "10 秒未到不应恢复");

            core.Tick(0.1f);
            Assert.AreEqual(CoreState.Normal, core.State);
            Assert.AreEqual(480, core.Hp, "恢复到 40%（480）");
            Assert.AreEqual(1, recoveredCount);
        }

        [Test]
        public void 恢复后可再次瘫痪()
        {
            var core = New();
            core.TakeDamage(1200);
            core.Tick(10f);
            Assert.IsTrue(core.TakeDamage(480));
            Assert.AreEqual(CoreState.Disabled, core.State);
        }

        [Test]
        public void 部分受伤不瘫痪()
        {
            var core = New();
            Assert.IsTrue(core.TakeDamage(1199));
            Assert.AreEqual(CoreState.Normal, core.State);
            Assert.AreEqual(1, core.Hp);
        }
    }
}
