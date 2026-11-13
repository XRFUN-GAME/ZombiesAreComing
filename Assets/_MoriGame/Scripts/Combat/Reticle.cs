using UnityEngine;

namespace MoriGame.Combat
{
    /// <summary>
    /// 准星与命中脉冲：挂 Main Camera，**动态深度贴射线命中物**——
    /// 命中僵尸/物体贴其表面、落空贴地面（6 米截断）、朝天无交点隐藏（修复固定 0.9 米导致的复视）。
    /// 双瞄准模式（红线：出包前 BuildAimMode 选定烘入 useBarrelRay，游戏内不切换）：
    /// A = 头部射线；B = muzzle + 枪管 forward。
    /// 命中脉冲：白 = 命中僵尸，金 = 弱点击杀；脉冲环 0.22 秒扩散。
    /// </summary>
    public class Reticle : MonoBehaviour
    {
        public Transform dot;
        public Transform pulse;
        public Renderer pulseRenderer;
        public Material pulseWhiteMaterial;
        public Material pulseGoldMaterial;
        [Tooltip("枪管瞄准模式开关（出包前由 BuildAimMode 烘入，游戏内不切换）")]
        public bool useBarrelRay;
        [Tooltip("枪管瞄准模式的射线源（= muzzle，MoriSceneSetup 注入）")]
        public Transform barrelOrigin;

        private const float PulseSeconds = 0.22f;
        private const float PulseStartScale = 0.02f;
        private const float PulseEndScale = 0.07f;
        private const float SurfaceOffsetMeters = 0.05f;
        private const float DotScalePerMeter = 0.008f; // 常角约 0.46°，远近一致

        private float pulseRemaining;
        private Transform head;

        private void Update()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                head = cam.transform;
            }

            var useBarrel = useBarrelRay && barrelOrigin != null;
            var ray = useBarrel
                ? new Ray(barrelOrigin.position, barrelOrigin.forward)
                : new Ray(head.position, head.forward);
            var physicsHit = PlayerStationFilter.Raycast(ray, 40f, out var hit); // 穿透玩家设施（2026-11-09）
            var groundDistance = 0f;
            var groundHit = !physicsHit && ray.direction.y < -0.01f &&
                            GroundDistance(ray, out groundDistance);
            var distance = ReticlePlacement.ComputeDistance(
                physicsHit, physicsHit ? hit.distance : 0f,
                groundHit, groundHit ? groundDistance : 0f);
            if (distance == null)
            {
                // 只藏视觉子物体——藏根物体会停掉本组件的 Update，准星将永久消失
                if (dot != null && dot.gameObject.activeSelf)
                {
                    dot.gameObject.SetActive(false);
                }
                return;
            }
            if (dot != null && !dot.gameObject.activeSelf)
            {
                dot.gameObject.SetActive(true);
            }
            transform.position = ray.origin + ray.direction * (distance.Value - SurfaceOffsetMeters);
            transform.rotation = Quaternion.LookRotation(ray.direction); // 准星面正对射线
            if (dot != null)
            {
                var s = distance.Value * DotScalePerMeter;
                dot.localScale = new Vector3(s, 0.001f, s);
            }

            TickPulse();
        }

        /// <summary>视线与地面（y=0）交点距离</summary>
        private static bool GroundDistance(Ray ray, out float distance)
        {
            distance = 0f;
            if (Mathf.Approximately(ray.direction.y, 0f))
            {
                return false;
            }
            var t = -ray.origin.y / ray.direction.y;
            if (t <= 0f)
            {
                return false;
            }
            distance = t;
            return true;
        }

        private void TickPulse()
        {
            if (pulseRemaining <= 0f)
            {
                return;
            }
            pulseRemaining -= Time.deltaTime;
            if (pulseRemaining <= 0f)
            {
                pulse.gameObject.SetActive(false);
                return;
            }
            var t = 1f - pulseRemaining / PulseSeconds;
            var s = Mathf.Lerp(PulseStartScale, PulseEndScale, t);
            pulse.localScale = new Vector3(s, 0.002f, s); // 盘面预转 90°，厚度沿局部 Y
        }

        public void Hit(bool weakpoint, bool killed)
        {
            if (pulse == null || pulseRenderer == null)
            {
                return;
            }
            pulseRenderer.sharedMaterial = weakpoint && killed ? pulseGoldMaterial : pulseWhiteMaterial;
            pulse.localScale = new Vector3(PulseStartScale, 0.002f, PulseStartScale);
            pulse.gameObject.SetActive(true);
            pulseRemaining = PulseSeconds;
        }
    }
}
