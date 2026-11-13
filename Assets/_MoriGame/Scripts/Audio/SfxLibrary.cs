using UnityEngine;

namespace MoriGame.Audio
{
    /// <summary>音效库（2026-11-10 资产化替换程序合成 Beep）：集中 SerializeField 引用不散落路径；
    /// 装配在 ConfigAssetGenerator.CreateSfxLibrary（《音效映射表》见 CLAUDE.md 决策日志）；
    /// 运行时经 MatchBootstrap.Sfx（SfxBus 门面）访问。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Sfx Library", fileName = "SfxLibrary")]
    public class SfxLibrary : ScriptableObject
    {
        [Header("玩家武器（2D · sfxVolume）")]
        [Tooltip("开火枪声（每发一响，PlayOneShot 重叠混音）")]
        public AudioClip playerFire;
        public AudioClip magSnap;

        [Header("命中（3D · pitch 随机防重复感）")]
        public AudioClip hit;
        public AudioClip weakpointHit;

        [Header("僵尸（3D）")]
        public AudioClip zombieMeleeAttack;
        public AudioClip acidSpit;
        [Tooltip("普通死亡音池（随机 5）")]
        public AudioClip[] normalDeathPool = new AudioClip[5];
        [Tooltip("壮汉（蛮兽）死亡音池（随机 2）")]
        public AudioClip[] bruteDeathPool = new AudioClip[2];
        [Tooltip("督战者死亡（1）")]
        public AudioClip overseerDeath;
        [Tooltip("弱点击杀（1）")]
        public AudioClip weakpointKill;

        [Header("建筑/对局事件（3D 世界声源）")]
        public AudioClip turretFire;
        public AudioClip fenceLoop;
        public AudioClip coreDown;
        public AudioClip coreRecover;
        public AudioClip crateOpen;

        [Header("玩家事件（2D）")]
        public AudioClip playerDown;

        [Header("UI/钱包/Toast（2D · uiVolume）")]
        public AudioClip coinBig;
        public AudioClip coinSmall;
        public AudioClip purchaseSuccess;
        public AudioClip shopOpen;
        [Tooltip("腕部菜单展开（菜单已撤，字段预留可回挂）")]
        public AudioClip wristMenuOpen;
        public AudioClip uiClick;
        public AudioClip grab;
        public AudioClip dock;
        public AudioClip success;
        public AudioClip fail;

        [Header("背景音乐（2D · BgmDirector 双源交叉淡化）")]
        [Tooltip("轮播基础曲 1")]
        public AudioClip bgmLoop1;
        [Tooltip("轮播基础曲 2")]
        public AudioClip bgmLoop2;
        [Tooltip("紧张插曲（偶尔夹杂，播完回轮播）")]
        public AudioClip bgmTension;
        [Tooltip("最终 Boss 主题（督战者实体在场期间循环）")]
        public AudioClip bgmBoss;
        [Tooltip("背景音乐全局音量")]
        public float bgmVolume = 0.5f;
        [Tooltip("交叉淡化时长（秒）——切换丝滑的关键参数")]
        public float bgmCrossfadeSeconds = 3f;
        [Tooltip("每轮轮播切紧张插曲的概率（至少隔 2 轮普通曲才可触发）")]
        [Range(0f, 1f)]
        public float bgmTensionChance = 0.25f;

        [Header("全局参数")]
        [Tooltip("游戏音效全局音量")]
        public float sfxVolume = 0.7f;
        [Tooltip("UI 类音量（菜单/点击/钱包/Toast）")]
        public float uiVolume = 0.5f;
        [Tooltip("3D 声源对数衰减最大距离（米）")]
        public float spatialMaxDistance = 15f;
        [Tooltip("命中/僵尸攻击音调随机下限")]
        public float pitchMin = 0.95f;
        [Tooltip("命中/僵尸攻击音调随机上限")]
        public float pitchMax = 1.05f;
        [Tooltip("僵尸死亡同帧最大并发数（防爆音）")]
        public int deathConcurrentLimit = 3;
    }
}
