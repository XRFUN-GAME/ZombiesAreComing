using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Combat
{
    /// <summary>弹丸对象池（2026-11-02 真实弹丸整改，同僵尸池模式）：
    /// 灰盒球体直径 0.06 米，速度 25 米/秒直线无重力（2026-11-03 用户实测：80 米/秒看不清弹道，降速+加大），
    /// 起点→终点飞行，**到达回调结算**（命中判定由武器在开火时沿瞄准射线预计算，弹丸视觉与判定同路径——
    /// 降速后判定仍即时结算：视觉慢飞是刻意的可读性表现，命中结果不等弹丸到达）。
    /// 材质由 MoriSceneSetup 注入场景引用（T3.5 教训：不运行时 Shader.Find）。</summary>
    public class ProjectilePool : MonoBehaviour
    {
        public Material ballMaterial;
        public float speedMetersPerSecond = 37.5f; // 2026-11-11 用户：25×1.5（判定仍开火瞬间结算，弹丸纯视觉提速）
        public const int PoolCap = 48;

        private struct Active
        {
            public Transform Ball;
            public Vector3 From;
            public Vector3 To;
            public float Traveled;
            public System.Action OnArrive;
            // 拖尾走 VfxBus.Follow（挂池根防小尺度缩没，宿主失活自动回收，2026-11-11 修复弹丸冻结/特效不可见）
        }

        private readonly Stack<GameObject> inactive = new Stack<GameObject>();
        private readonly List<Active> active = new List<Active>();

        private void BuildOne()
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Projectile";
            Destroy(ball.GetComponent<Collider>()); // 无碰撞体：命中判定走射线，弹丸纯视觉
            ball.transform.localScale = Vector3.one * 0.06f; // 2026-11-03 加大弹丸（0.03→0.06），配合降速提高可读性
            ball.GetComponent<MeshRenderer>().sharedMaterial = ballMaterial;
            ball.transform.SetParent(transform, false);
            ball.SetActive(false);
            inactive.Push(ball);
        }

        public static Quaternion FlightRotation(Vector3 from, Vector3 to)
        {
            var direction = to - from;
            return direction.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;
        }

        public void Spawn(Vector3 from, Vector3 to, System.Action onArrive)
        {
            if (inactive.Count == 0)
            {
                if (active.Count >= PoolCap)
                {
                    return; // 池尽丢弃（3 发/秒 × 亚秒飞行，实际到不了上限）
                }
                BuildOne();
            }
            var ball = inactive.Pop();
            ball.transform.SetPositionAndRotation(from, FlightRotation(from, to));
            ball.SetActive(true);
            active.Add(new Active
            {
                Ball = ball.transform, From = from, To = to, Traveled = 0f, OnArrive = onArrive
            });
            Vfx.VfxBus.Follow(l => l.projectileTrail, ball.transform); // 拖尾随弹（回收免管）
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var p = active[i];
                var total = Vector3.Distance(p.From, p.To);
                p.Traveled += speedMetersPerSecond * dt;
                if (p.Traveled >= total)
                {
                    p.Ball.gameObject.SetActive(false); // 拖尾随失活自动回收，回收路径零 VFX 调用（2026-11-11 修复冻结）
                    inactive.Push(p.Ball.gameObject);
                    active.RemoveAt(i);
                    p.OnArrive?.Invoke(); // 到达即结算（伤害/脉冲随弹丸落点）
                    continue;
                }
                p.Ball.position = Vector3.Lerp(p.From, p.To, p.Traveled / Mathf.Max(total, 0.0001f));
                active[i] = p;
            }
        }
    }
}
