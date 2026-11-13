namespace MoriGame.Tutorial
{
    /// <summary>教学关会话硬闸（2026-11-12 用户定版：教学关不允许任何僵尸攻击玩家）：
    /// TutorialFlow 进场置位/退场清零；ZombieAgent 据此全系统禁索敌/移动/攻击——
    /// 与 dummyMode 双保险，即使正式关逻辑泄漏进教学关也无法伤到玩家。</summary>
    public static class TutorialSession
    {
        public static bool Active;
    }
}
