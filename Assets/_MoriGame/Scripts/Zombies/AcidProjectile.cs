using MoriGame.Build;
using UnityEngine;

namespace MoriGame.Zombies
{
    /// <summary>喷吐者酸液：实体飞行到目标点后结算，可伤害玩家、建筑与中控。</summary>
    public class AcidProjectile : MonoBehaviour
    {
        private Transform target;
        private Vector3 targetPoint;
        private int damage;
        private float speedMps;
        // 酸液弹视觉走 VfxBus.Follow（挂池根防 0.14 缩没；销毁自动回收，2026-11-11 修复冻结/不可见）

        public static AcidProjectile Spawn(Vector3 origin, Transform targetTransform,
            int acidDamage, float projectileSpeedMps, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AcidProjectile";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * 0.14f;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            var projectile = go.AddComponent<AcidProjectile>();
            Vfx.VfxBus.Follow(l => l.acidProjectile, go.transform);
            projectile.target = targetTransform;
            projectile.targetPoint = AimPoint(targetTransform);
            projectile.damage = acidDamage;
            projectile.speedMps = Mathf.Max(0.1f, projectileSpeedMps);
            return projectile;
        }

        private void Update()
        {
            if (target != null)
            {
                targetPoint = AimPoint(target);
            }
            var delta = targetPoint - transform.position;
            var step = speedMps * Time.deltaTime;
            if (delta.sqrMagnitude > step * step)
            {
                transform.position += delta.normalized * step;
                return;
            }
            transform.position = targetPoint;
            if (target != null)
            {
                var prop = target.GetComponent<PropBehaviour>();
                if (prop != null)
                {
                    prop.TakeDamage(damage);
                }
                else
                {
                    var match = MatchBootstrap.Instance;
                    if (match != null && match.IsPlayerTarget(target))
                    {
                        match.Vitals.TakeDamage(damage);
                    }
                    else if (match != null && match.coreDevice != null && target == match.coreDevice.transform)
                    {
                        match.coreDevice.TakeDamage(damage);
                    }
                }
            }
            Vfx.VfxBus.Play(l => l.acidImpact, targetPoint); // 命中爆开（2026-11-11 特效系统）
            Destroy(gameObject); // 酸液拖尾随销毁自动回收，回收路径零 VFX 调用
        }

        private static Vector3 AimPoint(Transform targetTransform)
        {
            if (targetTransform == null)
            {
                return Vector3.zero;
            }
            var prop = targetTransform.GetComponent<PropBehaviour>();
            if (prop != null)
            {
                return prop.transform.position + Vector3.up * 0.35f;
            }
            var match = MatchBootstrap.Instance;
            if (match != null && match.IsPlayerTarget(targetTransform))
            {
                return targetTransform.position + Vector3.up;
            }
            return targetTransform.position + Vector3.up * 0.5f;
        }
    }
}
