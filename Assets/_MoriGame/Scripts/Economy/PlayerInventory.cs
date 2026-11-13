using System;
using System.Collections.Generic;

namespace MoriGame.Economy
{
    /// <summary>已购道具持有计数（T6 建造放置时消耗，纯 C# 可单测）</summary>
    public class PlayerInventory
    {
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

        public event Action Changed;

        public int GetCount(string propId)
        {
            return counts.TryGetValue(propId, out var n) ? n : 0;
        }

        public void Add(string propId, int amount = 1)
        {
            counts[propId] = GetCount(propId) + amount;
            Changed?.Invoke();
        }

        public bool TryConsume(string propId, int amount = 1)
        {
            if (GetCount(propId) < amount)
            {
                return false;
            }
            counts[propId] -= amount;
            Changed?.Invoke();
            return true;
        }
    }
}
