using UnityEngine;

namespace MoriGame.Configs
{
    /// <summary>武器配置（docs/03 武器数值表，基础值）</summary>
    [CreateAssetMenu(menuName = "MoriGame/Weapon Config", fileName = "Weapon_")]
    public class WeaponConfig : ScriptableObject
    {
        [Header("解锁与价格")]
        public string weaponId;
        public string displayName;
        public int unlockAtSeconds;
        public int price;

        [Header("基础数值")]
        [Tooltip("单颗弹丸伤害；霰弹枪为每颗弹丸的伤害")]
        public int damagePerPellet = 24;
        [Tooltip("每发弹丸数，非霰弹武器为 1")]
        public int pelletsPerShot = 1;
        public float fireRatePerSecond = 3f;
        public int magazineSize = 12;
        public float reloadSeconds = 1.4f;

        [Header("特性参数")]
        [Tooltip("射程衰减起始距离（米），0 = 无衰减")]
        public float rangeFalloffStartMeters;
        [Tooltip("电弧枪连锁跳跃次数，0 = 无连锁")]
        public int arcChainJumpCount;
        public int arcChainDamage;
        public float arcChainRangeMeters;
        public bool arcSkipsBoss = true;
        [Tooltip("可打断疾行者扑击")]
        public bool interruptsPounce;

        [TextArea] public string notes;
    }
}
