using MoriGame.Territory;
using UnityEngine;

namespace MoriGame.Gestures
{
    /// <summary>凝视地面点计算（2026-11-09 锚点化）：头部射线 → 玩法地面平面（WorldFloorY，默认 0），
    /// 超过 maxMeters 沿水平方向截断，并夹回**以网格锚点为中心**的 ±arenaHalf 场地边界。
    /// 旧实现写死 y=0 平面与世界原点 ±13.4 边界——地图搬锚点（-45,35）后落点全被夹回老区域，
    /// 放置预览永远非法（实测"放置建筑没了"的根因）。</summary>
    public static class GazeUtil
    {
        public static bool TryGetGroundPoint(Transform head, float maxMeters, float arenaHalf, out Vector3 point)
        {
            point = Vector3.zero;
            if (head == null)
            {
                return false;
            }
            var dir = head.forward;
            if (dir.y > -0.08f)
            {
                return false; // 抬头/平视过平，无落点
            }
            var groundY = TerritoryGrid.WorldFloorY;
            var origin = TerritoryGrid.WorldOrigin;
            var t = (groundY - head.position.y) / dir.y;
            if (t <= 0f)
            {
                return false;
            }
            point = head.position + dir * t;
            var flat = new Vector3(point.x - head.position.x, 0f, point.z - head.position.z);
            if (flat.magnitude > maxMeters)
            {
                flat = flat.normalized * maxMeters;
                point = new Vector3(head.position.x + flat.x, groundY, head.position.z + flat.z);
            }
            point.x = Mathf.Clamp(point.x, origin.x - arenaHalf, origin.x + arenaHalf);
            point.z = Mathf.Clamp(point.z, origin.y - arenaHalf, origin.y + arenaHalf);
            point.y = groundY;
            return true;
        }
    }
}
