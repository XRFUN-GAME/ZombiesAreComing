using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>编辑器内触发文件执行器（CLAUDE.md 2026-11-04 测试执行规则：编辑器开着时禁止批处理链——
    /// -batchmode 与 GUI 编辑器抢工程锁。改为外部投递 Builds/*.trigger，本组件 1 秒轮询拾取后
    /// 在当前打开的编辑器内执行并写回执，编辑器全程不关）：
    /// config-gen.trigger → AssetDatabase.Refresh + ConfigAssetGenerator.GenerateAll → config-gen.done
    /// tests.trigger / run-tests.trigger → Refresh + TestRunnerApi 全量 EditMode → editmode-results.xml + tests.done
    /// scene-setup.trigger → Refresh + MoriSceneSetup 重搭并保存场景 → scene-setup.done（不出 APK）
    /// zombie-animation-audit.trigger → MoriMain 自动 Play 审计六模型贴地/行走/受击/死亡/回收
    /// build-apk.trigger → Refresh + BuildScript.BuildAndroidAPK（重搭场景+Dev APK+build-report）→ build-apk.done
    /// 每步先 AssetDatabase.Refresh：已聚焦的编辑器不会自动编译磁盘代码变更（只在切回编辑器瞬间刷新），
    /// 不刷新就会拿旧程序集跑测试。载入时写 trigger-watcher.ready 供外部确认看门狗已就位。</summary>
    [InitializeOnLoad]
    public static class TestRunTrigger
    {
        private const string GenTrigger = "Builds/config-gen.trigger";
        private const string GenDone = "Builds/config-gen.done";
        private const string TestsTrigger = "Builds/tests.trigger";
        private const string TestsTriggerAlt = "Builds/run-tests.trigger";
        private const string TestsDone = "Builds/tests.done";
        private const string SceneTrigger = "Builds/scene-setup.trigger";
        private const string SceneDone = "Builds/scene-setup.done";
        private const string ZombieAuditTrigger = "Builds/zombie-animation-audit.trigger";
        private const string ZombieAuditDone = "Builds/zombie-animation-audit.done";
        private const string BossFootingTrigger = "Builds/boss-footing-audit.trigger";
        private const string BossFootingDone = "Builds/boss-footing-audit.done";
        private const string RecordTrigger = "Builds/record-scene.trigger";
        private const string RecordDone = "Builds/record-scene.done";
        private const string MigrateTrigger = "Builds/migrate-realspace.trigger";
        private const string MigrateDone = "Builds/migrate-realspace.done";
        private const string LargeUiTrigger = "Builds/large-ui.trigger";
        private const string LargeUiDone = "Builds/large-ui.done";
        private const string SwapGunTrigger = "Builds/swap-gun.trigger";
        private const string SwapGunDone = "Builds/swap-gun.done";
        private const string VfxShaderTrigger = "Builds/vfx-shader.trigger";
        private const string VfxShaderDone = "Builds/vfx-shader.done";
        private const string VfxProbeTrigger = "Builds/vfx-probe.trigger";
        private const string VfxProbeDone = "Builds/vfx-probe.done";
        private const string TutorialBuildTrigger = "Builds/build-tutorial.trigger";
        private const string TutorialBuildDone = "Builds/build-tutorial.done";
        private const string PreviewUiTrigger = "Builds/preview-startui.trigger";
        private const string PreviewUiDone = "Builds/preview-startui.done";
        private const string FixedUiPreviewTrigger = "Builds/preview-fixedui.trigger";
        private const string FixedUiPreviewDone = "Builds/preview-fixedui.done";
        private const string BuildTrigger = "Builds/build-apk.trigger";
        private const string BuildDone = "Builds/build-apk.done";
        private const string ReadyFile = "Builds/trigger-watcher.ready";

        private static double nextPoll;
        // primed 两拍模式：Refresh 的编译是异步排队的（本次实测：Refresh 返回时 isCompiling 才置位），
        // 第一拍只 Refresh 让编译排队并立即返回；下一拍确认不编译才删触发执行。
        // 若期间域重载：静态清零 + 触发文件幸存 → 重载后重新两拍，必然跑在新程序集上。
        private static bool genPrimed;
        private static bool testsPrimed;
        private static bool scenePrimed;
        private static bool zombieAuditPrimed;
        private static bool bossFootingPrimed;
        private static bool buildPrimed;
        private static bool recordPrimed;
        private static bool migratePrimed;
        private static bool largeUiPrimed;
        private static bool swapGunPrimed;
        private static bool vfxShaderPrimed;
        private static bool vfxProbePrimed;
        private static bool tutorialBuildPrimed;
        private static bool previewUiPrimed;
        private static bool fixedUiPreviewPrimed;

        static TestRunTrigger()
        {
            try
            {
                Directory.CreateDirectory("Builds");
                File.WriteAllText(ReadyFile, "ready " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] trigger 看门狗就绪标记写入失败：" + e.Message);
            }
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
            {
                return;
            }
            nextPoll = EditorApplication.timeSinceStartup + 1.0;
            if (EditorApplication.isPlaying)
            {
                ZombieAnimationPlayAudit.Tick();
                BossFootingPlayAudit.Tick();
                VfxRenderProbeAudit.Tick(); // 特效渲染探针 Play 态驱动
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            if (File.Exists(GenTrigger))
            {
                RunConfigGen();
            }
            else if (File.Exists(TestsTrigger) || File.Exists(TestsTriggerAlt))
            {
                RunTests();
            }
            else if (File.Exists(SceneTrigger))
            {
                RunSceneSetup();
            }
            else if (File.Exists(ZombieAuditTrigger))
            {
                RunZombieAudit();
            }
            else if (File.Exists(BossFootingTrigger))
            {
                RunBossFootingAudit();
            }
            else if (File.Exists(RecordTrigger))
            {
                RunRecord();
            }
            else if (File.Exists(MigrateTrigger))
            {
                RunMigration();
            }
            else if (File.Exists(LargeUiTrigger))
            {
                RunLargeUiMigration();
            }
            else if (File.Exists(SwapGunTrigger))
            {
                RunSwapGun();
            }
            else if (File.Exists(VfxShaderTrigger))
            {
                RunVfxShader();
            }
            else if (File.Exists(VfxProbeTrigger))
            {
                RunVfxProbe();
            }
            else if (File.Exists(TutorialBuildTrigger))
            {
                RunTutorialBuild();
            }
            else if (File.Exists(PreviewUiTrigger))
            {
                RunPreviewUi();
            }
            else if (File.Exists(FixedUiPreviewTrigger))
            {
                RunFixedUiPreview();
            }
            else if (File.Exists(BuildTrigger))
            {
                RunBuild();
            }
            else
            {
                genPrimed = false; // 无触发时清 primed，下次触发重新走两拍
                testsPrimed = false;
                scenePrimed = false;
                zombieAuditPrimed = false;
                bossFootingPrimed = false;
                buildPrimed = false;
                recordPrimed = false;
                migratePrimed = false;
                largeUiPrimed = false;
                swapGunPrimed = false;
                vfxShaderPrimed = false;
                vfxProbePrimed = false;
                tutorialBuildPrimed = false;
                previewUiPrimed = false;
                fixedUiPreviewPrimed = false;
            }
        }

        private static void RunConfigGen()
        {
            if (!genPrimed)
            {
                genPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(GenTrigger);
            try
            {
                ConfigAssetGenerator.GenerateAll(); // GUI 下 inBatchMode=false 不会 Exit
                Done(GenDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(GenDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunTests()
        {
            if (!testsPrimed)
            {
                testsPrimed = true;
                ForceImportScripts();
                return; // 编译+域重载后：静态清零、触发幸存、重新两拍——最终必跑最新程序集
            }
            SafeDelete(TestsTrigger);
            SafeDelete(TestsTriggerAlt);
            SafeDelete(TestsDone);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(ScriptableObject.CreateInstance<TriggerCallbacks>()); // ScriptableObject 回调可跨域重载
            Debug.Log("[MoriGame] 触发链调用 TestRunnerApi.Execute（EditMode 全量）");
            try
            {
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
            }
            catch (Exception e)
            {
                Done(TestsDone, "EXITCODE=1 " + OneLine(e));
                Debug.LogException(e);
            }
        }

        private static void RunSceneSetup()
        {
            if (!scenePrimed)
            {
                scenePrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(SceneTrigger);
            try
            {
                EditorSceneManager.SaveOpenScenes();
                MoriSceneSetup.SetupGreyboxScene();
                Done(SceneDone, "EXITCODE=0 " + MoriSceneSetup.SetupVersion + " " + Stamp());
            }
            catch (Exception e)
            {
                Done(SceneDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunZombieAudit()
        {
            if (!zombieAuditPrimed)
            {
                zombieAuditPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(ZombieAuditTrigger);
            SafeDelete(ZombieAuditDone);
            try
            {
                ZombieAnimationPlayAudit.Run();
            }
            catch (Exception e)
            {
                Done(ZombieAuditDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunBossFootingAudit()
        {
            if (!bossFootingPrimed)
            {
                bossFootingPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(BossFootingTrigger);
            SafeDelete(BossFootingDone);
            try
            {
                BossFootingPlayAudit.Run();
            }
            catch (Exception e)
            {
                Done(BossFootingDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunRecord()
        {
            if (!recordPrimed)
            {
                recordPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(RecordTrigger);
            try
            {
                SceneRecordAudit.Run(); // 只读：保存+备份+转储
                Done(RecordDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(RecordDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunMigration()
        {
            if (!migratePrimed)
            {
                migratePrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(MigrateTrigger);
            try
            {
                RealSpaceMigration.Run(); // 增量迁移（不全量重搭）：锚点接线+塔+隐形地板+导航重烘
                Done(MigrateDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(MigrateDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunLargeUiMigration()
        {
            if (!largeUiPrimed)
            {
                largeUiPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(LargeUiTrigger);
            try
            {
                LargeUiMigration.Run();
                Done(LargeUiDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(LargeUiDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunSwapGun()
        {
            if (!swapGunPrimed)
            {
                swapGunPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(SwapGunTrigger);
            try
            {
                GunModelSwap.Run(); // 枪模整体替换（保持预制体链接，位姿/枪口/插槽链路不动）
                Done(SwapGunDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(SwapGunDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunVfxShader()
        {
            if (!vfxShaderPrimed)
            {
                vfxShaderPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(VfxShaderTrigger);
            try
            {
                VfxShaderConverter.ConvertAll(); // PolygonParticles 材质批量转 URP + 抽查三 prefab
                Done(VfxShaderDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(VfxShaderDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunVfxProbe()
        {
            if (!vfxProbePrimed)
            {
                vfxProbePrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(VfxProbeTrigger);
            VfxRenderProbeAudit.Run(); // 进 Play 跑渲染探针（写 vfx-probe.done/log，Poll 每 tick 驱动 Tick 挂 Runner）
        }

        private static void RunPreviewUi()
        {
            if (!previewUiPrimed)
            {
                previewUiPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(PreviewUiTrigger);
            try
            {
                TutorialScenesBuilder.PreviewStartUi(); // StartScene UI 编辑器预览烘焙（免 Play 查看）
                Done(PreviewUiDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(PreviewUiDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunFixedUiPreview()
        {
            if (!fixedUiPreviewPrimed)
            {
                fixedUiPreviewPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(FixedUiPreviewTrigger);
            SafeDelete(FixedUiPreviewDone);
            try
            {
                WorldUiPreviewMigration.Run();
                Done(FixedUiPreviewDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(FixedUiPreviewDone, "EXITCODE=1 " + OneLine(e));
                Debug.LogException(e);
            }
        }

        private static void RunTutorialBuild()
        {
            if (!tutorialBuildPrimed)
            if (!tutorialBuildPrimed)
            {
                tutorialBuildPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(TutorialBuildTrigger);
            try
            {
                TutorialScenesBuilder.Build(); // Tutorial/StartScene 场景 + Build Settings 顺序
                Done(TutorialBuildDone, "EXITCODE=0 " + Stamp());
            }
            catch (Exception e)
            {
                Done(TutorialBuildDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        private static void RunBuild()
        {
            if (!buildPrimed)
            {
                buildPrimed = true;
                ForceImportScripts();
                return;
            }
            SafeDelete(BuildTrigger);
            try
            {
                EditorSceneManager.SaveOpenScenes(); // 与菜单入口一致：防重开 MoriMain 丢失未存改动
                var ok = BuildScript.BuildAndroidAPK(); // GUI 下不 Exit；重搭场景+APK+写 build-report.txt
                Done(BuildDone, (ok ? "EXITCODE=0" : "EXITCODE=1") + " " + Stamp());
            }
            catch (Exception e)
            {
                Done(BuildDone, "EXITCODE=1 " + OneLine(e));
            }
        }

        /// <summary>强制导入**有变更**的脚本源（2026-11-04 两坑叠加）：①编辑器聚焦时 Refresh 不重扫目录（OS 目录监视
        /// 捕获不到外部进程写入）→ 用 ImportAsset 绕过扫描；②但 no-op ImportAsset 也会被 Unity 视为"变化"
        /// → 空编译请求风暴（实测 9 次/秒）→ 必须**按 mtime 过滤**：只导比最新程序集新的文件，
        /// 无磁盘变更时零导入零编译请求。新文件的 mtime 必然新于 DLL，覆盖"新文件不被捕获"场景。</summary>
        private static void ForceImportScripts()
        {
            try
            {
                AssetDatabase.Refresh(); // 无害保留：有时它能工作
                var dllDir = Path.Combine(Application.dataPath, "..", "Library", "ScriptAssemblies");
                var newestDll = DateTime.MinValue;
                if (Directory.Exists(dllDir))
                {
                    foreach (var dll in Directory.GetFiles(dllDir, "*.dll"))
                    {
                        var t = File.GetLastWriteTime(dll);
                        if (t > newestDll)
                        {
                            newestDll = t;
                        }
                    }
                }
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, "_MoriGame"));
                foreach (var pattern in new[] { "*.cs", "*.asmdef" })
                {
                    foreach (var file in Directory.GetFiles(root, pattern, SearchOption.AllDirectories))
                    {
                        if (File.GetLastWriteTime(file) <= newestDll)
                        {
                            continue; // 未变更：不导入（防 no-op 导入触发空编译循环）
                        }
                        var rel = "Assets" + file.Replace('\\', '/').Substring(Application.dataPath.Length);
                        AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceSynchronousImport);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] 强制导入脚本失败：" + e.Message);
            }
        }

        private static string Stamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private static string OneLine(Exception e)
        {
            return (e.Message + " @ " + e.GetType().Name).Replace('\n', ' ').Replace('\r', ' ');
        }

        private static void Done(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] 触发回执写入失败 " + path + "：" + e.Message);
            }
        }

        private static void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] 触发文件删除失败 " + path + "：" + e.Message);
            }
        }
    }

    /// <summary>TestRunnerApi 回调（ScriptableObject 跨域重载）：跑完写 editmode-results.xml（summary 属性
    /// 兼容既有判绿 grep）+ tests.done（EXITCODE/total/passed/failed，红时附失败用例名清单）。</summary>
    public class TriggerCallbacks : ScriptableObject, ICallbacks
    {
        public void RunStarted(ITestAdaptor testsToRun)
        {
            Debug.Log($"[MoriGame] EditMode 测试开始：{testsToRun.TestCaseCount} 条");
        }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            var passed = result.TestStatus == TestStatus.Passed;
            var total = result.PassCount + result.FailCount; // ITestResultAdaptor 无 TotalCount；EditMode 全量无跳过用例，和即总数
            try
            {
                Directory.CreateDirectory("Builds");
                File.WriteAllText("Builds/editmode-results.xml",
                    "<test-run result=\"" + (passed ? "Passed" : "Failed") + "\" total=\"" + total +
                    "\" passed=\"" + result.PassCount + "\" failed=\"" + result.FailCount + "\" />\n");
                var done = "EXITCODE=" + (passed ? 0 : 2) + " total=" + total +
                           " passed=" + result.PassCount + " failed=" + result.FailCount +
                           " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                if (!passed)
                {
                    done += " failed_tests=" + CollectFailed(result);
                }
                File.WriteAllText("Builds/tests.done", done);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] 测试回执写入失败：" + e.Message);
            }
        }

        private static string CollectFailed(ITestResultAdaptor result, int depth = 0)
        {
            if (depth > 6)
            {
                return "";
            }
            var names = "";
            if (result.TestStatus == TestStatus.Failed && !result.HasChildren)
            {
                var msg = (result.Message ?? "").Replace('\n', ' ').Replace('\r', ' ');
                names = result.FullName + "[" + msg + "];";
            }
            if (result.HasChildren)
            {
                foreach (var child in result.Children)
                {
                    names += CollectFailed(child, depth + 1);
                }
            }
            return names.Length > 900 ? names.Substring(0, 900) : names;
        }
    }
}
