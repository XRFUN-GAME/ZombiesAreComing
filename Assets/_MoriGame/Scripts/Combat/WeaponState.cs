namespace MoriGame.Combat
{
    public enum FireResult : byte
    {
        Fired = 0,
        Cooldown = 1,
        Empty = 2,
        Reloading = 3
    }

    /// <summary>
    /// 武器射击状态（docs/03，纯 C# 可单测）：射速冷却、弹匣、换弹计时。
    /// 弹匣空 → TryFire 返回 Empty，由调用方决定自动辅助换弹（docs/03 通用射击规则）。
    /// </summary>
    public class WeaponState
    {
        private const float TimingEpsilon = 0.0005f; // 浮点累加边界容差（T4 经验）

        public readonly int MagazineSize;
        public readonly float FireIntervalSeconds;
        public readonly float ReloadSeconds;

        public int AmmoInMag { get; private set; }
        public bool IsReloading { get; private set; }
        public float ReloadRemainingSeconds { get; private set; }

        private float cooldownRemaining;

        public WeaponState(int magazineSize, float fireRatePerSecond, float reloadSeconds)
        {
            MagazineSize = magazineSize;
            FireIntervalSeconds = 1f / fireRatePerSecond;
            ReloadSeconds = reloadSeconds;
            AmmoInMag = magazineSize;
        }

        public bool CanFire => !IsReloading && AmmoInMag > 0 && cooldownRemaining <= TimingEpsilon;

        /// <summary>尝试开火：成功扣 1 发并进入射速冷却。</summary>
        public FireResult TryFire()
        {
            if (IsReloading)
            {
                return FireResult.Reloading;
            }
            if (AmmoInMag <= 0)
            {
                return FireResult.Empty;
            }
            if (cooldownRemaining > TimingEpsilon)
            {
                return FireResult.Cooldown;
            }
            AmmoInMag--;
            cooldownRemaining = FireIntervalSeconds;
            return FireResult.Fired;
        }

        /// <summary>手动/辅助换弹：弹匣未满且未在换弹时开始。</summary>
        public bool StartReload()
        {
            if (IsReloading || AmmoInMag >= MagazineSize)
            {
                return false;
            }
            IsReloading = true;
            ReloadRemainingSeconds = ReloadSeconds;
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            if (cooldownRemaining > 0f)
            {
                cooldownRemaining -= deltaSeconds;
            }
            if (!IsReloading)
            {
                return;
            }
            ReloadRemainingSeconds -= deltaSeconds;
            if (ReloadRemainingSeconds <= TimingEpsilon)
            {
                IsReloading = false;
                ReloadRemainingSeconds = 0f;
                AmmoInMag = MagazineSize;
            }
        }
    }
}
