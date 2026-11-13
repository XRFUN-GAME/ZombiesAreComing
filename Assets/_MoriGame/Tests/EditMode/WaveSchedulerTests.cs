using System.Collections.Generic;
using System.Reflection;
using MoriGame.Configs;
using MoriGame.Waves;
using MoriGame.Zombies;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M2-T1 波次导演纯逻辑测试（数值镜像 MatchPhases 资产 / docs/01、04）：
    /// 12 秒准备期不刷怪、每 30 秒预算发放与消耗、累积上限两轮、五阶段切换时点与到达奖励 300×5、
    /// 切换前 10 秒减速窗、07:12 督战者事件、08:00 结束。</summary>
    public class WaveSchedulerTests
    {
        private static WaveScheduler BuildScheduler()
        {
            var phases = new List<PhaseSpec>
            {
                new PhaseSpec { phaseId = "Preparation", displayName = "落地准备",
                    startSeconds = 0, endSeconds = 12, threatBudgetPerInterval = 0, arrivalGoldReward = 0 },
                new PhaseSpec { phaseId = "Phase1", displayName = "感染扩散",
                    startSeconds = 12, endSeconds = 120, threatBudgetPerInterval = 31, arrivalGoldReward = 300 },
                new PhaseSpec { phaseId = "Phase2", displayName = "快速威胁",
                    startSeconds = 120, endSeconds = 216, threatBudgetPerInterval = 61, arrivalGoldReward = 300 },
                new PhaseSpec { phaseId = "Phase3", displayName = "装甲推进",
                    startSeconds = 216, endSeconds = 312, threatBudgetPerInterval = 78, arrivalGoldReward = 300 },
                new PhaseSpec { phaseId = "Phase4", displayName = "重装尸潮",
                    startSeconds = 312, endSeconds = 408, threatBudgetPerInterval = 125, arrivalGoldReward = 300 },
                new PhaseSpec { phaseId = "FinalPhase", displayName = "末日狂潮",
                    startSeconds = 408, endSeconds = 480, threatBudgetPerInterval = 170, arrivalGoldReward = 300 }
            };
            var events = new List<SpecialEventSpec>
            {
                new SpecialEventSpec { atSeconds = 432, eventId = "SpawnOverseer", parameter = "Overseer" }
            };
            return new WaveScheduler(phases, 30f, 480f, 2, 10f, events);
        }

        private static void TickBy(WaveScheduler scheduler, float seconds, float step = 1f)
        {
            for (var t = 0f; t < seconds; t += step)
            {
                scheduler.Tick(step);
            }
        }

        [Test]
        public void 准备期不发放预算也不发奖励()
        {
            var scheduler = BuildScheduler();
            PhaseSpec entered = null;
            var rewardTotal = 0;
            scheduler.PhaseEntered += p =>
            {
                entered = p;
                rewardTotal += p.arrivalGoldReward;
            };

            TickBy(scheduler, 11f); // 准备期 00:00—00:12（不含切换那一拍）

            Assert.AreEqual("Preparation", entered.phaseId);
            Assert.AreEqual(0, rewardTotal);
            Assert.AreEqual(0, scheduler.CarriedBudget);
            Assert.IsFalse(scheduler.Ended);
        }

        [Test]
        public void 阶段切换十二秒进入第一阶段并发奖励300()
        {
            var scheduler = BuildScheduler();
            var rewards = new List<PhaseSpec>();
            scheduler.PhaseEntered += rewards.Add;

            TickBy(scheduler, 11f);
            Assert.AreEqual(1, rewards.Count); // 只有 Preparation

            scheduler.Tick(1f); // 00:12 整
            Assert.AreEqual(2, rewards.Count);
            Assert.AreEqual("Phase1", rewards[1].phaseId);
            Assert.AreEqual(300, rewards[1].arrivalGoldReward); // 阶段到达奖励（经济 v2）
        }

        [Test]
        public void 阶段入口立即发放首批预算并可全部消耗()
        {
            var scheduler = BuildScheduler();
            TickBy(scheduler, 13f); // 00:12 过 1 秒

            Assert.AreEqual(31, scheduler.CarriedBudget); // 感染扩散入口第一批（2026-11-10 拍距 12s 版）
            for (var i = 0; i < 31; i++)
            {
                Assert.IsTrue(scheduler.TrySpend(1), "第 " + (i + 1) + " 只应可生成");
            }
            Assert.IsFalse(scheduler.TrySpend(1)); // 预算耗尽
            Assert.AreEqual(0, scheduler.CarriedBudget);
        }

        [Test]
        public void 未消耗预算累积最多两轮()
        {
            var scheduler = BuildScheduler();
            TickBy(scheduler, 73f); // 12/42/72 三批已达两轮累积上限

            Assert.AreEqual(93, scheduler.CarriedBudget); // 31 × (1 + 积两轮) 封顶

            TickBy(scheduler, 30f); // 102 再来一批，仍在第一阶段
            Assert.AreEqual(93, scheduler.CarriedBudget); // 封顶不涨
        }

        [Test]
        public void 两分钟整进入第二阶段奖励照发且对局继续()
        {
            var scheduler = BuildScheduler();
            PhaseSpec lastEntered = null;
            scheduler.PhaseEntered += p => lastEntered = p;

            TickBy(scheduler, 119f);
            Assert.IsFalse(scheduler.Ended);
            Assert.AreEqual("Phase1", lastEntered.phaseId);

            scheduler.Tick(1f); // 02:00 整
            Assert.AreEqual("Phase2", lastEntered.phaseId);
            Assert.AreEqual(300, lastEntered.arrivalGoldReward);
            Assert.IsFalse(scheduler.Ended); // 02:00 进入第二阶段，整局 08:00 结束
        }

        [Test]
        public void 八分钟整对局结束且结束后不可再生成()
        {
            var scheduler = BuildScheduler();
            PhaseSpec lastEntered = null;
            var endedCount = 0;
            scheduler.PhaseEntered += p => lastEntered = p;
            scheduler.MatchEnded += () => endedCount++;

            TickBy(scheduler, 479f);
            Assert.IsFalse(scheduler.Ended);
            Assert.AreEqual("FinalPhase", lastEntered.phaseId);
            Assert.AreEqual(1f, scheduler.RemainingSeconds);

            scheduler.Tick(1f); // 08:00 整
            Assert.IsTrue(scheduler.Ended);
            Assert.AreEqual(1, endedCount);
            Assert.AreEqual(0f, scheduler.RemainingSeconds);
            Assert.IsFalse(scheduler.TrySpend(1)); // 结束后不可再生成

            scheduler.Tick(1f); // 结束后再 tick 不重复触发
            Assert.AreEqual(1, endedCount);
        }

        [Test]
        public void 终局清场_活跃僵尸回池且待生成请求清空()
        {
            const BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
            var poolGo = new GameObject("EndMatchPool");
            var directorGo = new GameObject("EndMatchDirector");
            try
            {
                var pool = poolGo.AddComponent<ZombiePool>();
                var director = directorGo.AddComponent<WaveDirector>();
                var active = (List<ZombieAgent>)typeof(ZombiePool).GetField("active", instancePrivate).GetValue(pool);
                var inactive = (List<ZombieAgent>)typeof(ZombiePool).GetField("inactive", instancePrivate).GetValue(pool);
                var first = new GameObject("ActiveNormal");
                var second = new GameObject("ActiveBrute");
                first.transform.SetParent(poolGo.transform, false);
                second.transform.SetParent(poolGo.transform, false);
                active.Add(first.AddComponent<ZombieAgent>());
                active.Add(second.AddComponent<ZombieAgent>());
                var queue = (List<string>)typeof(WaveDirector).GetField("spawnQueue", instancePrivate).GetValue(director);
                queue.Add("Normal");
                queue.Add("Overseer");
                typeof(WaveDirector).GetField("pool", instancePrivate).SetValue(director, pool);
                typeof(WaveDirector).GetField("pendingOverseer", instancePrivate).SetValue(director, true);

                var clear = typeof(WaveDirector).GetMethod("ClearRemainingZombies", instancePrivate);
                Assert.NotNull(clear);
                clear.Invoke(director, null);

                Assert.AreEqual(0, pool.ActiveCount);
                Assert.AreEqual(2, inactive.Count, "清场只回池，不走击杀结算");
                Assert.IsFalse(first.activeSelf);
                Assert.IsFalse(second.activeSelf);
                Assert.AreEqual(0, queue.Count, "结算后不得再激活已排队的僵尸");
                Assert.IsFalse((bool)typeof(WaveDirector).GetField("pendingOverseer", instancePrivate).GetValue(director));
            }
            finally
            {
                Object.DestroyImmediate(directorGo);
                Object.DestroyImmediate(poolGo);
            }
        }

        [Test]
        public void 全程五次阶段奖励合计1500()
        {
            var scheduler = BuildScheduler();
            var rewardTotal = 0;
            scheduler.PhaseEntered += p => rewardTotal += p.arrivalGoldReward;

            TickBy(scheduler, 480f);

            Assert.AreEqual(1500, rewardTotal); // 准备 0 + 五阶段 ×300（经济 v2）
            Assert.IsTrue(scheduler.Ended);
        }

        [Test]
        public void 切换前十秒为减速窗()
        {
            var scheduler = BuildScheduler();
            TickBy(scheduler, 1f); // 距 00:12 切换 11 秒
            Assert.IsFalse(scheduler.InSlowWindow);

            TickBy(scheduler, 1f); // 00:02，恰 10 秒整进窗
            Assert.IsTrue(scheduler.InSlowWindow);

            TickBy(scheduler, 108f); // 到 01:50，距 02:00 切换 10 秒整
            Assert.IsTrue(scheduler.InSlowWindow);

            TickBy(scheduler, 9f); // 01:59 仍在窗
            Assert.IsTrue(scheduler.InSlowWindow);
        }

        [Test]
        public void 切换后不再是减速窗()
        {
            var scheduler = BuildScheduler();
            TickBy(scheduler, 13f); // 00:13：准备阶段切换刚过
            Assert.IsFalse(scheduler.InSlowWindow);

            TickBy(scheduler, 112f); // 02:05：第二阶段切换刚过
            Assert.IsFalse(scheduler.InSlowWindow);
        }

        [Test]
        public void 末日狂潮无下一阶段不产生减速窗()
        {
            var scheduler = BuildScheduler();
            TickBy(scheduler, 475f); // 07:55：08:00 是对局结束而非阶段切换
            Assert.IsFalse(scheduler.InSlowWindow);
        }

        [Test]
        public void 七分十二秒督战者事件恰好触发一次()
        {
            var scheduler = BuildScheduler();
            var fired = new List<SpecialEventSpec>();
            scheduler.SpecialEventReached += fired.Add;

            TickBy(scheduler, 431f); // 07:11
            Assert.AreEqual(0, fired.Count);

            scheduler.Tick(1f); // 07:12 整
            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual("SpawnOverseer", fired[0].eventId);

            TickBy(scheduler, 48f); // 走到结束，不重复触发
            Assert.AreEqual(1, fired.Count);
        }

        [Test]
        public void 波次只从预算够付的允许类型中选择()
        {
            var options = new List<ZombieSpawnOption>
            {
                new ZombieSpawnOption("Normal", 1),
                new ZombieSpawnOption("Runner", 2),
                new ZombieSpawnOption("Spitter", 3),
                new ZombieSpawnOption("Brute", 6)
            };
            Assert.AreEqual(0, WaveComposition.SelectAffordable(options, 1, 0f));
            Assert.AreEqual(-1, WaveComposition.SelectAffordable(options, 0, 0f));
        }

        [Test]
        public void 波次同预算可混选普通疾行喷吐()
        {
            var options = new List<ZombieSpawnOption>
            {
                new ZombieSpawnOption("Normal", 1),
                new ZombieSpawnOption("Runner", 2),
                new ZombieSpawnOption("Spitter", 3)
            };
            var selected = new HashSet<string>();
            for (var roll = 0; roll < 30; roll++)
            {
                var index = WaveComposition.SelectAffordable(options, 3, roll / 30f);
                Assert.GreaterOrEqual(index, 0);
                Assert.LessOrEqual(options[index].ThreatPoints, 3);
                selected.Add(options[index].ZombieId);
            }
            CollectionAssert.AreEquivalent(new[] { "Normal", "Runner", "Spitter" }, selected);
        }

        [Test]
        public void 类型占比加权选择_按用户配比分布_20261110()
        {
            // 第二阶段配比：普通 50 / 疾行 30 / 喷吐 20
            var options = new List<ZombieSpawnOption>
            {
                new ZombieSpawnOption("Normal", 1, 50f),
                new ZombieSpawnOption("Runner", 2, 30f),
                new ZombieSpawnOption("Spitter", 3, 20f)
            };
            Assert.AreEqual(0, WaveComposition.SelectAffordable(options, 3, 0.49f), "前 50% 落普通");
            Assert.AreEqual(1, WaveComposition.SelectAffordable(options, 3, 0.51f), "50%—80% 落疾行");
            Assert.AreEqual(2, WaveComposition.SelectAffordable(options, 3, 0.85f), "80%—100% 落喷吐");
            // 第三阶段配比 40/30/20/10：累积界 0.4 / 0.7 / 0.9
            var p3 = new List<ZombieSpawnOption>
            {
                new ZombieSpawnOption("Normal", 1, 40f),
                new ZombieSpawnOption("Runner", 2, 30f),
                new ZombieSpawnOption("Spitter", 3, 20f),
                new ZombieSpawnOption("ShieldGuard", 5, 10f)
            };
            Assert.AreEqual(0, WaveComposition.SelectAffordable(p3, 5, 0.39f));
            Assert.AreEqual(1, WaveComposition.SelectAffordable(p3, 5, 0.45f));
            Assert.AreEqual(2, WaveComposition.SelectAffordable(p3, 5, 0.75f));
            Assert.AreEqual(3, WaveComposition.SelectAffordable(p3, 5, 0.95f));
            // 权重全零退化为等概率兜底
            var zero = new List<ZombieSpawnOption>
            {
                new ZombieSpawnOption("Normal", 1, 0f),
                new ZombieSpawnOption("Runner", 2, 0f)
            };
            Assert.AreEqual(0, WaveComposition.SelectAffordable(zero, 3, 0f));
            Assert.AreEqual(1, WaveComposition.SelectAffordable(zero, 3, 0.9f));
        }

        [Test]
        public void 阶段资产使用八分钟压缩时间轴()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchPhaseConfig>(
                "Assets/_MoriGame/Configs/MatchPhases.asset");
            Assert.NotNull(config);
            Assert.AreEqual(480, config.matchDurationSeconds);
            CollectionAssert.AreEqual(new[] { 0, 12, 120, 216, 312, 408 },
                config.phases.ConvertAll(p => p.startSeconds));
            CollectionAssert.AreEqual(new[] { 12, 120, 216, 312, 408, 480 },
                config.phases.ConvertAll(p => p.endSeconds));
            var overseer = config.specialEvents.Find(e => e.eventId == "SpawnOverseer");
            Assert.NotNull(overseer);
            Assert.AreEqual(432, overseer.atSeconds);
        }

        [Test]
        public void 阶段资产按阶段解锁新类型()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchPhaseConfig>(
                "Assets/_MoriGame/Configs/MatchPhases.asset");
            Assert.NotNull(config);
            CollectionAssert.AreEqual(new[] { "Normal" }, config.phases[1].allowedZombieIds);
            CollectionAssert.AreEqual(new[] { "Normal", "Runner", "Spitter" }, config.phases[2].allowedZombieIds);
            Assert.Contains("ShieldGuard", config.phases[3].allowedZombieIds);
            Assert.Contains("Brute", config.phases[4].allowedZombieIds);
            Assert.Contains("Overseer", config.phases[5].allowedZombieIds);
        }

        [Test]
        public void 拍波递增_首拍入场数_逐拍加一_封顶15与20_20261110()
        {
            // 前三阶段封顶 15（2026-11-10 三次调压：前期压力再降）
            Assert.AreEqual(5, WaveComposition.WaveSize(5, 0, 15), "感染扩散首拍 5 只");
            Assert.AreEqual(6, WaveComposition.WaveSize(5, 1, 15), "第二拍 6 只");
            Assert.AreEqual(13, WaveComposition.WaveSize(5, 8, 15), "逐拍 +1");
            Assert.AreEqual(15, WaveComposition.WaveSize(5, 10, 15), "第 11 拍封顶 15");
            Assert.AreEqual(15, WaveComposition.WaveSize(5, 60, 15), "之后恒 15");
            Assert.AreEqual(10, WaveComposition.WaveSize(10, 0, 15), "快速威胁首拍 10 只");
            Assert.AreEqual(15, WaveComposition.WaveSize(10, 5, 15), "二阶段中途封顶");
            Assert.AreEqual(13, WaveComposition.WaveSize(13, 0, 15), "装甲推进首拍 13 只");
            Assert.AreEqual(15, WaveComposition.WaveSize(13, 2, 15), "三阶段中途封顶");
            // 后两阶段封顶 20
            Assert.AreEqual(16, WaveComposition.WaveSize(16, 0, 20), "重装尸潮首拍 16 只");
            Assert.AreEqual(20, WaveComposition.WaveSize(16, 4, 20), "四阶段封顶 20");
            Assert.AreEqual(20, WaveComposition.WaveSize(20, 0, 20), "末日狂潮首拍=封顶 20 只");
            Assert.AreEqual(5, WaveComposition.WaveSize(5, -1, 15), "非法拍序兜底为首拍");
        }

        [Test]
        public void 阶段入场存活阶梯_二阶段为10_换阶段递增_20261110()
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<MatchPhaseConfig>(
                "Assets/_MoriGame/Configs/MatchPhases.asset");
            Assert.NotNull(config);
            Assert.AreEqual(0, config.phases[0].initialAliveCount, "准备期不刷怪");
            var ladder = new[] { 5, 10, 13, 16, 20 };
            for (var i = 1; i <= 5; i++)
            {
                Assert.AreEqual(ladder[i - 1], config.phases[i].initialAliveCount,
                    $"第 {i} 战斗阶段入场存活应为 {ladder[i - 1]}");
                if (i > 1)
                {
                    Assert.Greater(config.phases[i].initialAliveCount, config.phases[i - 1].initialAliveCount,
                        "每阶段起点必须比上一阶段多");
                }
            }
            Assert.AreEqual(20, config.maxActiveZombiesPerPlayer, "同屏最大可同时出现 20 只（全局默认，P5 由 aliveCap=25 覆盖）");
            // 2026-11-10 拍距 12s + 加权占比版：预算按拍波计划配足
            // （Σ(入场..cap)×加权均价 / 30 秒窗口 = 275×1、285×1.7、297×2.1、390×2.55、300×3.4）
            var budgets = new[] { 31, 61, 78, 125, 170 };
            var caps = new[] { 15, 15, 15, 20, 20 };
            var aliveCaps = new[] { 20, 20, 20, 20, 25 };
            var weightSets = new[]
            {
                new[] { 50f, 30f, 20f },
                new[] { 40f, 30f, 20f, 10f },
                new[] { 35f, 25f, 20f, 10f, 10f },
                new[] { 20f, 20f, 20f, 20f, 20f }
            };
            for (var i = 1; i <= 5; i++)
            {
                Assert.AreEqual(budgets[i - 1], config.phases[i].threatBudgetPerPlayer,
                    $"第 {i} 战斗阶段每 30 秒预算应为 {budgets[i - 1]}");
                Assert.AreEqual(caps[i - 1], config.phases[i].waveSizeCap,
                    $"第 {i} 战斗阶段拍波封顶应为 {caps[i - 1]}");
                Assert.AreEqual(aliveCaps[i - 1], config.phases[i].aliveCap,
                    $"第 {i} 战斗阶段同屏上限应为 {aliveCaps[i - 1]}");
            }
            Assert.AreEqual(1, config.phases[1].typeWeights.Count, "一阶段仅普通，权重表占位");
            for (var i = 2; i <= 5; i++)
            {
                CollectionAssert.AreEqual(weightSets[i - 2], config.phases[i].typeWeights,
                    $"第 {i} 战斗阶段类型占比权重应为用户配比表");
            }
        }
    }
}
