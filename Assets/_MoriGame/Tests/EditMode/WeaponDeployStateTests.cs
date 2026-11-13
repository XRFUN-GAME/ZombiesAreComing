using MoriGame.Combat;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>抬手开火状态机（docs/07 2026-11-02 定案：0.3s 出枪 / 垂手 1s 收枪）</summary>
    public class WeaponDeployStateTests
    {
        [Test]
        public void 抬手零点三秒出枪_浮点边界()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.299f, true, false); // 低于 0.3-epsilon(0.0005)，不应出枪
            Assert.IsFalse(d.Deployed);
            d.Tick(0.001f, true, false);
            Assert.IsTrue(d.Deployed);
        }

        [Test]
        public void 中途离场重置抬手计时()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.2f, true, false);
            d.Tick(0.05f, false, false); // 离开有效区
            d.Tick(0.2f, true, false);
            Assert.IsFalse(d.Deployed, "计时被重置，0.2 < 0.3 不应出枪");
            d.Tick(0.1f, true, false);
            Assert.IsTrue(d.Deployed);
        }

        [Test]
        public void 垂手一秒收枪_浮点边界()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.3f, true, false);
            Assert.IsTrue(d.Deployed);
            d.Tick(0.999f, false, true); // 低于 1-epsilon(0.0005)，不应收枪
            Assert.IsTrue(d.Deployed);
            d.Tick(0.001f, false, true);
            Assert.IsFalse(d.Deployed);
        }

        [Test]
        public void 垂手中途抬手重置收枪计时()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.3f, true, false);
            d.Tick(0.8f, false, true);
            d.Tick(0.1f, true, false); // 垂手中途抬手
            d.Tick(0.9f, false, true);
            Assert.IsTrue(d.Deployed, "收枪计时被重置，0.9 < 1.0 不应收枪");
        }

        [Test]
        public void 离开有效区但未垂手保持出枪()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.3f, true, false);
            d.Tick(5f, false, false); // 平举指向：不在胸前有效区但也没垂手
            Assert.IsTrue(d.Deployed);
        }

        [Test]
        public void 收枪后再抬手可再出枪()
        {
            var d = new WeaponDeployState(0.3f, 1f);
            d.Tick(0.3f, true, false);
            d.Tick(1f, false, true);
            Assert.IsFalse(d.Deployed);
            d.Tick(0.3f, true, false);
            Assert.IsTrue(d.Deployed);
        }
    }

    /// <summary>拍击弹匣换弹检测（docs/07 2026-11-02 定案：0.15m + 速度阈值区分停留/拍击）</summary>
    public class MagSlapDetectorTests
    {
        [Test]
        public void 高速进入触发一次_停留不重复触发()
        {
            var s = new MagSlapDetector(1.2f, 0.8f);
            Assert.IsTrue(s.Update(true, 2.0f, 0.016f), "高速进入弹匣范围应触发");
            Assert.IsFalse(s.Update(true, 2.0f, 0.016f), "持续在范围内不重复触发");
        }

        [Test]
        public void 低速进入_停留不触发()
        {
            var s = new MagSlapDetector(1.2f, 0.8f);
            Assert.IsFalse(s.Update(true, 0.5f, 0.016f), "低于速度阈值=停留，不是拍击");
            Assert.IsFalse(s.Update(true, 0.3f, 0.016f));
        }

        [Test]
        public void 冷却期内再次拍击不触发_冷却后恢复()
        {
            var s = new MagSlapDetector(1.2f, 0.8f);
            Assert.IsTrue(s.Update(true, 2.0f, 0.016f));
            s.Update(false, 0f, 0.016f); // 离开范围
            Assert.IsFalse(s.Update(true, 2.0f, 0.016f), "冷却期内不触发");
            s.Update(false, 0f, 0.8f); // 冷却耗尽并离开范围
            Assert.IsTrue(s.Update(true, 2.0f, 0.016f), "冷却结束后恢复触发");
        }

        [Test]
        public void 未离开范围即使冷却结束也不触发()
        {
            var s = new MagSlapDetector(1.2f, 0.8f);
            Assert.IsTrue(s.Update(true, 2.0f, 0.016f));
            for (var i = 0; i < 60; i++)
            {
                Assert.IsFalse(s.Update(true, 2.0f, 0.016f), "手一直按在弹匣上不应连发换弹");
            }
        }
    }

    /// <summary>准星动态深度（2026-11-02 复视修复）</summary>
    public class ReticlePlacementTests
    {
        [Test]
        public void 物理命中贴命中表面()
        {
            Assert.AreEqual(3.2f, ReticlePlacement.ComputeDistance(true, 3.2f, false, 0f));
        }

        [Test]
        public void 落空贴地面_最远六米截断()
        {
            Assert.AreEqual(4.5f, ReticlePlacement.ComputeDistance(false, 0f, true, 4.5f));
            Assert.AreEqual(6f, ReticlePlacement.ComputeDistance(false, 0f, true, 10f), "超 6 米截断");
        }

        [Test]
        public void 朝天无地面交点隐藏()
        {
            Assert.IsNull(ReticlePlacement.ComputeDistance(false, 0f, false, 0f));
        }

        [Test]
        public void 近距离下限以下隐藏()
        {
            Assert.IsNull(ReticlePlacement.ComputeDistance(true, 0.2f, false, 0f));
        }
    }
}
