using System.Collections.Generic;
using System.IO;
using MoriGame.Audio;
using MoriGame.Configs;
using UnityEditor;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>按 docs/01、03、04、05、06 数值表批量生成配置资产。
    /// 重复运行会覆盖为文档基准值（设计者后续手改的资产不要放这些路径或改后勿重跑）。</summary>
    public static class ConfigAssetGenerator
    {
        private const string Root = "Assets/_MoriGame/Configs";
        private static readonly List<string> Report = new List<string>();

        public static void GenerateAll()
        {
            try
            {
                EnsureDir(Root + "/Weapons");
                EnsureDir(Root + "/Zombies");
                EnsureDir(Root + "/Props");
                EnsureDir(Root + "/Cards");

                GenerateWeapons();
                GenerateZombies();
                GenerateProps();
                GenerateCards();
                GenerateMatchPhases();
                GeneratePlayerCoreConfigsInner();
                CreateSfxLibrary();
                // CreateVfxLibrary 已移出批量（2026-11-11 用户自管区）：手改的 VfxLibrary.asset 会被批量覆盖，
                // 重置映射时单独调 GenerateVfxLibrary()

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                File.WriteAllText(Root + "/generation-report.txt", string.Join("\n", Report));
                Debug.Log($"[MoriGame] config generation done: {Report.Count} entries");
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        private static void EnsureDir(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static T Save<T>(string path, System.Action<T> setup) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            var created = asset == null;
            if (created)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            setup(asset);
            EditorUtility.SetDirty(asset);
            Report.Add($"{(created ? "CREATE" : "OVERWRITE")} {path}");
            return asset;
        }

        // docs/07 4.1/4.5 + docs/01 3.4 + docs/05 7.1（M1-T4）
        private static void GeneratePlayerCoreConfigsInner()
        {
            EnsureDir(Root + "/Player");
            EnsureDir(Root + "/Devices");
            Save<PlayerConfig>($"{Root}/Player/PlayerConfig.asset", p =>
            {
                p.maxHp = 100;
                p.downedSeconds = 8f;
                p.respawnProtectionSeconds = 3f;
                p.downedGoldLossPercent = 15f;
                p.downedGoldLossCap = 60;
                p.startingGold = 300; // 经济标定 v2：特指定值（非 ×10），灰盒宽松经济，M2 回写 docs
            });
            Save<CoreDeviceConfig>($"{Root}/Devices/CoreDeviceConfig.asset", c =>
            {
                c.maxHp = 1200;
                c.paralyzeSeconds = 10f;
                c.paralyzeGoldPenalty = 800;
                c.recoverHp = 480;
                c.newbiePhaseCount = 2; c.newbieDamageFactor = 0.5f; // 新手翻身（2026-11-11 用户三选二）
                c.empKnockbackMeters = 2f; c.empStunSeconds = 2f; // 瘫痪 EMP 反冲
            });
        }

        /// <summary>M1-T4：只生成玩家/中控两份配置（不触碰 T2 资产）</summary>
        public static void GeneratePlayerCoreConfigs()
        {
            try
            {
                GeneratePlayerCoreConfigsInner();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                File.WriteAllText(Root + "/generation-report-playercore.txt", string.Join("\n", Report));
                Debug.Log("[MoriGame] player/core config generation done");
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        // docs/03 武器数值表
        private static void GenerateWeapons()
        {
            Save<WeaponConfig>($"{Root}/Weapons/Weapon_Pistol.asset", w =>
            {
                w.weaponId = "Pistol"; w.displayName = "应急手枪";
                w.unlockAtSeconds = 0; w.price = 0;
                w.damagePerPellet = 50; w.pelletsPerShot = 1; w.fireRatePerSecond = 3f;
                w.magazineSize = 100; w.reloadSeconds = 1.4f; // 2026-11-02 实测 12→100：用户手感优先（M2 数值校准统一回归策划案基准 12）
                w.notes = "稳定精准；普通僵尸身体 3 发、弱点 2 发";
            });
            Save<WeaponConfig>($"{Root}/Weapons/Weapon_SMG.asset", w =>
            {
                w.weaponId = "SMG"; w.displayName = "冲锋枪";
                w.unlockAtSeconds = 60; w.price = 180;
                w.damagePerPellet = 16; w.pelletsPerShot = 1; w.fireRatePerSecond = 10f;
                w.magazineSize = 30; w.reloadSeconds = 1.8f;
                w.rangeFalloffStartMeters = 15f;
                w.notes = "中近距离，15 米后衰减";
            });
            Save<WeaponConfig>($"{Root}/Weapons/Weapon_Shotgun.asset", w =>
            {
                w.weaponId = "Shotgun"; w.displayName = "战术霰弹枪";
                w.unlockAtSeconds = 300; w.price = 260;
                w.damagePerPellet = 12; w.pelletsPerShot = 10; w.fireRatePerSecond = 1.1f;
                w.magazineSize = 6; w.reloadSeconds = 2.2f;
                w.interruptsPounce = true;
                w.notes = "6 米内高爆发，可打断疾行者扑击";
            });
            Save<WeaponConfig>($"{Root}/Weapons/Weapon_Rifle.asset", w =>
            {
                w.weaponId = "Rifle"; w.displayName = "战斗步枪";
                w.unlockAtSeconds = 540; w.price = 320;
                w.damagePerPellet = 55; w.pelletsPerShot = 1; w.fireRatePerSecond = 3f;
                w.magazineSize = 20; w.reloadSeconds = 2.0f;
                w.notes = "中远距离精准";
            });
            Save<WeaponConfig>($"{Root}/Weapons/Weapon_ArcGun.asset", w =>
            {
                w.weaponId = "ArcGun"; w.displayName = "电弧枪";
                w.unlockAtSeconds = 780; w.price = 360;
                w.damagePerPellet = 45; w.pelletsPerShot = 1; w.fireRatePerSecond = 1.5f;
                w.magazineSize = 12; w.reloadSeconds = 2.4f;
                w.arcChainJumpCount = 2; w.arcChainDamage = 27; w.arcChainRangeMeters = 8f;
                w.arcSkipsBoss = true;
                w.notes = "8 米内连锁；对蛮兽不跳跃";
            });
        }

        // docs/04 僵尸数值表（经济标定 v2 2026-11-03：击杀金币收入端 ×10；combatScore/其余数值不动）
        private static void GenerateZombies()
        {
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_Normal.asset", z =>
            {
                z.zombieId = "Normal"; z.displayName = "普通感染者";
                z.threatPoints = 1; z.maxHealth = 120; z.moveSpeedMps = 0.8f; // 血量 ×2（2026-11-11 用户）
                z.meleeDamage = 8; z.killGold = 40; z.combatScore = 1;
                z.weakpointMultiplier = 2f;
                z.notes = "沿通路接近最近有效目标";
            });
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_Runner.asset", z =>
            {
                z.zombieId = "Runner"; z.displayName = "疾行者";
                z.threatPoints = 2; z.maxHealth = 120; z.moveSpeedMps = 1.8f; // 血量 ×3（2026-11-11 用户）
                z.meleeDamage = 6; z.killGold = 50; z.combatScore = 2;
                z.weakpointMultiplier = 2f;
                z.hitsToStagger = 3; z.staggerSeconds = 0.5f; z.staggerChainResetSeconds = 1.5f;
                z.notes = "1.8 米/秒优先扑玩家；任意来源连续命中 3 次硬直 0.5 秒，1.5 秒未续击重新计数";
            });
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_Spitter.asset", z =>
            {
                z.zombieId = "Spitter"; z.displayName = "喷吐者";
                z.threatPoints = 3; z.maxHealth = 240; z.moveSpeedMps = 0.65f; // 血量 ×3（2026-11-11 用户）
                z.meleeDamage = 0; z.killGold = 80; z.combatScore = 3;
                z.weakpointMultiplier = 2f;
                z.acidDamage = 10; z.acidCooldownSeconds = 3f; z.acidRangeMeters = 2f; // 2026-11-11 用户：3→2 米（4 格）
                z.acidProjectileSpeedMps = 6f;
                z.acidWindupSeconds = 0.35f; // 起手延迟对齐 Attack 动画（2026-11-11 实测反馈）
                z.notes = "2 米内停下吐酸（6→3→2 三次收紧）；酸液对设施同伤害";
            });
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_ShieldGuard.asset", z =>
            {
                z.zombieId = "ShieldGuard"; z.displayName = "盾卫";
                z.threatPoints = 5; z.maxHealth = 900; z.moveSpeedMps = 0.7f; // 血量 ×5（2026-11-11 用户）
                z.meleeDamage = 12; z.killGold = 120; z.combatScore = 5;
                z.weakpointMultiplier = 2f;
                z.frontShieldHealth = 120;
                z.notes = "正面减伤；侧后与腿部为弱点";
            });
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_Brute.asset", z =>
            {
                z.zombieId = "Brute"; z.displayName = "蛮兽";
                z.threatPoints = 6; z.maxHealth = 1800; z.moveSpeedMps = 0.55f; // 血量 ×5（2026-11-11 用户）
                z.meleeDamage = 20; z.killGold = 200; z.combatScore = 8;
                z.weakpointMultiplier = 2f; z.isBossType = true;
                z.notes = "优先攻击路障、自动设施和核心";
            });
            Save<ZombieConfig>($"{Root}/Zombies/Zombie_Overseer.asset", z =>
            {
                z.zombieId = "Overseer"; z.displayName = "督战者";
                z.threatPoints = 30; z.maxHealth = 18000; z.moveSpeedMps = 0.45f; // 血量 ×10（2026-11-11 用户）
                z.meleeDamage = 25; z.killGold = 1200; z.combatScore = 40;
                z.weakpointMultiplier = 1.5f; z.isBossType = true;
                z.chargeDamage = 35;
                z.notes = "07:12 出现一次；阶段冲锋 + 召唤普通感染者";
            });
        }

        // docs/06 道具数值表 + 破拆器（拓界信标已随建设即扩张废除，2026-11-03 docs/02）
        private static void GenerateProps()
        {
            Save<PropConfig>($"{Root}/Props/Prop_Roadblock.asset", p =>
            {
                p.propId = "Roadblock"; p.displayName = "路障";
                p.unlockAtSeconds = 0; p.price = 70; p.maxHealth = 450;
                p.footprintCellsX = 1f; p.footprintCellsY = 0.5f;
                p.notes = "阻挡普通僵尸改寻路；不许彻底封死系统保留通路";
            });
            Save<PropConfig>($"{Root}/Props/Prop_AutoTurret.asset", p =>
            {
                p.propId = "AutoTurret"; p.displayName = "自动机枪塔";
                p.unlockAtSeconds = 0; p.price = 300; p.maxHealth = 300; // 2026-11-10 用户定版
                p.footprintCellsX = 1f; p.footprintCellsY = 1f; p.maxPerPlayer = 20; // 上限 10→20（2026-11-10 用户，T7 数值回写）
                p.turretRangePadCells = 5; p.turretDamage = 8; p.turretFireRatePerSecond = 5f; // 8×5=40DPS；射界 12×12 格=6×6 米（±5 格，2026-11-11 用户）
                p.notes = "占地 2×2 格，射界 12×12 格（落地圈地=射界）；受击盒=模型包围盒×0.9（僵尸贴模型啃咬，不停在占地边缘）；优先最近目标";
            });
            // M2-T3.5 电网改区域减速场（2026-11-05）；2026-11-10 用户定版：减速 60%/督战 30% + 10 伤/秒 + 每跳自扣 10 血
            Save<PropConfig>($"{Root}/Props/Prop_ElectricFence.asset", p =>
            {
                p.propId = "ElectricFence"; p.displayName = "电网";
                p.unlockAtSeconds = 0; p.price = 200; p.maxHealth = 1000;
                p.footprintCellsX = 2f; p.footprintCellsY = 0.5f; // 2×0.5 米 = 4×1 格
                p.fenceDamage = 5; p.fenceTickSeconds = 0.5f; // 5×2 = 10 伤/秒
                p.fenceSelfDamagePerTick = 10; // 每次造成伤害的跳表自扣 10 血（1000 血 ≈ 50 秒持续作战）
                p.slowPercent = 60f; p.bossSlowPercent = 30f; // 30% 仅督战者（isFinalBoss），蛮兽吃 60%
                p.notes = "4×1 格区域减速场：-60% / 督战者 -30%，10 伤/秒且每跳自扣 10 血；无阻挡；共享普通设施上限 60；圈地 6×5 对称矩形";
            });
            Save<PropConfig>($"{Root}/Props/Prop_SlowField.asset", p =>
            {
                p.propId = "SlowField"; p.displayName = "减速力场";
                p.unlockAtSeconds = 300; p.price = 140; p.maxHealth = 220;
                p.footprintCellsX = 1f; p.footprintCellsY = 1f;
                p.slowRadiusMeters = 2.5f; p.slowPercent = 35f; p.bossSlowPercent = 15f;
                p.notes = "无伤害";
            });
            Save<PropConfig>($"{Root}/Props/Prop_RepairDrone.asset", p =>
            {
                p.propId = "RepairDrone"; p.displayName = "修复无人机";
                p.unlockAtSeconds = 540; p.price = 160; p.maxHealth = 180;
                p.footprintCellsX = 0.5f; p.footprintCellsY = 0.5f; p.maxPerPlayer = 2;
                p.repairRangeMeters = 3f; p.repairPerSecond = 18;
                p.notes = "自动修复受损最重己方设施；不修自身和同类";
            });
            Save<PropConfig>($"{Root}/Props/Prop_FlameTrap.asset", p =>
            {
                p.propId = "FlameTrap"; p.displayName = "烈焰陷阱";
                p.unlockAtSeconds = 780; p.price = 120; p.maxHealth = 160;
                p.footprintCellsX = 1f; p.footprintCellsY = 1f;
                p.trapDamage = 90; p.trapRadiusMeters = 1.5f; p.trapCooldownSeconds = 12f;
                p.trapHalvedVsBoss = true;
                p.notes = "对蛮兽减半";
            });
            Save<PropConfig>($"{Root}/Props/Prop_Demolisher.asset", p =>
            {
                p.propId = "Demolisher"; p.displayName = "破拆器";
                p.unlockAtSeconds = 300; p.price = 120; p.maxHealth = 140;
                p.footprintCellsX = 1f; p.footprintCellsY = 1f; p.maxPerPlayer = 1;
                p.isDemolisher = true;
                p.demolisherArmSeconds = 6f; p.demolisherTargetRangeMeters = 2f;
                p.demolisherBoundaryMaxMeters = 1f; p.demolisherCooldownSeconds = 20f;
                p.notes = "己方一侧、距共同边界 1 米内；防守方可摧毁（docs/06 9.2）";
            });
        }

        // docs/05 卡片列表（12 张；M1—M2 只启用 inMvpScope 六张）
        private static void GenerateCards()
        {
            Card("QuickReload", "快速装填", CardRarity.Common, CardType.Passive,
                false, true, CardEffectKind.ReloadTimePercent, -0.15f, 0, 0, 0, null,
                "换弹时间 -15%", true);
            Card("BountyHunter", "赏金猎人", CardRarity.Common, CardType.Passive,
                false, true, CardEffectKind.WeakpointKillBonusGold, 2, 0, 0, 0, null,
                "弱点击杀 +2 金币", true);
            Card("LightMaterial", "轻量建材", CardRarity.Common, CardType.Passive,
                false, true, CardEffectKind.PropPricePercent, -0.20f, 0, 0, 0, "Roadblock,FlameTrap",
                "路障和烈焰陷阱价格 -20%", true);
            Card("FieldMedic", "野战医疗", CardRarity.Common, CardType.Passive,
                false, true, CardEffectKind.HealTuning, 3, 7, 0, 0, null,
                "回血等待 4→3 秒，速度 5→7/秒", false);
            Card("ExtendedMag", "扩容弹匣", CardRarity.Rare, CardType.Passive,
                false, true, CardEffectKind.MagazineCapacityPercent, 0.30f, 0, 0, 0, null,
                "弹匣容量 +30%，向上取整", false);
            Card("EfficientBeacon", "高效拓界", CardRarity.Rare, CardType.Passive,
                false, false, CardEffectKind.BeaconPricePercent, -0.15f, 0, 0, 0, null,
                "拓界信标价格 -15%", true);
            Card("OverloadGrid", "过载电网", CardRarity.Rare, CardType.Passive,
                false, false, CardEffectKind.TurretDamageHealthTrade, 0.20f, -0.10f, 0, 0, null,
                "机枪塔伤害 +20%，自身生命 -10%", false);
            Card("Demolition", "定向爆破", CardRarity.Rare, CardType.Passive,
                false, false, CardEffectKind.DemolisherOverride, 100, 4.5f, 0, 0, null,
                "破拆器 120→100，启动 6→4.5 秒", false);
            Card("EmergencyBarrier", "紧急屏障", CardRarity.Epic, CardType.Active,
                true, true, CardEffectKind.CoreInvulnerable, 0, 0, 0, 8, null,
                "中控 8 秒免疫伤害", true);
            Card("TimeStasis", "时间静滞", CardRarity.Epic, CardType.Active,
                true, true, CardEffectKind.ZombieFreeze, 4, 2, 0, 0, null,
                "己方地盘内普通僵尸停止 4 秒，督战者 2 秒", false);
            Card("GoldenHour", "黄金时刻", CardRarity.Epic, CardType.Active,
                true, true, CardEffectKind.KillGoldBoost, 0.50f, 0, 0, 30, null,
                "30 秒内自身击杀金币 +50%", false);
            Card("EmergencyRebuild", "应急重建", CardRarity.Epic, CardType.Active,
                true, true, CardEffectKind.RepairBurst, 200, 100, 0, 0, null,
                "中控 +200、所有己方道具 +100 生命", true);
        }

        private static void Card(string id, string name, CardRarity rarity, CardType type,
            bool oneShot, bool stackable, CardEffectKind kind,
            float v1, float v2, float v3, float duration, string applies, string desc, bool mvp)
        {
            Save<CardConfig>($"{Root}/Cards/Card_{id}.asset", c =>
            {
                c.cardId = id; c.displayName = name;
                c.rarity = rarity; c.type = type;
                c.oneShotUse = oneShot; c.stackable = stackable;
                c.effectKind = kind;
                c.value1 = v1; c.value2 = v2; c.value3 = v3;
                c.durationSeconds = duration;
                c.appliesToProps = applies;
                c.description = desc;
                c.inMvpScope = mvp;
            });
        }

        // 音效资产装配（2026-11-10 音效系统）：按《音效映射表》把 wav 拖进 SfxLibrary（运行时零路径查找）
        private static void CreateSfxLibrary()
        {
            Save<SfxLibrary>($"{Root}/SfxLibrary.asset", s =>
            {
                const string wav = "Assets/资产/道具音效/";
                s.playerFire = Clip(wav + "枪/枪声-mcx20070508.wav"); // 2026-11-11 用户指定手枪音（每发一响，非整段 loop）
                s.magSnap = Clip(wav + "枪/枪声-mcx20070508.wav");
                s.hit = Clip(wav + "箭弩/箭0-mcx20070511.wav");
                s.weakpointHit = Clip(wav + "箭弩/清脆高频击中－mcx20070416.wav");
                s.zombieMeleeAttack = Clip(wav + "丧尸死亡/带人声激励的攻击zth070518.wav");
                s.acidSpit = Clip(wav + "丧尸死亡/带人声的发射武器zth070518.wav");
                s.normalDeathPool = new[]
                {
                    Clip(wav + "丧尸死亡/人型怪倒地死亡zth070522.wav"),
                    Clip(wav + "丧尸死亡/带人声的死亡倒地4zth070518.wav"),
                    Clip(wav + "丧尸死亡/带男声的死亡倒地2zth070518.wav"),
                    Clip(wav + "丧尸死亡/带男声的死亡倒地zth070518.wav"),
                    Clip(wav + "丧尸死亡/死亡倒底zth070516.wav")
                };
                s.bruteDeathPool = new[]
                {
                    Clip(wav + "丧尸死亡/壮男死亡倒地4-mcx20070517.wav"),
                    Clip(wav + "丧尸死亡/壮男死亡倒地5-mcx20070517.wav")
                };
                s.overseerDeath = Clip(wav + "丧尸死亡/大怪死亡倒地zth070522.wav");
                s.weakpointKill = Clip(wav + "丧尸死亡/带男声中刀死亡倒地zth070518.wav");
                s.turretFire = Clip(wav + "枪/中型火枪开火-YS070510.wav");
                s.fenceLoop = Clip(wav + "电塔/带有电的攻击－xh20070509.wav");
                s.coreDown = Clip(wav + "电塔/雷电-爆炸-LTT20070518.wav");
                s.coreRecover = Clip(wav + "电塔/电的魔法释放-YS070516.wav");
                s.crateOpen = Clip(wav + "打开装备箱－xh20070419.wav");
                s.playerDown = Clip(wav + "箭弩/带激光的爆炸1－xh20070510.wav");
                s.coinBig = Clip(wav + "掠夺大的硬币－xh20070419.wav");
                s.coinSmall = Clip(wav + "掠夺小的硬币－xh20070419.wav");
                s.purchaseSuccess = Clip(wav + "提示音类1-mcx20070511.wav");
                s.shopOpen = Clip(wav + "打开金币窗口－xh20070419.wav");
                s.wristMenuOpen = Clip(wav + "打开袖珍地图－xh20070419.wav"); // 菜单已撤，字段预留
                s.uiClick = Clip(wav + "按钮1——20070419.wav");
                s.grab = Clip(wav + "抢夺一空－xh20070419.wav");
                s.dock = Clip(wav + "箭弩/金属攻击-WQ20070509.wav");
                s.success = Clip(wav + "按钮2——20070419.wav");
                s.fail = Clip(wav + "提示音类2-mcx20070511.wav");
                // 背景音乐（2026-11-10）：Assets/_MoriGame/Audio 四首
                const string bgm = "Assets/_MoriGame/Audio/";
                s.bgmLoop1 = Clip(bgm + "背景1- 潜行废土.WAV");
                s.bgmLoop2 = Clip(bgm + "背景2- 未熄的营火.WAV");
                s.bgmTension = Clip(bgm + "背景3-紧张.WAV");
                s.bgmBoss = Clip(bgm + "背景4_紧张 节奏强.WAV");
                s.bgmVolume = 0.5f;
                s.bgmCrossfadeSeconds = 3f;
                s.bgmTensionChance = 0.25f;
            });
            ConfigureBgmStreaming();
        }

        /// <summary>特效库装配（2026-11-11 用户自管区，已移出 GenerateAll 批量）：29 条目映射（docs/10 权威表）；
        /// scale 全 1 由用户 Inspector 自赋值；重置映射时单独调 GenerateVfxLibrary()</summary>
        public static void GenerateVfxLibrary()
        {
            Save<MoriGame.Vfx.VfxLibrary>($"{Root}/VfxLibrary.asset", s =>
            {
                s.vfxEnabled = true; s.compressOnSpawn = false; // 总开关开+不压缩（2026-11-11 用户自管）
                const string fx = "Assets/PolygonParticles/Prefabs/";
                s.muzzleShot = Vfx(fx + "FX_Gunshot_Repeating.prefab", 1f);
                s.shellEject = Vfx(fx + "FX_Shell_Ejection_Repeating_Small_01.prefab", 1f);
                s.projectileTrail = Vfx(fx + "FX_Sparkles_Trail_01.prefab", 1f);
                s.acidProjectile = Vfx(fx + "FX_Poison_Green_01.prefab", 1f);
                s.acidImpact = Vfx(fx + "FX_ShardVine_Explosion_01.prefab", 1f);
                s.bloodHit = Vfx(fx + "FX_BloodSplat_Small_01.prefab", 1f, 10);
                s.bloodWeak = Vfx(fx + "FX_BloodSplat_01.prefab", 1f, 10);
                s.deathNormal = Vfx(fx + "FX_Explosion_Body_01.prefab", 1f, 6);
                s.deathBrute = Vfx(fx + "FX_Explosion_Body_Bloody_01.prefab", 1f, 6);
                s.goldGain = Vfx(fx + "FX_Money_Coins_Shooting_01.prefab", 1f);
                s.turretFire = Vfx(fx + "FX_Gunshot_Heavy_Repeating_01.prefab", 1f);
                s.turretHit = Vfx(fx + "FX_Impact_Small_01.prefab", 1f);
                s.fenceLoop = Vfx(fx + "FX_Electricity_01.prefab", 1f);
                s.coreDown = Vfx(fx + "FX_LightningStrike_01.prefab", 1f);
                s.coreRecover = Vfx(fx + "FX_Heal_01.prefab", 1f);
                s.smokeLight = Vfx(fx + "FX_Smoke_White_Small_01.prefab", 1f);
                s.smokeMid = Vfx(fx + "FX_Smoke_White_01.prefab", 1f);
                s.smokeHeavy = Vfx(fx + "FX_Smoke_Black_01.prefab", 1f);
                s.damageFire = Vfx(fx + "FX_Fire_Small_01.prefab", 1f);
                s.buildingDestroy = Vfx(fx + "FX_Explosion_01.prefab", 1f);
                s.crateOpen = Vfx(fx + "FX_Money_Coins_Burst_01.prefab", 1f);
                s.crateGlow = Vfx(fx + "FX_GlowSpot_01.prefab", 1f);
                s.purchaseSuccess = Vfx(fx + "FX_RainbowSparkle_01.prefab", 1f);
                s.placementDust = Vfx(fx + "FX_Dust_Small_01.prefab", 1f);
                s.playerDowned = Vfx(fx + "FX_StarStunned_01.prefab", 1f);
                s.protectionHalo = Vfx(fx + "FX_Healing_Cirle_01.prefab", 1f);
                s.teleportLanding = Vfx(fx + "FX_Portal_Round_01.prefab", 1f);
                s.medalConfetti = Vfx(fx + "FX_Confetti_Falling_01.prefab", 1f);
                s.medalFireworks = Vfx(fx + "FX_Fireworks_01.prefab", 1f);
            });
        }

        private static MoriGame.Vfx.VfxEntry Vfx(string path, float scale, int cap = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[MoriGame] 特效映射缺文件：" + path);
            }
            return new MoriGame.Vfx.VfxEntry { prefab = prefab, scale = scale, maxConcurrent = cap };
        }

        /// <summary>BGM 四首设流式加载——27MB WAV ×4 全解压约 110MB 内存，PICO 不可接受（2026-11-10）。
        /// Unity 6000.0.49 API（Cecil 实证）：AudioImporter.defaultSampleSettings.loadType</summary>
        private static void ConfigureBgmStreaming()
        {
            foreach (var name in new[]
                     {
                         "背景1- 潜行废土.WAV", "背景2- 未熄的营火.WAV", "背景3-紧张.WAV", "背景4_紧张 节奏强.WAV"
                     })
            {
                var importer = AssetImporter.GetAtPath("Assets/_MoriGame/Audio/" + name) as AudioImporter;
                if (importer == null || importer.defaultSampleSettings.loadType == AudioClipLoadType.Streaming)
                {
                    continue;
                }
                var settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.Streaming;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }

        private static AudioClip Clip(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Debug.LogError("[MoriGame] 音效映射缺文件：" + path);
            }
            return clip;
        }

        // docs/01 对局阶段表 + docs/04 波次预算表
        private static void GenerateMatchPhases()
        {
            Save<MatchPhaseConfig>($"{Root}/MatchPhases.asset", m =>
            {
                m.matchDurationSeconds = 480;
                m.budgetIntervalSeconds = 30;
                m.initialHealth = 100;
                m.initialGold = 300; // 经济标定 v2：与 PlayerConfig.startingGold 同步
                m.maxActiveZombiesPerPlayer = 20; // 2026-11-10 三次调压：同屏上限 30→20（用户实测打不过来）
                m.maxActiveZombiesTotal = 60;
                m.maxBudgetCarryRounds = 2;
                m.phaseSwitchSlowWindowSeconds = 10f;
                m.phaseSwitchSlowFactor = 0.5f;

                // 2026-11-13：原 20 分钟时间轴整体 ×0.4，准备期 12 秒，08:00 结算；经济与拍波参数不变。
                // 2026-11-09 拍波制：首拍=入场数（5/10/13/16/20），逐拍+1 到阶段封顶（15/15/15/20/20）
                // 2026-11-10 四次调压：拍距 12 秒、类型占比权重化、P5 同屏 25（其余 20）
                // 预算按拍波计划配足（12s 拍距）：275×1、285×1.7、297×2.1、390×2.55、300×3.4 → 31/61/78/125/170
                m.phases = new List<MatchPhaseDef>
                {
                    new MatchPhaseDef { phaseId = "Preparation", displayName = "落地准备",
                        startSeconds = 0, endSeconds = 12, threatBudgetPerPlayer = 0, initialAliveCount = 0, waveSizeCap = 15, aliveCap = 20,
                        allowedZombieIds = new List<string>(), phaseArrivalGoldReward = 0 },
                    new MatchPhaseDef { phaseId = "Phase1", displayName = "感染扩散",
                        startSeconds = 12, endSeconds = 120, threatBudgetPerPlayer = 31, initialAliveCount = 5, waveSizeCap = 15, aliveCap = 20,
                        allowedZombieIds = new List<string> { "Normal" }, typeWeights = new List<float> { 100f }, phaseArrivalGoldReward = 300 },
                    new MatchPhaseDef { phaseId = "Phase2", displayName = "快速威胁",
                        startSeconds = 120, endSeconds = 216, threatBudgetPerPlayer = 61, initialAliveCount = 10, waveSizeCap = 15, aliveCap = 20,
                        allowedZombieIds = new List<string> { "Normal", "Runner", "Spitter" },
                        typeWeights = new List<float> { 50f, 30f, 20f }, phaseArrivalGoldReward = 300 },
                    new MatchPhaseDef { phaseId = "Phase3", displayName = "装甲推进",
                        startSeconds = 216, endSeconds = 312, threatBudgetPerPlayer = 78, initialAliveCount = 13, waveSizeCap = 15, aliveCap = 20,
                        allowedZombieIds = new List<string> { "Normal", "Runner", "Spitter", "ShieldGuard" },
                        typeWeights = new List<float> { 40f, 30f, 20f, 10f }, phaseArrivalGoldReward = 300 },
                    new MatchPhaseDef { phaseId = "Phase4", displayName = "重装尸潮",
                        startSeconds = 312, endSeconds = 408, threatBudgetPerPlayer = 125, initialAliveCount = 16, waveSizeCap = 20, aliveCap = 20,
                        allowedZombieIds = new List<string> { "Normal", "Runner", "Spitter", "ShieldGuard", "Brute" },
                        typeWeights = new List<float> { 35f, 25f, 20f, 10f, 10f }, phaseArrivalGoldReward = 300 },
                    new MatchPhaseDef { phaseId = "FinalPhase", displayName = "末日狂潮",
                        startSeconds = 408, endSeconds = 480, threatBudgetPerPlayer = 170, initialAliveCount = 20, waveSizeCap = 20, aliveCap = 25,
                        allowedZombieIds = new List<string> { "Normal", "Runner", "Spitter", "ShieldGuard", "Brute", "Overseer" },
                        typeWeights = new List<float> { 20f, 20f, 20f, 20f, 20f }, phaseArrivalGoldReward = 300 }
                };

                m.specialEvents = new List<SpecialEventDef>
                {
                    new SpecialEventDef { atSeconds = 432, eventId = "SpawnOverseer", parameter = "Overseer" }
                };
            });
        }
    }
}
