using UnityEngine;

namespace MoriGame.Configs
{
    /// <summary>僵尸配置（docs/04 僵尸数值表）。
    /// 弱点倍率按 5.1 规则文本属僵尸类型属性（普通 2 倍 / 督战者 1.5 倍），
    /// 故放此处而非 docs/03 实现要点提到的 WeaponConfig。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Zombie Config", fileName = "Zombie_")]
    public class ZombieConfig : ScriptableObject
    {
        public string zombieId;
        public string displayName;

        [Header("基础数值")]
        public int threatPoints = 1;
        public int maxHealth = 60;
        public float moveSpeedMps = 0.8f;
        public int meleeDamage = 8;
        public int killGold = 4;
        public int combatScore = 1;
        public float weakpointMultiplier = 2f;
        [Tooltip("蛮兽、督战者为 true：减速力场效果减半、电弧枪不跳跃")]
        public bool isBossType;

        [Header("疾行者")]
        public int hitsToStagger = 3;
        public float staggerSeconds = 0.5f;
        public float staggerChainResetSeconds = 1.5f;

        [Header("喷吐者")]
        public int acidDamage;
        public float acidCooldownSeconds;
        public float acidRangeMeters;
        public float acidProjectileSpeedMps = 6f;
        [Tooltip("起手延迟（秒）：先播 Attack 动画再发射酸弹，2026-11-11 实测反馈")]
        public float acidWindupSeconds = 0.35f;

        [Header("盾卫")]
        public int frontShieldHealth;

        [Header("督战者")]
        public int chargeDamage;

        [TextArea] public string notes;
    }
}
