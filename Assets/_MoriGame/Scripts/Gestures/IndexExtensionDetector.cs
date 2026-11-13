using UnityEngine;

namespace MoriGame.Gestures
{
    /// <summary>食指伸直判定（docs/07 2026-11-03 定案"食指伸直=开火"，纯 C# 可单测）：
    /// 指向向量（食指近节→食指尖）与掌基向量（腕→食指近节）的夹角小于阈值判"伸直"。
    /// 2026-11-04 真机修正：根关节从掌指根（metacarpal）改为食指近节——掌骨沿掌基走向，
    /// 掌指根做根时自然伸直夹角实测 30—40°（需指尖上翘才触发）；近节根下自然放松伸直夹角 10—20°。
    /// 完全伸直时食指沿掌基方向延伸（夹角 ≈0—15°）；握拳/半屈（扣扳机姿态）时指尖折向掌心（夹角 60°+）。</summary>
    public static class IndexExtensionDetector
    {
        /// <summary>夹角判定（严格小于 maxAngleDeg）；任一向量长度 <1cm（数据退化）返回 false。</summary>
        public static bool IsExtended(Vector3 wrist, Vector3 indexRoot, Vector3 indexTip, float maxAngleDeg)
        {
            var fingerDir = indexTip - indexRoot;
            var baseDir = indexRoot - wrist;
            if (fingerDir.sqrMagnitude < 0.0001f || baseDir.sqrMagnitude < 0.0001f)
            {
                return false;
            }
            return Vector3.Angle(fingerDir, baseDir) < maxAngleDeg;
        }
    }
}
