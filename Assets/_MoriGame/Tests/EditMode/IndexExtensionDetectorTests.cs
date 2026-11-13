using MoriGame.Gestures;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    // 2026-11-03 射击手势改版（docs/07 定案"食指伸直=开火"）：
    // 夹角法纯逻辑——指向向量（食指近节→指尖）与掌基向量（腕→食指近节）夹角 < 阈值（30°）判伸直。
    // 2026-11-04 真机修正：根关节掌指根→食指近节（掌指根实测伸直夹角 30—40°，需指尖上翘才触发）。
    // 几何约定：腕在原点、食指近节沿 +Z 8cm，指尖绕根偏转构造各姿态。
    public class IndexExtensionDetectorTests
    {
        private const float MaxAngle = 30f;
        private static readonly Vector3 Wrist = Vector3.zero;
        private static readonly Vector3 Root = new Vector3(0f, 0f, 0.08f);

        private static Vector3 Tip(float offsetDeg, float fingerLength = 0.07f)
        {
            var r = offsetDeg * Mathf.Deg2Rad;
            return Root + new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)) * fingerLength;
        }

        [Test]
        public void 完全伸直_沿掌基方向_判定伸直()
        {
            Assert.IsTrue(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(0f), MaxAngle), "夹角 0°");
        }

        [Test]
        public void 自然指向前方_偏转10度_判定伸直()
        {
            Assert.IsTrue(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(10f), MaxAngle), "10° < 30°");
        }

        [Test]
        public void 扣扳机半屈45度_不判伸直()
        {
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(45f), MaxAngle), "45° ≥ 30°");
        }

        [Test]
        public void 握拳_指尖折向掌心_不判伸直()
        {
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(120f), MaxAngle), "折回 >90°");
        }

        [Test]
        public void 阈值边界_29_5度内伸直_30_5度外不伸直()
        {
            Assert.IsTrue(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(29.5f), MaxAngle), "29.5° 严格小于 30°");
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(30.5f), MaxAngle), "30.5° 越过阈值");
        }

        [Test]
        public void 数据退化_零长度向量_返回不伸直()
        {
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Wrist, Root, Root, MaxAngle), "指尖==食指根（追踪退化）");
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Root, Root, Tip(0f), MaxAngle), "食指根==腕（追踪退化）");
        }

        [Test]
        public void 阈值可调_放宽到60度后半屈通过()
        {
            Assert.IsTrue(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(45f), 60f), "同姿态放宽阈值即伸直");
        }

        // 2026-11-04 真机修正回归（根关节=食指近节，阈值 30° 不变）：
        // 自然放松伸直实测夹角 10—20°（取中值 15° 断言）；扣扳机近节弯 ≈45° 必须判否。
        [Test]
        public void 真机姿态_近节根_自然伸直判真_扣扳机判假()
        {
            Assert.IsTrue(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(15f), MaxAngle),
                "自然放松伸直（近节夹角 10—20° 区间，30° 阈值直接命中）");
            Assert.IsFalse(IndexExtensionDetector.IsExtended(Wrist, Root, Tip(45f), MaxAngle),
                "扣扳机弯曲（近节弯 45°，跨过阈值停火）");
        }
    }
}
