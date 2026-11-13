using UnityEngine;

namespace MoriGame.Configs
{
    /// <summary>玩家生命与开局经济配置（docs/07 4.1 + docs/05 7.1）。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Player Config", fileName = "PlayerConfig")]
    public class PlayerConfig : ScriptableObject
    {
        [Header("生命（docs/07）")]
        public int maxHp = 100;

        [Tooltip("倒地到自动重生的时间（秒）")]
        public float downedSeconds = 8f;

        [Tooltip("重生后无敌时间（秒），主动射击提前结束")]
        public float respawnProtectionSeconds = 3f;

        [Header("倒地惩罚（docs/07）")]
        [Tooltip("损失当前金币百分比")]
        public float downedGoldLossPercent = 15f;

        [Tooltip("单次倒地金币损失上限")]
        public int downedGoldLossCap = 60;

        [Header("开局（docs/01 3.4 / docs/05 7.1）")]
        public int startingGold = 100;
    }
}
