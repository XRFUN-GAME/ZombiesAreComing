using System.Collections.Generic;
using MoriGame.Vfx;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>特效库资产断引用（2026-11-11 特效系统，风格同 SfxLibraryTests）：
    /// 粒子效果没法自动化看，断「全部条目 prefab 非空且含 ParticleSystem + 尺度/上限参数」即接线完整性守门。</summary>
    public class VfxLibraryTests
    {
        private static VfxLibrary Load()
        {
            return AssetDatabase.LoadAssetAtPath<VfxLibrary>("Assets/_MoriGame/Configs/VfxLibrary.asset");
        }

        private static List<VfxEntry> AllEntries(VfxLibrary s)
        {
            return new List<VfxEntry>
            {
                s.muzzleShot, s.shellEject, s.projectileTrail,
                s.acidProjectile, s.acidImpact,
                s.bloodHit, s.bloodWeak, s.deathNormal, s.deathBrute, s.goldGain,
                s.turretFire, s.turretHit,
                s.fenceLoop, s.coreDown, s.coreRecover,
                s.smokeLight, s.smokeMid, s.smokeHeavy, s.buildingDestroy,
                s.crateOpen, s.crateGlow, s.purchaseSuccess, s.placementDust,
                s.playerDowned, s.protectionHalo, s.teleportLanding,
                s.medalConfetti, s.medalFireworks, s.damageFire
            };
        }

        [Test]
        public void 已指派条目含粒子系统且槽位合法()
        {
            var s = Load();
            Assert.NotNull(s, "VfxLibrary.asset 未生成（重跑 config-gen）");
            var entries = AllEntries(s);
            Assert.AreEqual(29, entries.Count, "条目总数 29（映射表见决策日志，含危急火）");
            // 2026-11-12 特效资产改用户自管：prefab 置空 = 停用（VfxBus 空引用守卫静默跳过），属合法状态；
            // 仅对已指派的条目断言含 ParticleSystem。
            var assigned = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                Assert.NotNull(entry, "条目对象为空：#" + i);
                if (entry.prefab == null)
                {
                    continue;
                }
                assigned++;
                Assert.Greater(entry.prefab.GetComponentsInChildren<ParticleSystem>(true).Length, 0,
                    "prefab 不含 ParticleSystem：#" + i + " " + entry.prefab.name);
            }
            Assert.Greater(assigned, 0, "至少保留一个已指派条目（全空等于特效整体停用，请直接勾 VfxEnabled=false）");
        }

        [Test]
        public void 并发上限_爆体六溅血十()
        {
            var s = Load();
            Assert.NotNull(s);
            Assert.AreEqual(6, s.deathNormal.maxConcurrent, "普通爆体同帧上限 6");
            Assert.AreEqual(6, s.deathBrute.maxConcurrent, "壮汉爆体同帧上限 6");
            Assert.AreEqual(10, s.bloodHit.maxConcurrent, "溅血同帧上限 10");
            Assert.AreEqual(10, s.bloodWeak.maxConcurrent, "弱点溅血同帧上限 10");
        }

        [Test]
        public void 尺度修正参数()
        {
            var s = Load();
            Assert.NotNull(s);
            var entries = AllEntries(s);
            for (var i = 0; i < entries.Count; i++)
            {
                var minimumScale = entries[i] == s.muzzleShot ? 0.05f : 0.1f; // 枪口 0.05 为用户 2026-11-13 手调权威值
                Assert.GreaterOrEqual(entries[i].scale, minimumScale, "尺度过小等于看不见：#" + i);
                Assert.LessOrEqual(entries[i].scale, 1f, "尺度超过 PC 原始尺寸（站桩 5 米场景不需要）：#" + i);
            }
        }
    }
}
