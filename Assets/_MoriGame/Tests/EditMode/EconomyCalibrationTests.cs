using MoriGame.Configs;
using NUnit.Framework;
using UnityEditor;

namespace MoriGame.Tests.EditMode
{
    /// <summary>经济标定 v2（2026-11-03）：收入端 ×10（击杀/阶段奖励/补给箱 docs）、初始金币 300（特指定值）、
    /// 支出价格一律不动、倒地罚金不变、中控瘫痪罚金 800（2026-11-06 T5）、准备期 12 秒。
    /// 直接断言真实配置资产，防 GenerateAll 漏改/回退。</summary>
    public class EconomyCalibrationTests
    {
        private const string Root = "Assets/_MoriGame/Configs";

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, "缺少配置资产：" + path);
            return asset;
        }

        [Test]
        public void 初始金币300_中控瘫痪罚金800()
        {
            var player = Load<PlayerConfig>($"{Root}/Player/PlayerConfig.asset");
            Assert.AreEqual(300, player.startingGold); // 经济标定 v2 特指定值（非 ×10）
            Assert.AreEqual(15f, player.downedGoldLossPercent); // 不变
            Assert.AreEqual(60, player.downedGoldLossCap); // 不变
            var core = Load<CoreDeviceConfig>($"{Root}/Devices/CoreDeviceConfig.asset");
            Assert.AreEqual(800, core.paralyzeGoldPenalty); // M2-T5：瘫痪立即扣最多 800
        }

        [Test]
        public void 击杀金币收入端乘十()
        {
            Assert.AreEqual(40, Load<ZombieConfig>($"{Root}/Zombies/Zombie_Normal.asset").killGold);
            Assert.AreEqual(50, Load<ZombieConfig>($"{Root}/Zombies/Zombie_Runner.asset").killGold);
            Assert.AreEqual(80, Load<ZombieConfig>($"{Root}/Zombies/Zombie_Spitter.asset").killGold);
            Assert.AreEqual(120, Load<ZombieConfig>($"{Root}/Zombies/Zombie_ShieldGuard.asset").killGold);
            Assert.AreEqual(200, Load<ZombieConfig>($"{Root}/Zombies/Zombie_Brute.asset").killGold);
            Assert.AreEqual(1200, Load<ZombieConfig>($"{Root}/Zombies/Zombie_Overseer.asset").killGold);
        }

        [Test]
        public void 手枪单发伤害50_20261113()
        {
            var pistol = Load<WeaponConfig>($"{Root}/Weapons/Weapon_Pistol.asset");
            Assert.AreEqual(50, pistol.damagePerPellet, "玩家手枪单发伤害 24→50");
            Assert.AreEqual(1, pistol.pelletsPerShot, "仍为单弹丸，不叠乘弹丸数");
        }

        [Test]
        public void 武器与道具购买价格不动()
        {
            Assert.AreEqual(0, Load<WeaponConfig>($"{Root}/Weapons/Weapon_Pistol.asset").price);
            Assert.AreEqual(180, Load<WeaponConfig>($"{Root}/Weapons/Weapon_SMG.asset").price);
            Assert.AreEqual(260, Load<WeaponConfig>($"{Root}/Weapons/Weapon_Shotgun.asset").price);
            Assert.AreEqual(320, Load<WeaponConfig>($"{Root}/Weapons/Weapon_Rifle.asset").price);
            Assert.AreEqual(360, Load<WeaponConfig>($"{Root}/Weapons/Weapon_ArcGun.asset").price);

            Assert.AreEqual(70, Load<PropConfig>($"{Root}/Props/Prop_Roadblock.asset").price);
            Assert.AreEqual(300, Load<PropConfig>($"{Root}/Props/Prop_AutoTurret.asset").price);
            Assert.AreEqual(200, Load<PropConfig>($"{Root}/Props/Prop_ElectricFence.asset").price); // 2026-11-10 用户定版（140→200）
            Assert.AreEqual(140, Load<PropConfig>($"{Root}/Props/Prop_SlowField.asset").price);
            Assert.AreEqual(160, Load<PropConfig>($"{Root}/Props/Prop_RepairDrone.asset").price);
            Assert.AreEqual(120, Load<PropConfig>($"{Root}/Props/Prop_FlameTrap.asset").price);
            Assert.AreEqual(120, Load<PropConfig>($"{Root}/Props/Prop_Demolisher.asset").price);
        }

        [Test]
        public void 电网区域减速场_四乘一格_伤害与双档减速()
        {
            var fence = Load<PropConfig>($"{Root}/Props/Prop_ElectricFence.asset");
            Assert.AreEqual(2f, fence.footprintCellsX);
            Assert.AreEqual(0.5f, fence.footprintCellsY);
            Assert.AreEqual(5, fence.fenceDamage); // 2026-11-10 用户定版：5×2 = 10 伤/秒
            Assert.AreEqual(0.5f, fence.fenceTickSeconds);
            Assert.AreEqual(60f, fence.slowPercent);
            Assert.AreEqual(30f, fence.bossSlowPercent);
            Assert.AreEqual(10, fence.fenceSelfDamagePerTick, "每跳自扣 10 血（打人自己掉血，随作战衰减）");
            Assert.AreEqual(1000, fence.maxHealth, "电网血量 200→1000（补偿自扣衰减）");
        }

        [Test]
        public void 机枪塔用户定版_20261110()
        {
            var turret = Load<PropConfig>($"{Root}/Props/Prop_AutoTurret.asset");
            Assert.AreEqual(8, turret.turretDamage, "8 伤/发 × 5 发/秒 = 40 DPS");
            Assert.AreEqual(5f, turret.turretFireRatePerSecond);
            Assert.AreEqual(5, turret.turretRangePadCells, "射界 2×2 占地 ±5 格 = 12×12 格（6×6 米，2026-11-11 用户）");
            Assert.AreEqual(20, turret.maxPerPlayer, "放置上限 10→20（2026-11-10 用户）");
            Assert.AreEqual(300, turret.maxHealth, "血量 260→300");
        }

        [Test]
        public void 共享上限与同屏校准_20261110()
        {
            var fence = Load<PropConfig>($"{Root}/Props/Prop_ElectricFence.asset");
            Assert.AreEqual(0, fence.maxPerPlayer, "电网走共享上限");
            Assert.AreEqual(60, MoriGame.Shop.ShopService.SharedNormalFacilityCap, "共享普通设施上限 60");
            var match = Load<MatchPhaseConfig>($"{Root}/MatchPhases.asset");
            Assert.AreEqual(20, match.maxActiveZombiesPerPlayer, "同屏默认 20（P5 由 aliveCap=25 覆盖）");
        }

        [Test]
        public void 阶段奖励乘十_准备期12秒首波十二秒()
        {
            var match = Load<MatchPhaseConfig>($"{Root}/MatchPhases.asset");
            Assert.AreEqual(300, match.initialGold); // 与 PlayerConfig.startingGold 同步
            Assert.AreEqual(12, match.phases[0].endSeconds);
            Assert.AreEqual(12, match.phases[1].startSeconds); // 首波 00:12
            for (var i = 1; i < match.phases.Count; i++)
            {
                Assert.AreEqual(300, match.phases[i].phaseArrivalGoldReward,
                    $"阶段 {match.phases[i].phaseId} 到达奖励应为 ×10 后的 300");
            }
            Assert.AreEqual(0, match.phases[0].phaseArrivalGoldReward); // 准备期无奖励
        }
    }
}
