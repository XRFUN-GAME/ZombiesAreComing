using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;

namespace MoriGame.Editor
{
    public static class BuildScript
    {
        private const string ProjectRoot = "F:/morisinglegame";
        private const string SceneDir = "Assets/_MoriGame/Scenes";
        private const string ScenePath = SceneDir + "/MoriMain.unity";
        private const string BuildDir = ProjectRoot + "/Builds/Android";
        private const string ApkPath = BuildDir + "/MoriGame-Dev.apk";
        private const string ReportPath = BuildDir + "/build-report.txt";

        [MenuItem("MoriGame/构建Dev包（编辑器内）并安装")]
        public static void BuildAndroidAPKFromMenu()
        {
            // 编辑器内构建：先保存当前场景改动，避免 SetupGreyboxScene 重开 MoriMain 时丢失
            EditorSceneManager.SaveOpenScenes();
            if (BuildAndroidAPK())
            {
                InstallToConnectedDevice(); // 构建成功才装；无设备连接则跳过（2026-11-02 约定）
            }
        }

        public static bool BuildAndroidAPK()
        {
            var lines = new System.Collections.Generic.List<string>();
            try
            {
                EnsureScene(); // 仅当场景文件缺失时兜底建空场景
                EditorSceneManager.SaveOpenScenes(); // 用户手工场景原样出包——全量重搭已被闸门禁用（2026-11-06）
                lines.Add("scene=user-maintained");
                lines.Add("savedAt=" + File.GetLastWriteTime(ScenePath).ToString("yyyy-MM-dd HH:mm:ss"));
                ConfigurePlayerSettings();
                ConfigureXrLoader(lines);

                Directory.CreateDirectory(BuildDir);

                var options = new BuildPlayerOptions
                {
                    // M4-T0：Build Settings 场景链 StartScene → Tutorial → MoriMain（未配置时兜底主场景单场景）
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray()
                        is { Length: > 0 } buildScenes
                        ? buildScenes
                        : new[] { ScenePath },
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    locationPathName = ApkPath,
                    options = BuildOptions.Development
                };
                var report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                lines.Add($"result={summary.result}");
                lines.Add($"errors={summary.totalErrors}");
                lines.Add($"size={summary.totalSize}");
                lines.Add($"time={summary.totalTime}");
                Debug.Log($"[MoriGame] build finished: {summary.result}, errors={summary.totalErrors}, size={summary.totalSize}");
                WriteReport(lines);
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
                }
                return summary.result == BuildResult.Succeeded;
            }
            catch (Exception e)
            {
                lines.Add("exception=" + e);
                WriteReport(lines);
                Debug.LogException(e);
                if (UnityEditorInternal.InternalEditorUtility.inBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                return false;
            }
        }

        /// <summary>adb 安装到已连接设备；无设备连接则不做事（adb 路径取编辑器自带 Android 模块）</summary>
        private static void InstallToConnectedDevice()
        {
            var editorDir = Path.GetDirectoryName(EditorApplication.applicationPath);
            var adb = Path.Combine(editorDir ?? "", "Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe");
            if (!File.Exists(adb))
            {
                Debug.LogWarning("[MoriGame] 找不到 adb：" + adb);
                return;
            }
            var devices = RunProcess(adb, "devices");
            if (devices == null || !devices.Contains("\tdevice"))
            {
                Debug.Log("[MoriGame] 无 adb 设备连接，跳过安装");
                return;
            }
            var result = RunProcess(adb, "install -r \"" + ApkPath + "\"");
            Debug.Log("[MoriGame] adb install → " + (result != null && result.Contains("Success") ? "Success" : result));
        }

        private static string RunProcess(string exe, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return output;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] 进程执行失败 " + exe + " " + args + "：" + e.Message);
                return null;
            }
        }

        private static void EnsureScene()
        {
            if (File.Exists(ScenePath))
            {
                return;
            }
            Directory.CreateDirectory(SceneDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[MoriGame] created empty scene " + ScenePath);
        }

        private static void ConfigurePlayerSettings()
        {
            // 身份字段（公司/产品/包名/版本）以 ProjectSettings 为准，构建永不覆盖（用户手管区）
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        }

        private static void ConfigureXrLoader(System.Collections.Generic.List<string> lines)
        {
            try
            {
                var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
                if (general == null)
                {
                    lines.Add("xr=no-general-settings-android");
                    return;
                }
                var manager = general.Manager;
                if (manager == null)
                {
                    lines.Add("xr=no-manager");
                    return;
                }
                foreach (var loader in manager.activeLoaders)
                {
                    lines.Add($"xr-active={loader.GetType().FullName}");
                }
                if (manager.activeLoaders.Count > 0)
                {
                    return;
                }
                var ok = XRPackageMetadataStore.AssignLoader(
                    manager, "ByteDance.PICO.XR.PXR_Loader", BuildTargetGroup.Android);
                lines.Add($"xr-assign={ok}");
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(general);
                AssetDatabase.SaveAssets();
                foreach (var loader in manager.activeLoaders)
                {
                    lines.Add($"xr-active-after={loader.GetType().FullName}");
                }
            }
            catch (Exception e)
            {
                lines.Add("xr-config-failed=" + e.Message);
            }
        }

        private static void WriteReport(System.Collections.Generic.List<string> lines)
        {
            try
            {
                Directory.CreateDirectory(BuildDir);
                File.WriteAllLines(ReportPath, lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MoriGame] failed to write build report: " + e.Message);
            }
        }
    }
}
