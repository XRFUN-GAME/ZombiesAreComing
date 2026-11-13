namespace MoriGame.Editor
{
    /// <summary>把 MoriMain/Tutorial 的旧序列化 HUD 升级为远距侧向大版；StartScene 不在迁移范围内。</summary>
    public static class LargeUiMigration
    {
        public static void Run()
        {
            WorldUiPreviewMigration.Run();
        }
    }
}
