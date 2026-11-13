using System;
using UnityEngine;

namespace MoriGame.Vfx
{
    [Serializable]
    public class VfxEntry
    {
        public GameObject prefab;
        [Tooltip("站桩 5 米尺度修正（PC 原始尺寸 × scale）")]
        public float scale = 1f;
        [Tooltip("同屏并发上限（超限丢弃最旧）；0=不限")]
        public int maxConcurrent;
    }

    /// <summary>特效库（2026-11-11 资产化，风格同 SfxLibrary）：集中 SerializeField 引用不散落路径；
    /// 装配在 ConfigAssetGenerator.CreateVfxLibrary（映射表见 CLAUDE.md 决策日志，按名语义映射备复核）；
    /// 运行时经 MatchBootstrap.Vfx（VfxPool 门面）访问。</summary>
    [CreateAssetMenu(menuName = "MoriGame/Vfx Library", fileName = "VfxLibrary")]
    public class VfxLibrary : ScriptableObject
    {
        [Header("总开关（2026-11-11 移到 Inspector：勾=全部特效启用）")]
        [Tooltip("关=全部特效静默（接线/资产保留）；开=按各条目播放")]
        public bool vfxEnabled = true;

        [Header("运行时预算")]
        [Tooltip("运行时压缩：实例化时 maxParticles 减半+装饰子粒子禁用+Light 禁用。自己调参时保持关（否则改了没生效）")]
        public bool compressOnSpawn = false;

        [Header("武器（枪口焰/弹壳/弹丸拖尾）")]
        public VfxEntry muzzleShot = new VfxEntry();
        public VfxEntry shellEject = new VfxEntry();
        public VfxEntry projectileTrail = new VfxEntry();

        [Header("酸液")]
        public VfxEntry acidProjectile = new VfxEntry();
        public VfxEntry acidImpact = new VfxEntry();

        [Header("命中与死亡（上限防爆体/溅血风暴）")]
        [Tooltip("普通命中溅血（maxConcurrent=10）")]
        public VfxEntry bloodHit = new VfxEntry();
        [Tooltip("弱点击杀溅血（同 10 上限）")]
        public VfxEntry bloodWeak = new VfxEntry();
        [Tooltip("普通系爆体（maxConcurrent=6）")]
        public VfxEntry deathNormal = new VfxEntry();
        [Tooltip("壮汉/Boss 系爆体")]
        public VfxEntry deathBrute = new VfxEntry();
        [Tooltip("击杀金币入账（击杀点喷金币）")]
        public VfxEntry goldGain = new VfxEntry();

        [Header("机枪塔")]
        public VfxEntry turretFire = new VfxEntry();
        public VfxEntry turretHit = new VfxEntry();

        [Header("电网/中控/建筑")]
        [Tooltip("电网电光（循环挂实体）")]
        public VfxEntry fenceLoop = new VfxEntry();
        [Tooltip("中控瘫痪雷击")]
        public VfxEntry coreDown = new VfxEntry();
        public VfxEntry coreRecover = new VfxEntry();
        [Tooltip("建筑受损三档烟（循环，随宿主）")]
        public VfxEntry smokeLight = new VfxEntry();
        public VfxEntry smokeMid = new VfxEntry();
        public VfxEntry smokeHeavy = new VfxEntry();
        [Tooltip("危急火（≤10% 血量与黑烟叠加，T3.5 余项补齐 2026-11-11）")]
        public VfxEntry damageFire = new VfxEntry();
        [Tooltip("建筑摧毁爆炸")]
        public VfxEntry buildingDestroy = new VfxEntry();

        [Header("经济/交互")]
        [Tooltip("补给箱开箱金币喷溅")]
        public VfxEntry crateOpen = new VfxEntry();
        [Tooltip("补给箱常驻微光标记（循环）")]
        public VfxEntry crateGlow = new VfxEntry();
        [Tooltip("抓取购买成功（展示位）")]
        public VfxEntry purchaseSuccess = new VfxEntry();
        [Tooltip("放置扬尘")]
        public VfxEntry placementDust = new VfxEntry();

        [Header("玩家状态")]
        [Tooltip("倒地星星")]
        public VfxEntry playerDowned = new VfxEntry();
        [Tooltip("重生保护光环（循环=保护期时长）")]
        public VfxEntry protectionHalo = new VfxEntry();
        [Tooltip("传送落点（传送已废弃站桩制，字段预留可回挂）")]
        public VfxEntry teleportLanding = new VfxEntry();

        [Header("结算")]
        public VfxEntry medalConfetti = new VfxEntry();
        [Tooltip("金牌加放烟花")]
        public VfxEntry medalFireworks = new VfxEntry();
    }
}
