using MoriGame.Audio;
using NUnit.Framework;
using UnityEditor;

namespace MoriGame.Tests.EditMode
{
    /// <summary>音效库资产断引用（2026-11-10 音效系统）：音效没法自动化听，
    /// 断「全部字段非空 + 音池数量正确 + 全局参数」即接线完整性的守门断言。</summary>
    public class SfxLibraryTests
    {
        private static SfxLibrary Load()
        {
            return AssetDatabase.LoadAssetAtPath<SfxLibrary>("Assets/_MoriGame/Configs/SfxLibrary.asset");
        }

        [Test]
        public void 音效库全字段非空()
        {
            var s = Load();
            Assert.NotNull(s, "SfxLibrary.asset 未生成（重跑 config-gen）");
            Assert.NotNull(s.playerFire, "开火枪声（每发一响）");
            Assert.NotNull(s.magSnap, "弹匣拍入");
            Assert.NotNull(s.hit, "命中");
            Assert.NotNull(s.weakpointHit, "弱点命中");
            Assert.NotNull(s.zombieMeleeAttack, "僵尸近战攻击");
            Assert.NotNull(s.acidSpit, "酸液吐射");
            Assert.NotNull(s.turretFire, "机枪塔开火");
            Assert.NotNull(s.fenceLoop, "电网 loop");
            Assert.NotNull(s.coreDown, "中控瘫痪");
            Assert.NotNull(s.coreRecover, "中控恢复");
            Assert.NotNull(s.crateOpen, "补给箱开箱");
            Assert.NotNull(s.playerDown, "玩家倒地");
            Assert.NotNull(s.coinBig, "金币大");
            Assert.NotNull(s.coinSmall, "金币小");
            Assert.NotNull(s.purchaseSuccess, "购买成功");
            Assert.NotNull(s.shopOpen, "商店展开");
            Assert.NotNull(s.wristMenuOpen, "腕部菜单展开（预留）");
            Assert.NotNull(s.uiClick, "UI 点击");
            Assert.NotNull(s.grab, "抓取");
            Assert.NotNull(s.dock, "放回");
            Assert.NotNull(s.success, "成功");
            Assert.NotNull(s.fail, "失败");
            Assert.NotNull(s.bgmLoop1, "背景音乐轮播 1");
            Assert.NotNull(s.bgmLoop2, "背景音乐轮播 2");
            Assert.NotNull(s.bgmTension, "紧张插曲");
            Assert.NotNull(s.bgmBoss, "Boss 主题");
        }

        [Test]
        public void 背景音乐参数_轮播淡化与插曲概率()
        {
            var s = Load();
            Assert.NotNull(s);
            Assert.Greater(s.bgmVolume, 0f, "BGM 音量必须可闻");
            Assert.GreaterOrEqual(s.bgmCrossfadeSeconds, 1f, "交叉淡化窗口 ≥1 秒（切换丝滑）");
            Assert.Greater(s.bgmTensionChance, 0f, "插曲概率 >0（偶尔夹杂紧张曲）");
            Assert.LessOrEqual(s.bgmTensionChance, 1f, "插曲概率 ≤1");
        }

        [Test]
        public void 死亡音池数量正确_普通五壮汉二Boss一弱点一()
        {
            var s = Load();
            Assert.NotNull(s);
            Assert.AreEqual(5, s.normalDeathPool.Length, "普通死亡音池 5 个");
            Assert.AreEqual(2, s.bruteDeathPool.Length, "壮汉（蛮兽）死亡音池 2 个");
            Assert.NotNull(s.overseerDeath, "Boss（督战者）死亡 1 个");
            Assert.NotNull(s.weakpointKill, "弱点击杀 1 个");
            foreach (var clip in s.normalDeathPool)
            {
                Assert.NotNull(clip, "普通死亡音池存在空槽");
            }
            foreach (var clip in s.bruteDeathPool)
            {
                Assert.NotNull(clip, "壮汉死亡音池存在空槽");
            }
        }

        [Test]
        public void 全局参数_音量零点七UI零点五音距十五并发三()
        {
            var s = Load();
            Assert.NotNull(s);
            Assert.AreEqual(0.7f, s.sfxVolume, "SFX 全局音量 0.7");
            Assert.AreEqual(0.5f, s.uiVolume, "UI 类音量 0.5");
            Assert.AreEqual(15f, s.spatialMaxDistance, "3D 声源最大音距 15 米");
            Assert.AreEqual(3, s.deathConcurrentLimit, "死亡音同帧并发上限 3");
            Assert.AreEqual(0.95f, s.pitchMin, "音调随机下限");
            Assert.AreEqual(1.05f, s.pitchMax, "音调随机上限");
        }
    }
}
