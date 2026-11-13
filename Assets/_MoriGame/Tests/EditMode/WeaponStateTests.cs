using MoriGame.Combat;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M1-T5 武器状态机（应急手枪 docs/03：50 伤害 · 3 发/秒 · 12 弹匣 · 1.4 秒换弹）</summary>
    public class WeaponStateTests
    {
        private static WeaponState NewPistol()
        {
            return new WeaponState(12, 3f, 1.4f);
        }

        [Test]
        public void 开火消耗弹药并进入射速冷却()
        {
            var w = NewPistol();
            Assert.AreEqual(FireResult.Fired, w.TryFire());
            Assert.AreEqual(11, w.AmmoInMag);
            Assert.AreEqual(FireResult.Cooldown, w.TryFire());
        }

        [Test]
        public void 冷却结束可再次开火()
        {
            var w = NewPistol();
            w.TryFire();
            w.Tick(0.32f); // 3 发/秒 → 间隔 0.3333，0.32 不够
            Assert.AreEqual(FireResult.Cooldown, w.TryFire());
            w.Tick(0.02f);
            Assert.AreEqual(FireResult.Fired, w.TryFire());
        }

        [Test]
        public void 按住连发约每秒三发()
        {
            var w = NewPistol();
            var fired = 0;
            var elapsed = 0f;
            while (elapsed < 1.05f)
            {
                if (w.TryFire() == FireResult.Fired)
                {
                    fired++;
                }
                w.Tick(0.01f);
                elapsed += 0.01f;
            }
            Assert.AreEqual(4, fired); // t=0 / 0.333 / 0.666 / 1.0 四发（3 发/秒含首发）
            Assert.AreEqual(8, w.AmmoInMag);
        }

        [Test]
        public void 弹匣打空返回Empty()
        {
            var w = NewPistol();
            for (var i = 0; i < 12; i++)
            {
                Assert.AreEqual(FireResult.Fired, w.TryFire());
                w.Tick(0.34f);
            }
            Assert.AreEqual(0, w.AmmoInMag);
            Assert.AreEqual(FireResult.Empty, w.TryFire());
        }

        [Test]
        public void 换弹计时到达后恢复满弹匣_浮点边界()
        {
            var w = NewPistol();
            w.TryFire(); // 消耗 1 发，弹匣非满才能换弹
            Assert.IsTrue(w.StartReload());
            w.Tick(1.3999999f); // T4 教训：浮点累加边界必须被 epsilon 吞掉
            Assert.IsFalse(w.IsReloading);
            Assert.AreEqual(12, w.AmmoInMag);
        }

        [Test]
        public void 换弹中不能开火()
        {
            var w = NewPistol();
            w.TryFire();
            w.StartReload();
            Assert.AreEqual(FireResult.Reloading, w.TryFire());
        }

        [Test]
        public void 满弹匣不可换弹()
        {
            var w = NewPistol();
            Assert.IsFalse(w.StartReload());
        }
    }
}
