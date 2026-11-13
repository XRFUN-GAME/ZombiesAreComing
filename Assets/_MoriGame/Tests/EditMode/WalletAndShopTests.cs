using MoriGame.Configs;
using MoriGame.Economy;
using MoriGame.Shop;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    // docs/09 经济规则：金币增减、购买（docs/05 7.1/7.3 + docs/06 上限）
    public class WalletTests
    {
        [Test]
        public void 开局100金币_够钱扣钱_不够拒绝()
        {
            var wallet = new PlayerWallet(100);
            Assert.IsTrue(wallet.TrySpend(70));
            Assert.AreEqual(30, wallet.Gold);
            Assert.IsFalse(wallet.TrySpend(31));
            Assert.AreEqual(30, wallet.Gold);
        }

        [Test]
        public void 倒地罚金_百分比封顶60()
        {
            var wallet = new PlayerWallet(100);
            Assert.AreEqual(15, wallet.ApplyDownedPenalty(15f, 60));
            Assert.AreEqual(85, wallet.Gold);

            wallet.Add(900); // 985 × 15% = 147.75 → 封顶 60
            Assert.AreEqual(60, wallet.ApplyDownedPenalty(15f, 60));
            Assert.AreEqual(925, wallet.Gold);
        }

        [Test]
        public void 瘫痪罚金_最多800_不足扣到0()
        {
            var wallet = new PlayerWallet(1000);
            Assert.AreEqual(800, wallet.ApplyParalyzePenalty(800));
            Assert.AreEqual(200, wallet.Gold);
            Assert.AreEqual(200, wallet.ApplyParalyzePenalty(800));
            Assert.AreEqual(0, wallet.Gold);
        }
    }

    public class ShopServiceTests
    {
        private static PropConfig Prop(int price, int maxPerPlayer)
        {
            var prop = ScriptableObject.CreateInstance<PropConfig>();
            prop.propId = "Test";
            prop.displayName = "测试道具";
            prop.price = price;
            prop.maxPerPlayer = maxPerPlayer;
            prop.unlockAtSeconds = 0;
            return prop;
        }

        [Test]
        public void 正常购买_扣款入包()
        {
            var wallet = new PlayerWallet(100);
            var inventory = new PlayerInventory();
            Assert.AreEqual(ShopBuyResult.Ok,
                ShopService.TryBuy(wallet, inventory, Prop(70, 0), true, 0f, 0));
            Assert.AreEqual(30, wallet.Gold);
            Assert.AreEqual(1, inventory.GetCount("Test"));
        }

        [Test]
        public void 金币不足拒绝且不扣款()
        {
            var wallet = new PlayerWallet(30);
            var inventory = new PlayerInventory();
            Assert.AreEqual(ShopBuyResult.NoMoney,
                ShopService.TryBuy(wallet, inventory, Prop(100, 8), true, 0f, 0));
            Assert.AreEqual(30, wallet.Gold);
            Assert.AreEqual(0, inventory.GetCount("Test"));
        }

        [Test]
        public void 机枪塔单方上限4()
        {
            var wallet = new PlayerWallet(10000);
            var inventory = new PlayerInventory();
            var turret = Prop(220, 4);
            for (var i = 0; i < 4; i++)
            {
                Assert.AreEqual(ShopBuyResult.Ok,
                    ShopService.TryBuy(wallet, inventory, turret, true, 0f, 0));
            }
            Assert.AreEqual(ShopBuyResult.CapReached,
                ShopService.TryBuy(wallet, inventory, turret, true, 0f, 0));
            Assert.AreEqual(4, inventory.GetCount("Test"));
        }

        [Test]
        public void 普通设施共享上限60()
        {
            // 2026-11-09 校准：共享普通设施上限 30→60（放置上限翻倍）
            var wallet = new PlayerWallet(100000);
            var inventory = new PlayerInventory();
            var roadblock = Prop(70, 0);
            Assert.AreEqual(ShopBuyResult.Ok,
                ShopService.TryBuy(wallet, inventory, roadblock, true, 0f, 59));
            Assert.AreEqual(ShopBuyResult.CapReached,
                ShopService.TryBuy(wallet, inventory, roadblock, true, 0f, 60));
        }

        [Test]
        public void 倒地或瘫痪时商店停用()
        {
            var wallet = new PlayerWallet(999);
            var inventory = new PlayerInventory();
            Assert.AreEqual(ShopBuyResult.ShopClosed,
                ShopService.TryBuy(wallet, inventory, Prop(70, 0), false, 0f, 0));
            Assert.AreEqual(999, wallet.Gold, "停用时不应扣款");
        }

        [Test]
        public void 背包消耗与持有数()
        {
            var inventory = new PlayerInventory();
            inventory.Add("Beacon", 2);
            Assert.AreEqual(2, inventory.GetCount("Beacon"));
            Assert.IsTrue(inventory.TryConsume("Beacon"));
            Assert.AreEqual(1, inventory.GetCount("Beacon"));
            Assert.IsFalse(inventory.TryConsume("Beacon", 2));
        }
    }
}
