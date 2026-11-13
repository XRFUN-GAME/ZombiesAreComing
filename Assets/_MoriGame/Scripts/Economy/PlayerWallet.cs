using System;

namespace MoriGame.Economy
{
    /// <summary>金币钱包（docs/05 7.1，纯 C# 可单测）</summary>
    public class PlayerWallet
    {
        public int Gold { get; private set; }

        public event Action Changed;

        public PlayerWallet(int startingGold)
        {
            Gold = startingGold;
        }

        public void Add(int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            Gold += amount;
            Changed?.Invoke();
        }

        public bool TrySpend(int amount)
        {
            if (amount <= 0 || amount > Gold)
            {
                return false;
            }
            Gold -= amount;
            Changed?.Invoke();
            return true;
        }

        /// <summary>倒地罚金：损失当前金币 percent%，单次上限 cap（docs/07）。返回实扣数额。</summary>
        public int ApplyDownedPenalty(float percent, int cap)
        {
            var penalty = Math.Min((int)Math.Floor(Gold * percent / 100f), cap);
            if (penalty <= 0)
            {
                return 0;
            }
            Gold -= penalty;
            Changed?.Invoke();
            return penalty;
        }

        /// <summary>瘫痪罚金：立即扣最多 max，不足扣到 0（docs/07）。返回实扣数额。</summary>
        public int ApplyParalyzePenalty(int max)
        {
            var penalty = Math.Min(Gold, max);
            if (penalty <= 0)
            {
                return 0;
            }
            Gold -= penalty;
            Changed?.Invoke();
            return penalty;
        }
    }
}
