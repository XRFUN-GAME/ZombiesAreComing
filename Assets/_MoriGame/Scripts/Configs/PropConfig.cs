using UnityEngine;

namespace MoriGame.Configs
{
    /// <summary>道具配置（docs/06 道具数值表 + 破拆器）。
    /// maxPerPlayer 为 0 时走共享上限（普通设施 20 个/玩家）。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Prop Config", fileName = "Prop_")]
    public class PropConfig : ScriptableObject
    {
        public string propId;
        public string displayName;

        [Header("解锁与价格")]
        public int unlockAtSeconds;
        public int price;

        [Header("基础数值")]
        public int maxHealth;
        [Tooltip("占地米数（0.5 米格），路障为 1 x 0.5")]
        public float footprintCellsX = 1f;
        public float footprintCellsY = 1f;
        [Tooltip("每玩家上限；0 = 共享普通设施上限 60（一切建筑落地即圈地，docs/02 2026-11-03；2026-11-09 翻倍）")]
        public int maxPerPlayer;

        [Header("自动机枪塔")]
        [Tooltip("射界=占地各向外扩格数（2026-11-11 用户：2×2 占地 + 5 = 12×12 格；圈地=射界）")]
        public int turretRangePadCells = 5;
        public int turretDamage;
        public float turretFireRatePerSecond;

        [Header("电网")]
        public int fenceDamage;
        [Tooltip("接触伤害结算间隔（秒）")]
        public float fenceTickSeconds = 0.5f;
        [Tooltip("每次造成伤害的跳表自扣血量（2026-11-10 用户定版：打人自己掉 10 血，电网随作战衰减）")]
        public int fenceSelfDamagePerTick = 10;

        [Header("减速力场")]
        public float slowRadiusMeters;
        [Tooltip("普通僵尸减速百分比 35 = -35%")]
        public float slowPercent;
        public float bossSlowPercent;

        [Header("修复无人机")]
        public float repairRangeMeters;
        public int repairPerSecond;

        [Header("烈焰陷阱")]
        public int trapDamage;
        public float trapRadiusMeters;
        public float trapCooldownSeconds;
        public bool trapHalvedVsBoss;

        [Header("破拆器")]
        public bool isDemolisher;
        public float demolisherArmSeconds = 6f;
        public float demolisherTargetRangeMeters = 2f;
        public float demolisherBoundaryMaxMeters = 1f;
        public float demolisherCooldownSeconds = 20f;

        [TextArea] public string notes;
    }
}
