using MoriGame.Gestures;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    // M1-T3.6 手势判定纯逻辑（docs/07 手势范式：捏合=确认，张掌前推=取消）
    public class GestureEdgeDetectorTests
    {
        [Test]
        public void 捏合按下沿触发一次()
        {
            var e = new GestureEdgeDetector();
            e.Update(false);
            Assert.IsFalse(e.Rising);
            e.Update(true);
            Assert.IsTrue(e.Rising, "按下帧触发");
            e.Update(true);
            Assert.IsFalse(e.Rising, "按住不重复触发");
            e.Update(false);
            Assert.IsFalse(e.Rising);
            e.Update(true);
            Assert.IsTrue(e.Rising, "松开后再按下重新触发");
        }
    }

    public class PalmPushDetectorTests
    {
        private const float Speed = 1.0f;
        private const float Hold = 0.12f;
        private const float Cooldown = 0.7f;

        [Test]
        public void 持续前推触发一次并进入冷却()
        {
            var d = new PalmPushDetector(Speed, Hold, Cooldown);
            d.Update(true, 2f, 0.05f);
            Assert.IsFalse(d.Triggered, "持续不足不触发");
            d.Update(true, 2f, 0.05f);
            Assert.IsFalse(d.Triggered);
            d.Update(true, 2f, 0.05f);
            Assert.IsTrue(d.Triggered, "持续 0.15s 触发");
            d.Update(true, 2f, 0.05f);
            Assert.IsFalse(d.Triggered, "冷却期不重复触发");
        }

        [Test]
        public void 冷却结束后可再次触发()
        {
            var d = new PalmPushDetector(Speed, Hold, Cooldown);
            for (var i = 0; i < 3; i++)
            {
                d.Update(true, 2f, 0.05f);
            }
            Assert.IsTrue(d.Triggered);
            var firedAgain = false;
            for (var i = 0; i < 20; i++)
            {
                d.Update(true, 2f, 0.05f);
                if (d.Triggered)
                {
                    firedAgain = true;
                }
            }
            Assert.IsTrue(firedAgain, "冷却 0.7s 结束后可再次触发");
        }

        [Test]
        public void 速度不足不触发()
        {
            var d = new PalmPushDetector(Speed, Hold, Cooldown);
            for (var i = 0; i < 30; i++)
            {
                d.Update(true, 0.5f, 0.05f);
            }
            Assert.IsFalse(d.Triggered);
        }

        [Test]
        public void 未伸直不触发()
        {
            var d = new PalmPushDetector(Speed, Hold, Cooldown);
            for (var i = 0; i < 30; i++)
            {
                d.Update(false, 5f, 0.05f);
            }
            Assert.IsFalse(d.Triggered);
        }

        [Test]
        public void 中断重新计时()
        {
            var d = new PalmPushDetector(Speed, Hold, Cooldown);
            d.Update(true, 2f, 0.05f);
            d.Update(true, 2f, 0.05f);
            d.Update(false, 2f, 0.05f);   // 中断，计时清零
            d.Update(true, 2f, 0.05f);
            Assert.IsFalse(d.Triggered, "中断后需重新累积");
        }
    }

    public class GazeUtilTests
    {
        [SetUp]
        public void ResetGlobals()
        {
            Territory.TerritoryGrid.WorldOrigin = Vector2.zero; // 隔离主域污染（2026-11-11）
            Territory.TerritoryGrid.WorldFloorY = 0f;
        }

        [Test]
        public void 低头凝视命中地面()
        {
            var headGo = new GameObject("gaze-head");
            try
            {
                headGo.transform.position = new Vector3(0f, 1.6f, 0f);
                headGo.transform.localEulerAngles = new Vector3(45f, 0f, 0f);
                Assert.IsTrue(GazeUtil.TryGetGroundPoint(headGo.transform, 8f, 13.4f, out var p));
                Assert.AreEqual(0f, p.y, 0.001f);
                Assert.AreEqual(1.6f, p.z, 0.05f, "下视 45° 落点应为 z≈1.6");
            }
            finally
            {
                Object.DestroyImmediate(headGo);
            }
        }

        [Test]
        public void 抬头无落点()
        {
            var headGo = new GameObject("gaze-head");
            try
            {
                headGo.transform.position = new Vector3(0f, 1.6f, 0f);
                headGo.transform.localEulerAngles = new Vector3(-10f, 0f, 0f);
                Assert.IsFalse(GazeUtil.TryGetGroundPoint(headGo.transform, 8f, 13.4f, out _));
            }
            finally
            {
                Object.DestroyImmediate(headGo);
            }
        }

        [Test]
        public void 远落点截断到最大距离()
        {
            var headGo = new GameObject("gaze-head");
            try
            {
                headGo.transform.position = new Vector3(0f, 1.6f, 0f);
                headGo.transform.localEulerAngles = new Vector3(10f, 0f, 0f); // 浅下视，落点 >8m
                Assert.IsTrue(GazeUtil.TryGetGroundPoint(headGo.transform, 8f, 13.4f, out var p));
                Assert.LessOrEqual(p.z, 8.01f, "超过 8 米截断");
            }
            finally
            {
                Object.DestroyImmediate(headGo);
            }
        }
    }
}
