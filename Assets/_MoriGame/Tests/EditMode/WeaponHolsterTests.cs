using System.Reflection;
using MoriGame.Combat;
using MoriGame.Configs;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    public class WeaponHolsterTests
    {
        private GameObject root;
        private Transform hand;
        private Transform slot;
        private WeaponController weapon;
        private WeaponHolster holster;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("HolsterTest");
            hand = new GameObject("Hand").transform;
            hand.SetParent(root.transform);
            slot = new GameObject("Slot").transform;
            slot.SetParent(root.transform);

            var gun = new GameObject("Pistol");
            gun.transform.SetParent(hand, false);
            gun.transform.localPosition = new Vector3(0.01f, 0.02f, 0.03f);
            gun.transform.localRotation = Quaternion.Euler(5f, 10f, 15f);
            var visual = new GameObject("Visual");
            visual.transform.SetParent(gun.transform, false);
            gun.SetActive(false);
            weapon = gun.AddComponent<WeaponController>();
            weapon.config = ScriptableObject.CreateInstance<WeaponConfig>();
            weapon.visualRoot = visual;
            gun.SetActive(true);
            Invoke(weapon, "Awake");

            holster = root.AddComponent<WeaponHolster>();
            holster.weapon = weapon;
            holster.slotAnchor = slot;
            Invoke(holster, "Start");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void 抓起即装备_真枪恢复手腕局部位姿()
        {
            Invoke(holster, "GrabIt");
            Assert.IsTrue(holster.Held, "抓起后枪套应进入持枪状态");
            Assert.IsFalse(weapon.Holstered, "抓起后应解除插槽锁火");
            Assert.IsTrue(weapon.Deployed, "抓起后应立即部署（WeaponController.Awake 应已初始化部署状态机）");
            Assert.AreSame(hand, weapon.transform.parent);
            Assert.AreEqual(new Vector3(0.01f, 0.02f, 0.03f), weapon.transform.localPosition);
            Assert.AreEqual(Quaternion.Euler(5f, 10f, 15f), weapon.transform.localRotation);
        }

        [Test]
        public void 放回插槽_真枪复位并锁火()
        {
            Invoke(holster, "GrabIt");
            holster.ReturnToSlot();
            Assert.IsFalse(holster.Held);
            Assert.IsTrue(weapon.Holstered);
            Assert.IsFalse(weapon.Deployed);
            Assert.AreSame(slot, weapon.transform.parent);
            Assert.AreEqual(Vector3.zero, weapon.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, weapon.transform.localRotation);
        }

        private static void Invoke(object target, string method)
        {
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }
    }
}
