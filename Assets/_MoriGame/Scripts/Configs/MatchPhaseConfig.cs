using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Configs
{
    [Serializable]
    public class MatchPhaseDef
    {
        public string phaseId;
        public string displayName;
        public int startSeconds;
        public int endSeconds;
        [Tooltip("每 30 秒 / 每玩家威胁点预算，准备阶段为 0")]
        public int threatBudgetPerPlayer;
        public List<string> allowedZombieIds = new List<string>();
        [Tooltip("阶段入场存活下限（2026-11-09：5/10/13/16/20 阶梯，比上一阶段起点多；换阶段重置加量）")]
        public int initialAliveCount;
        [Tooltip("各类型刷怪占比权重（与 allowedZombieIds 一一对应；空=等概率）。2026-11-10 用户配比：P2 50/30/20，P3 40/30/20/10，P4 35/25/20/10/10，P5 全 20")]
        public List<float> typeWeights = new List<float>();
        [Tooltip("拍波封顶数（2026-11-10 三次调压：前三阶段 15、后两 20——拍量从入场数逐拍+1 到此封顶）")]
        public int waveSizeCap;
        [Tooltip("本阶段同屏存活上限（0=用全局 maxActiveZombiesPerPlayer）。2026-11-10：P1—P4=20，最终阶段=25")]
        public int aliveCap;
        [Tooltip("到达本阶段的金币奖励（准备阶段 0；共 5 次 x 300，经济 v2）")]
        public int phaseArrivalGoldReward;
    }

    [Serializable]
    public class SpecialEventDef
    {
        public int atSeconds;
        public string eventId;
        public string parameter;
    }

    /// <summary>对局阶段与波次预算配置（docs/01 对局阶段表 + docs/04 波次预算表）。
    /// 全局规则：阶段切换前 10 秒减速窗不出新拍（2026-11-09 拍波制）；活跃上限 30/人（60 全场）；
    /// 预算最多积两轮；拍量 = 入场存活（5/10/13/16/20）逐拍 +1 到 waveSizeCap（前三阶段 20、后两 25）。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Match Phase Config", fileName = "MatchPhases")]
    public class MatchPhaseConfig : ScriptableObject
    {
        [Header("对局全局")]
        public int matchDurationSeconds = 480;
        public int budgetIntervalSeconds = 30;
        public int initialHealth = 100;
        public int initialGold = 100;

        [Header("生成上限与预算")]
        public int maxActiveZombiesPerPlayer = 20; // 2026-11-09：同阶段最大可同时出现 20 只
        public int maxActiveZombiesTotal = 60;
        public int maxBudgetCarryRounds = 2;
        public float phaseSwitchSlowWindowSeconds = 10f;
        public float phaseSwitchSlowFactor = 0.5f;

        [Header("阶段表")]
        public List<MatchPhaseDef> phases = new List<MatchPhaseDef>();

        [Header("特殊事件")]
        public List<SpecialEventDef> specialEvents = new List<SpecialEventDef>();
    }
}
