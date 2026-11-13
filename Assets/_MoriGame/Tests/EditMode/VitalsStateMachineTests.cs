using MoriGame.Player;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    // docs/09 对局流程：倒地 → 8 秒重生 → 3 秒保护 → 主动射击提前结束保护（docs/07 4.1）
    public class VitalsStateMachineTests
    {
        private static VitalsStateMachine New()
        {
            return new VitalsStateMachine(100, 8f, 3f);
        }

        [Test]
        public void 血量归零进入倒地()
        {
            var v = New();
            Assert.IsTrue(v.TakeDamage(100));
            Assert.AreEqual(VitalsState.Downed, v.State);
            Assert.AreEqual(0, v.Hp);
        }

        [Test]
        public void 倒地期间免疫伤害()
        {
            var v = New();
            v.TakeDamage(100);
            Assert.IsFalse(v.TakeDamage(50));
            Assert.AreEqual(VitalsState.Downed, v.State);
        }

        [Test]
        public void 倒地八秒后重生并进入三秒保护()
        {
            var v = New();
            v.TakeDamage(100);
            var respawned = 0;
            v.Respawned += () => respawned++;

            v.Tick(7.9f);
            Assert.AreEqual(VitalsState.Downed, v.State, "8 秒未到不应重生");

            v.Tick(0.1f);
            Assert.AreEqual(VitalsState.RespawnProtection, v.State);
            Assert.AreEqual(1, respawned);
            Assert.AreEqual(100, v.Hp, "重生满血");
        }

        [Test]
        public void 保护期免疫_三秒后恢复可受伤()
        {
            var v = New();
            v.TakeDamage(100);
            v.Tick(8f);
            Assert.IsFalse(v.TakeDamage(999), "保护期免疫");

            v.Tick(2.9f);
            Assert.AreEqual(VitalsState.RespawnProtection, v.State);
            v.Tick(0.1f);
            Assert.AreEqual(VitalsState.Alive, v.State);
            Assert.IsTrue(v.TakeDamage(10));
            Assert.AreEqual(90, v.Hp);
        }

        [Test]
        public void 主动射击提前结束保护()
        {
            var v = New();
            v.TakeDamage(100);
            v.Tick(8f);
            v.BreakProtection();
            Assert.AreEqual(VitalsState.Alive, v.State);
            Assert.IsTrue(v.TakeDamage(10), "破除保护后立即受伤");
        }

        [Test]
        public void 部分受伤不进入倒地()
        {
            var v = New();
            Assert.IsTrue(v.TakeDamage(30));
            Assert.AreEqual(VitalsState.Alive, v.State);
            Assert.AreEqual(70, v.Hp);
        }
    }
}
