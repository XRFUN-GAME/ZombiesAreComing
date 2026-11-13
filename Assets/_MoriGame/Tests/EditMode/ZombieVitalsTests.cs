using System.Collections.Generic;
using System.Reflection;
using MoriGame.Configs;
using MoriGame.Economy;
using MoriGame.Zombies;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>
    /// M1-T5 普通感染者生命体（docs/04：60 生命 / 弱点 2 倍 / 击杀 4 金币；
    /// docs/03 手枪行：普通僵尸身体 3 发、弱点 2 发）。
    /// </summary>
    public class ZombieVitalsTests
    {
        private static ZombieVitals NewNormal()
        {
            return new ZombieVitals(60, 2f, 4, 1);
        }

        [Test]
        public void 手枪身体三发击杀()
        {
            var z = NewNormal();
            Assert.AreEqual(24, z.TakeDamage(24, false));
            Assert.IsTrue(z.IsAlive);
            Assert.AreEqual(24, z.TakeDamage(24, false));
            Assert.IsTrue(z.IsAlive); // 60-48=12
            Assert.AreEqual(24, z.TakeDamage(24, false)); // 实扣 24（全额扣减，Hp 截到 0）
            Assert.IsFalse(z.IsAlive);
        }

        [Test]
        public void 手枪弱点两发击杀_倍率2()
        {
            var z = NewNormal();
            Assert.AreEqual(48, z.TakeDamage(24, true));
            Assert.IsTrue(z.IsAlive); // 60-48=12
            Assert.AreEqual(48, z.TakeDamage(24, true)); // 实扣 48（全额扣减，Hp 截到 0）
            Assert.IsFalse(z.IsAlive);
        }

        [Test]
        public void 死亡事件恰好触发一次_过量伤害不再结算()
        {
            var z = NewNormal();
            var diedCount = 0;
            z.Died += () => diedCount++;
            z.TakeDamage(60, false);
            Assert.AreEqual(1, diedCount);
            Assert.AreEqual(0, z.TakeDamage(24, false));
            Assert.AreEqual(1, diedCount);
        }

        [Test]
        public void 击杀金币入钱包_事件接线契约()
        {
            var z = NewNormal();
            var wallet = new PlayerWallet(100);
            z.Died += () => wallet.Add(z.KillGold);
            z.TakeDamage(60, false);
            Assert.AreEqual(104, wallet.Gold); // 单人独占击杀收益（docs/04 6.4）
        }

        [Test]
        public void 已死目标弱点倍率不生效()
        {
            var z = NewNormal();
            z.TakeDamage(60, false);
            Assert.AreEqual(0, z.TakeDamage(24, true));
        }

        [Test]
        public void 盾卫正面伤害先扣120盾()
        {
            var z = new ZombieVitals(180, 2f, 120, 5, 120);
            Assert.AreEqual(0, z.TakeDamage(100, false, true, false));
            Assert.AreEqual(20, z.FrontShieldHp);
            Assert.AreEqual(180, z.Hp);
        }

        [Test]
        public void 盾卫血条包含护盾且本体死亡即空()
        {
            var z = new ZombieVitals(180, 2f, 120, 5, 120);
            Assert.AreEqual(1f, z.HealthBarFraction, 0.0001f);

            z.TakeDamage(100, false, true, false);
            Assert.AreEqual(200f / 300f, z.HealthBarFraction, 0.0001f,
                "正面只扣护盾时血条也必须下降");

            z.TakeDamage(180, false, false, false);
            Assert.IsFalse(z.IsAlive);
            Assert.AreEqual(20, z.FrontShieldHp, "绕盾击杀后允许护盾尚有剩余");
            Assert.AreEqual(0f, z.HealthBarFraction, 0.0001f, "本体死亡时不得因剩余护盾显示残血");
        }

        [Test]
        public void 盾卫盾耗尽后溢出伤害进入生命()
        {
            var z = new ZombieVitals(180, 2f, 120, 5, 120);
            z.TakeDamage(100, false, true, false);
            Assert.AreEqual(30, z.TakeDamage(50, false, true, false));
            Assert.AreEqual(0, z.FrontShieldHp);
            Assert.AreEqual(150, z.Hp);
        }

        [Test]
        public void 盾卫侧后与腿部绕开盾()
        {
            var z = new ZombieVitals(180, 2f, 120, 5, 120);
            Assert.AreEqual(30, z.TakeDamage(30, false, false, false));
            Assert.AreEqual(20, z.TakeDamage(20, false, true, true));
            Assert.AreEqual(130, z.Hp);
            Assert.AreEqual(120, z.FrontShieldHp);
        }

        [Test]
        public void 盾卫头部弱点绕盾并吃倍率()
        {
            var z = new ZombieVitals(180, 2f, 120, 5, 120);
            Assert.AreEqual(48, z.TakeDamage(24, true, true, false));
            Assert.AreEqual(132, z.Hp);
            Assert.AreEqual(120, z.FrontShieldHp);
        }

        [Test]
        public void 疾行者连续三击硬直半秒()
        {
            var stagger = new HitStaggerState(3, 0.5f, 1.5f);
            Assert.IsFalse(stagger.RegisterHit());
            Assert.IsFalse(stagger.RegisterHit());
            Assert.IsTrue(stagger.RegisterHit());
            Assert.AreEqual(0.5f, stagger.Remaining);
            stagger.Tick(0.25f);
            Assert.AreEqual(0.25f, stagger.Remaining, 0.0001f);
            stagger.Tick(0.25f);
            Assert.AreEqual(0f, stagger.Remaining);
        }

        [Test]
        public void 疾行者连击超时重新计数()
        {
            var stagger = new HitStaggerState(3, 0.5f, 1.5f);
            stagger.RegisterHit();
            stagger.RegisterHit();
            stagger.Tick(1.6f);
            Assert.AreEqual(0, stagger.ConsecutiveHits);
            Assert.IsFalse(stagger.RegisterHit());
            Assert.AreEqual(1, stagger.ConsecutiveHits);
        }

        [Test]
        public void 模型脚底对齐根点并叠加手动补偿()
        {
            var root = new GameObject("Agent");
            var visual = new GameObject("Model");
            visual.transform.SetParent(root.transform, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(visual.transform, false);
            cube.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            root.transform.position = new Vector3(0f, 2f, 0f);
            try
            {
                Assert.AreEqual(0.95f,
                    ZombiePool.CalculateFootLocalY(visual.transform, root.transform, 0.05f), 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void 路径两米判定使用点到线段距离()
        {
            var corners = new[] { new UnityEngine.Vector3(0f, 0f, 0f), new UnityEngine.Vector3(10f, 0f, 0f) };
            Assert.AreEqual(2f, ZombieAgent.DistanceToPath(new UnityEngine.Vector3(5f, 0f, 2f), corners), 0.0001f);
            Assert.Greater(ZombieAgent.DistanceToPath(new UnityEngine.Vector3(5f, 0f, 2.01f), corners), 2f);
            Assert.AreEqual(3f, ZombieAgent.DistanceToPath(new UnityEngine.Vector3(13f, 0f, 0f), corners), 0.0001f);
        }

        [TestCase("Normal", true)]
        [TestCase("Runner", false)]
        [TestCase("Spitter", false)]
        [TestCase("ShieldGuard", false)]
        [TestCase("Brute", false)]
        [TestCase("Overseer", false)]
        public void 非致命受击动画_仅普通僵尸触发(string zombieId, bool expectDamageTrigger)
        {
            var controllerPath = "Assets/_MoriGame/Tests/EditMode/ZombieHitReaction_" +
                                 System.Guid.NewGuid().ToString("N") + ".controller";
            var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Damage", AnimatorControllerParameterType.Trigger);
            var zombie = new GameObject("HitReaction_" + zombieId);
            var config = ScriptableObject.CreateInstance<ZombieConfig>();
            try
            {
                var visual = new GameObject("Model");
                visual.transform.SetParent(zombie.transform, false);
                var animator = visual.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Update(0f);
                var agent = zombie.AddComponent<ZombieAgent>();
                agent.animator = animator;
                config.zombieId = zombieId;
                agent.config = config;
                typeof(ZombieAgent).GetField("<Vitals>k__BackingField",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(agent, new ZombieVitals(100, 2f, 4, 1));

                Assert.IsFalse(agent.ApplyHit(1, false), "测试命中必须是非致命命中");
                Assert.AreEqual(expectDamageTrigger, animator.GetBool("Damage"), zombieId);
            }
            finally
            {
                Object.DestroyImmediate(zombie);
                Object.DestroyImmediate(config);
                UnityEditor.AssetDatabase.DeleteAsset(controllerPath);
            }
        }

        [Test]
        public void 教学靶死亡倒计时优先于站桩_到期回池()
        {
            var poolGo = new GameObject("DummyPool");
            var zombieGo = new GameObject("DeadDummy");
            try
            {
                var pool = poolGo.AddComponent<ZombiePool>();
                var zombie = zombieGo.AddComponent<ZombieAgent>();
                var vitals = new ZombieVitals(1, 2f, 0, 0);
                vitals.TakeDamage(1, false);
                const BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(ZombieAgent).GetField("<Vitals>k__BackingField", instancePrivate).SetValue(zombie, vitals);
                typeof(ZombieAgent).GetField("pool", instancePrivate).SetValue(zombie, pool);
                typeof(ZombieAgent).GetField("deathRemaining", instancePrivate).SetValue(zombie, 0f);
                var active = (List<ZombieAgent>)typeof(ZombiePool)
                    .GetField("active", instancePrivate).GetValue(pool);
                active.Add(zombie);
                zombie.dummyMode = true;

                typeof(ZombieAgent).GetMethod("Update", instancePrivate).Invoke(zombie, null);

                Assert.AreEqual(0, pool.ActiveCount, "站桩分支不得阻断死亡回收");
                Assert.IsFalse(zombieGo.activeSelf, "回池后隐藏尸体，TutorialFlow 才会补足该槽位");
            }
            finally
            {
                Object.DestroyImmediate(zombieGo);
                Object.DestroyImmediate(poolGo);
            }
        }
    }
}
