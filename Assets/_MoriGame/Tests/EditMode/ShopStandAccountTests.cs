using MoriGame.Configs;
using MoriGame.Economy;
using MoriGame.Shop;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>
    /// 展示位账务纯逻辑（docs/07 2026-11-05 范式终稿 + 去库存修订）：
    /// 无库存概念，钱够就能抓——抓起即扣款；归位/扔掉/追踪丢失都未落地全额退款；
    /// 放下进放置预览后账务移交 BuildManager（取消/封路回滚由其退款）；置灰 = 预检不过。
    /// </summary>
    public class ShopStandAccountTests
    {
        private static PropConfig Prop(int price, int maxPerPlayer, int unlockAtSeconds = 0)
        {
            var prop = ScriptableObject.CreateInstance<PropConfig>();
            prop.propId = "Test";
            prop.displayName = "测试道具";
            prop.price = price;
            prop.maxPerPlayer = maxPerPlayer;
            prop.unlockAtSeconds = unlockAtSeconds;
            return prop;
        }

        // ---- 可抓判定（置灰反命题） ----

        [Test]
        public void 可抓_钱够放钱不够拒()
        {
            var prop = Prop(220, 4);
            Assert.IsTrue(ShopStandSession.CanGrab(prop, new PlayerWallet(220), true, 0f, 0, 0));
            Assert.IsFalse(ShopStandSession.CanGrab(prop, new PlayerWallet(219), true, 0f, 0, 0),
                "金币不足置灰");
        }

        [Test]
        public void 可抓_商店关闭拒()
        {
            Assert.IsFalse(ShopStandSession.CanGrab(Prop(220, 4), new PlayerWallet(300), false, 0f, 0, 0),
                "商店停用（倒地/中控瘫痪）不可购");
        }

        [Test]
        public void 可抓_未解锁拒_到点放行()
        {
            var prop = Prop(220, 4, 60);
            Assert.IsFalse(ShopStandSession.CanGrab(prop, new PlayerWallet(300), true, 59.9f, 0, 0));
            Assert.IsTrue(ShopStandSession.CanGrab(prop, new PlayerWallet(300), true, 60f, 0, 0));
        }

        [Test]
        public void 可抓_单方上限满拒_共享上限满拒()
        {
            Assert.IsFalse(ShopStandSession.CanGrab(Prop(220, 4), new PlayerWallet(10000), true, 0f, 4, 0),
                "已落地 4 达单方上限");
            // 2026-11-09 校准：共享普通设施上限 30→60（放置上限翻倍）
            Assert.IsFalse(ShopStandSession.CanGrab(Prop(140, 0), new PlayerWallet(10000), true, 0f, 0, 60),
                "共享普通设施落地 60 达共享上限");
            Assert.IsTrue(ShopStandSession.CanGrab(Prop(140, 0), new PlayerWallet(10000), true, 0f, 0, 59));
        }

        // ---- 抓起（购买） ----

        [Test]
        public void 抓起_扣款记在手()
        {
            var wallet = new PlayerWallet(300);
            var session = new ShopStandSession();
            Assert.AreEqual(ShopBuyResult.Ok, session.Grab(wallet, Prop(220, 4), true, 0f, 0, 0));
            Assert.AreEqual(80, wallet.Gold);
            Assert.IsTrue(session.InHand);
        }

        [Test]
        public void 抓起_钱不够拒_不扣款不留手()
        {
            var wallet = new PlayerWallet(100);
            var session = new ShopStandSession();
            Assert.AreEqual(ShopBuyResult.NoMoney, session.Grab(wallet, Prop(220, 4), true, 0f, 0, 0));
            Assert.AreEqual(100, wallet.Gold, "拒购不扣款");
            Assert.IsFalse(session.InHand, "拒购不留手");
        }

        [Test]
        public void 抓起_上限满拒()
        {
            var wallet = new PlayerWallet(10000);
            var session = new ShopStandSession();
            Assert.AreEqual(ShopBuyResult.CapReached, session.Grab(wallet, Prop(220, 4), true, 0f, 4, 0));
            Assert.IsFalse(session.InHand);
        }

        [Test]
        public void 抓起_一手一件_再抓拒不重复扣款()
        {
            var wallet = new PlayerWallet(10000);
            var session = new ShopStandSession();
            var prop = Prop(220, 4);
            Assert.AreEqual(ShopBuyResult.Ok, session.Grab(wallet, prop, true, 0f, 0, 0));
            Assert.AreEqual(ShopBuyResult.CapReached, session.Grab(wallet, prop, true, 0f, 0, 0));
            Assert.AreEqual(10000 - 220, wallet.Gold, "二次抓起被拒不重复扣款");
        }

        // ---- 归位 / 扔掉 / 放下 ----

        [Test]
        public void 归位_全额退款()
        {
            var wallet = new PlayerWallet(300);
            var prop = Prop(220, 4);
            var session = new ShopStandSession();
            session.Grab(wallet, prop, true, 0f, 0, 0);
            session.Dock(wallet, prop);
            Assert.AreEqual(300, wallet.Gold, "未落地归位全额退款");
            Assert.IsFalse(session.InHand);
        }

        [Test]
        public void 扔掉_同归位全额退款()
        {
            var wallet = new PlayerWallet(300);
            var prop = Prop(220, 4);
            var session = new ShopStandSession();
            session.Grab(wallet, prop, true, 0f, 0, 0);
            session.Discard(wallet, prop);
            Assert.AreEqual(300, wallet.Gold, "无库存概念，扔掉未落地同归位退款");
            Assert.IsFalse(session.InHand);
        }

        [Test]
        public void 放下_会话清空_钱保持已扣_移交BuildManager()
        {
            var wallet = new PlayerWallet(300);
            var prop = Prop(220, 4);
            var session = new ShopStandSession();
            session.Grab(wallet, prop, true, 0f, 0, 0);
            session.Dropped();
            Assert.IsFalse(session.InHand);
            Assert.AreEqual(80, wallet.Gold, "放下后钱保持已扣（取消/封路回滚由 BuildManager 退款）");
            session.Dock(wallet, prop); // 放下后 session 已清空——重复归位不二次退款
            Assert.AreEqual(80, wallet.Gold, "会话清空后归位不重复退款");
        }

        [Test]
        public void 直接换建筑_旧件全退_新件净扣款()
        {
            var wallet = new PlayerWallet(300);
            var oldProp = Prop(220, 4);
            var newProp = Prop(140, 0);
            var oldSession = new ShopStandSession();
            var newSession = new ShopStandSession();
            oldSession.Grab(wallet, oldProp, true, 0f, 0, 0);
            var beforeSwitch = wallet.Gold;
            var result = newSession.GrabReplacing(wallet, newProp, oldSession, oldProp, true, 0f, 0, 0);
            Assert.AreEqual(ShopBuyResult.Ok, result, "余额不足时应把旧件退款计入预检");
            Assert.AreEqual(beforeSwitch + oldProp.price - newProp.price, wallet.Gold,
                "切换后金币=切换前金币+旧件价格-新件价格");
            Assert.IsFalse(oldSession.InHand);
            Assert.IsTrue(newSession.InHand);
        }

        [Test]
        public void 直接换建筑_含退款仍不足_旧件与余额不动()
        {
            var wallet = new PlayerWallet(300);
            var oldProp = Prop(220, 4);
            var newProp = Prop(500, 4);
            var oldSession = new ShopStandSession();
            var newSession = new ShopStandSession();
            oldSession.Grab(wallet, oldProp, true, 0f, 0, 0);
            var beforeSwitch = wallet.Gold;
            var result = newSession.GrabReplacing(wallet, newProp, oldSession, oldProp, true, 0f, 0, 0);
            Assert.AreEqual(ShopBuyResult.NoMoney, result);
            Assert.AreEqual(beforeSwitch, wallet.Gold, "拒购不能先退旧件再失败");
            Assert.IsTrue(oldSession.InHand, "购买失败保留旧建筑在手");
            Assert.IsFalse(newSession.InHand);
        }
    }
}
