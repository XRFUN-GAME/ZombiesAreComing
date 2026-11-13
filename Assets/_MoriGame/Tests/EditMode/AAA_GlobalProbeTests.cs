using MoriGame.Territory;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>全局静态污染诊断（2026-11-11 临时，排最前执行）：定位 WorldOrigin/WorldFloorY 脏值出现的时点。</summary>
    public class AAA_GlobalProbeTests
    {
        [Test]
        public void A1_域初始化后静态应干净()
        {
            Assert.AreEqual(Vector2.zero, TerritoryGrid.WorldOrigin,
                "域初始化静态脏 → 写入者在域加载期（[InitializeOnLoad]/场景 Awake）");
        }

        [Test]
        public void A2_次测试前静态仍干净()
        {
            Assert.AreEqual(Vector2.zero, TerritoryGrid.WorldOrigin,
                "A1 过了 A2 脏 → 写入者在两条测试之间（ExecuteAlways Update 等 tick 行为）");
        }
    }
}
