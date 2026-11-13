using MoriGame.Configs;
using MoriGame.Economy;

namespace MoriGame.Shop
{
    public enum ShopBuyResult : byte
    {
        Ok = 0,
        ShopClosed,   // 倒地或核心瘫痪（docs/05 7.3：商店只在己方中控可用；倒地/瘫痪时停用）
        NotUnlocked,  // 解锁时间未到
        NoMoney,
        CapReached    // 单玩家上限（机枪塔 4；普通设施共享 30，docs/02 2026-11-03 建设即扩张连带）
    }

    /// <summary>购买判定（docs/05 7.3 + docs/02 上限），纯 C# 可单测。
    /// sharedNormalUsed = 当前已持有的普通设施（maxPerPlayer=0）总数。</summary>
    public static class ShopService
    {
        public const int SharedNormalFacilityCap = 60; // 2026-11-09 用户校准：放置上限翻倍（30→60）

        public static ShopBuyResult TryBuy(
            PlayerWallet wallet,
            PlayerInventory inventory,
            PropConfig prop,
            bool shopOpen,
            float matchElapsedSeconds,
            int sharedNormalUsed)
        {
            if (!shopOpen)
            {
                return ShopBuyResult.ShopClosed;
            }
            if (matchElapsedSeconds < prop.unlockAtSeconds)
            {
                return ShopBuyResult.NotUnlocked;
            }
            var capReached = prop.maxPerPlayer > 0
                ? inventory.GetCount(prop.propId) >= prop.maxPerPlayer
                : sharedNormalUsed >= SharedNormalFacilityCap;
            if (capReached)
            {
                return ShopBuyResult.CapReached;
            }
            if (!wallet.TrySpend(prop.price))
            {
                return ShopBuyResult.NoMoney;
            }
            inventory.Add(prop.propId);
            return ShopBuyResult.Ok;
        }
    }
}
