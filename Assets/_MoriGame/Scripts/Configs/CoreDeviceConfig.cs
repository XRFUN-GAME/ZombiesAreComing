using UnityEngine;

namespace MoriGame.Configs
{
    /// <summary>中控装置配置（docs/07 4.5 / 12.1）。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Core Device Config", fileName = "CoreDeviceConfig")]
    public class CoreDeviceConfig : ScriptableObject
    {
        public int maxHp = 1200;

        [Tooltip("瘫痪时长（秒）；期间核心不再受伤、商店停用、仍可重生、地盘保持")]
        public float paralyzeSeconds = 10f;

        [Tooltip("瘫痪触发时立即扣的金币（不足扣到 0）")]
        public int paralyzeGoldPenalty = 800;

        [Tooltip("瘫痪结束后自动恢复到的生命值（40% = 480）")]
        public int recoverHp = 480;

        [Header("新手翻身机制（2026-11-11 用户三选二：阶段减伤 + 瘫痪 EMP 反冲）")]
        [Tooltip("前 N 个阶段中控受伤按 newbieDamageFactor 缩减（0=永久关闭减伤）")]
        public int newbiePhaseCount = 2;
        [Tooltip("新手期中控受伤倍率（P1/P2 ×0.5，P3 起 ×1.0）")]
        public float newbieDamageFactor = 0.5f;
        [Tooltip("瘫痪 EMP 反冲：全场僵尸击退距离（米）")]
        public float empKnockbackMeters = 2f;
        [Tooltip("瘫痪 EMP 反冲：僵尸眩晕时长（秒）")]
        public float empStunSeconds = 2f;
    }
}
