using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Combat
{
    /// <summary>玩家设施碰撞体过滤（2026-11-09 用户实测：高台制下工作台/高塔/补给箱贴在枪口正前方，
    /// 单发 Physics.Raycast 全被自 家设施吃掉，僵尸打不到）。枪械结算与准星射线统一走本过滤器：
    /// RaycastAll 取首个**非玩家设施**命中，设施碰撞体（工作台/高塔/补给箱）对射击透明。</summary>
    public static class PlayerStationFilter
    {
        private static readonly HashSet<Collider> StationColliders = new HashSet<Collider>();
        private static bool collected;

        public static void EnsureCollected()
        {
            if (collected)
            {
                return;
            }
            collected = true;
            Collect("Workbench");
            Collect("SupplyCrate");
            var match = MatchBootstrap.Instance;
            var core = match != null ? match.coreDevice : null;
            if (core != null)
            {
                var collider = core.GetComponent<Collider>();
                if (collider != null)
                {
                    StationColliders.Add(collider);
                }
            }
            if (match != null && match.playerRig != null)
            {
                foreach (var collider in match.playerRig.GetComponentsInChildren<Collider>(true))
                {
                    StationColliders.Add(collider);
                }
            }
        }

        private static void Collect(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }
            // 全部子碰撞体（悬浮锚点抓取盒/枪模等——2026-11-12 用户实测锚点碰撞体挡子弹）
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                StationColliders.Add(collider);
            }
        }

        /// <summary>射线取首个非玩家设施命中；全部命中均被过滤则返回 false。</summary>
        public static bool Raycast(Ray ray, float maxMeters, out RaycastHit hit)
        {
            EnsureCollected();
            var hits = Physics.RaycastAll(ray, maxMeters);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var candidate in hits)
            {
                if (StationColliders.Contains(candidate.collider))
                {
                    continue;
                }
                hit = candidate;
                return true;
            }
            hit = default;
            return false;
        }
    }
}
