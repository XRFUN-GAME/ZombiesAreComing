using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Waves
{
    public struct ZombieSpawnOption
    {
        public string ZombieId;
        public int ThreatPoints;
        public float Weight; // 阶段占比权重（2026-11-10 用户配比；≤0 视为等概率兜底）

        public ZombieSpawnOption(string zombieId, int threatPoints, float weight = 1f)
        {
            ZombieId = zombieId;
            ThreatPoints = threatPoints;
            Weight = weight;
        }
    }

    /// <summary>按阶段占比权重混选预算可支付的类型；返回原列表下标，无可支付项返回 -1。</summary>
    public static class WaveComposition
    {
        /// <param name="randomUnit">[0,1) 随机单位量——权重累进选取；权重全零时退化为等概率</param>
        public static int SelectAffordable(IReadOnlyList<ZombieSpawnOption> options,
            int carriedBudget, float randomUnit)
        {
            if (options == null)
            {
                return -1;
            }
            var count = 0;
            var totalWeight = 0f;
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].ThreatPoints > 0 && options[i].ThreatPoints <= carriedBudget)
                {
                    count++;
                    totalWeight += Mathf.Max(options[i].Weight, 0f);
                }
            }
            if (count == 0)
            {
                return -1;
            }

            var lastAffordable = -1;
            if (totalWeight <= 0f)
            {
                // 等概率兜底（无权重配置时与旧版一致）
                var pick = Mathf.Clamp01(randomUnit) * count;
                var seen = 0;
                for (var i = 0; i < options.Count; i++)
                {
                    if (options[i].ThreatPoints <= 0 || options[i].ThreatPoints > carriedBudget)
                    {
                        continue;
                    }
                    lastAffordable = i;
                    if (seen >= (int)pick)
                    {
                        return i;
                    }
                    seen++;
                }
                return lastAffordable;
            }

            var target = Mathf.Clamp01(randomUnit) * totalWeight;
            var cumulative = 0f;
            for (var i = 0; i < options.Count; i++)
            {
                if (options[i].ThreatPoints <= 0 || options[i].ThreatPoints > carriedBudget)
                {
                    continue;
                }
                lastAffordable = i;
                var w = Mathf.Max(options[i].Weight, 0f);
                cumulative += w;
                if (w > 0f && cumulative > target)
                {
                    return i;
                }
            }
            return lastAffordable;
        }

        /// <summary>拍波递增（2026-11-09 用户定案）：首拍 = 阶段入场数（感染扩散 5），逐拍 +1，
        /// 末拍顶到同屏上限 30；后续阶段入场 10/13/16/20 类比抬升，同样 +1/拍 封顶 30。</summary>
        public static int WaveSize(int phaseEntryCount, int waveIndexInPhase, int cap)
        {
            var k = waveIndexInPhase < 0 ? 0 : waveIndexInPhase;
            var size = phaseEntryCount + k;
            return size > cap ? cap : size;
        }
    }
}
