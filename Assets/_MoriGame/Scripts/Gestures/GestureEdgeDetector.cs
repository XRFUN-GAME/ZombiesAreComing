namespace MoriGame.Gestures
{
    /// <summary>边沿检测（纯逻辑可单测）：Rising 在按下帧为 true，整帧有效，下一帧 Update 自动清除。
    /// 多个订阅方可在同一帧重复读取。</summary>
    public class GestureEdgeDetector
    {
        private bool wasDown;
        private bool rising;

        public bool Rising => rising;

        public void Update(bool down)
        {
            rising = down && !wasDown;
            wasDown = down;
        }
    }
}
