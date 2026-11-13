using UnityEngine;

namespace MoriGame.Configs
{
    public enum CardRarity { Common = 0, Rare = 1, Epic = 2 }

    public enum CardType { Passive = 0, Active = 1 }

    /// <summary>效果种类（结算走修饰器管线，禁止 if-else 卡片名分支）</summary>
    public enum CardEffectKind
    {
        None = 0,
        ReloadTimePercent = 1,      // 快速装填：value1 = -0.15
        WeakpointKillBonusGold = 2, // 赏金猎人：value1 = +2
        PropPricePercent = 3,       // 轻量建材：value1 = -0.20，appliesToProps 限定
        HealTuning = 4,             // 野战医疗：value1 = 新回血等待秒，value2 = 新速度/秒
        MagazineCapacityPercent = 5,// 扩容弹匣：value1 = +0.30（向上取整）
        BeaconPricePercent = 6,     // 高效拓界：value1 = -0.15
        TurretDamageHealthTrade = 7,// 过载电网：value1 = 伤害 +0.20，value2 = 生命 -0.10
        DemolisherOverride = 8,     // 定向爆破：value1 = 新价格，value2 = 新启动秒
        CoreInvulnerable = 9,       // 紧急屏障：durationSeconds = 8
        ZombieFreeze = 10,          // 时间静滞：value1 = 普通停止秒，value2 = 督战者停止秒
        KillGoldBoost = 11,         // 黄金时刻：durationSeconds = 30，value1 = +0.50
        RepairBurst = 12            // 应急重建：value1 = 中控 +生命，value2 = 每道具 +生命
    }

    /// <summary>卡片配置（docs/05 卡片列表，共 12 张；M1—M2 只启用 inMvpScope 六张）</summary>
    [CreateAssetMenu(menuName = "MoriGame/Card Config", fileName = "Card_")]
    public class CardConfig : ScriptableObject
    {
        public string cardId;
        public string displayName;
        public CardRarity rarity;
        public CardType type;
        [Tooltip("主动一次性卡为 true（用后消失）")]
        public bool oneShotUse;
        [Tooltip("不可叠加卡为 false；同名被动最多 2 层由卡组逻辑控制")]
        public bool stackable = true;

        public CardEffectKind effectKind;
        public float value1;
        public float value2;
        public float value3;
        [Tooltip("主动卡持续秒数；被动卡 0")]
        public float durationSeconds;
        [Tooltip("效果限定道具 propId 列表（逗号分隔），空 = 不限定")]
        public string appliesToProps;
        [Tooltip("M1—M2 MVP 范围内")]
        public bool inMvpScope;

        [TextArea] public string description;
    }
}
