namespace MoriGame.Combat
{
    /// <summary>
    /// 准星动态深度计算（2026-11-02 复视修复，纯 C# 可单测）：
    /// 命中物体 → 贴命中表面；落空 → 贴地面（最远 6 米截断）；朝天/无地面交点 → 隐藏（null）。
    /// </summary>
    public static class ReticlePlacement
    {
        public const float MissGroundMaxMeters = 6f;
        public const float MinVisibleMeters = 0.3f;

        /// <summary>返回准星沿视线的距离（米），null = 隐藏</summary>
        public static float? ComputeDistance(bool physicsHit, float physicsHitDistance,
            bool groundHit, float groundDistance)
        {
            if (physicsHit && physicsHitDistance >= MinVisibleMeters)
            {
                return physicsHitDistance;
            }
            if (groundHit)
            {
                var d = groundDistance > MissGroundMaxMeters ? MissGroundMaxMeters : groundDistance;
                return d >= MinVisibleMeters ? d : null;
            }
            return null;
        }
    }
}
