using MoriGame.Devices;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>新手翻身机制单测（2026-11-11 用户三选二：阶段减伤 + 瘫痪 EMP 反冲的纯函数部分）。</summary>
    public class NewbieComebackTests
    {
        [Test]
        public void 阶段减伤_P1P2半伤_P3起全额()
        {
            Assert.AreEqual(15, CoreDevice.ApplyNewbieFactor(30, 0, 2, 0.5f), "P1 受伤 ×0.5");
            Assert.AreEqual(15, CoreDevice.ApplyNewbieFactor(30, 1, 2, 0.5f), "P2 受伤 ×0.5");
            Assert.AreEqual(30, CoreDevice.ApplyNewbieFactor(30, 2, 2, 0.5f), "P3 起恢复全额");
            Assert.AreEqual(30, CoreDevice.ApplyNewbieFactor(30, 4, 2, 0.5f), "P5 全额");
            Assert.AreEqual(1, CoreDevice.ApplyNewbieFactor(1, 0, 2, 0.5f), "半伤向下取整保底 1（不掉 0）");
            Assert.AreEqual(30, CoreDevice.ApplyNewbieFactor(30, 0, 0, 0.5f), "newbiePhaseCount=0 = 永久关闭减伤");
            Assert.AreEqual(30, CoreDevice.ApplyNewbieFactor(30, -1, 2, 0.5f), "无调度器（-1）不减免");
        }

        [Test]
        public void 核心回血_仅正常态_钳上限()
        {
            var state = new CoreDeviceState(1200, 10f, 480);
            Assert.IsTrue(state.TakeDamage(400), "扣到 800");
            Assert.IsTrue(state.Heal(300), "正常态可回血");
            Assert.AreEqual(1100, state.Hp, "回血 800+300=1100");
            Assert.IsTrue(state.Heal(999), "超量回血");
            Assert.AreEqual(1200, state.Hp, "钳到 MaxHp");
            Assert.IsTrue(state.TakeDamage(1200), "打瘫（Disabled）");
            Assert.IsFalse(state.Heal(100), "瘫痪态禁回血（恢复走 recoverHp 流程）");
            Assert.IsFalse(state.Heal(0), "0 回血无效");
            Assert.IsFalse(state.Heal(-5), "负回血无效");
        }
    }
}
