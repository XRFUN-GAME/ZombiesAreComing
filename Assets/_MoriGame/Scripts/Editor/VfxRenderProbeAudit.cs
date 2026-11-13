using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>特效渲染探针审计（2026-11-11 诊断真机全没特效，模式同 ZombieAnimationPlayAudit）：
    /// Play 模式在主相机前实例化 muzzleShot + 池路径对照，三向证据（粒子存活/isVisible/渲染像素）写 done。</summary>
    public static class VfxRenderProbeAudit
    {
        private const string ArmedPath = "Builds/vfx-probe.armed";
        private const string DonePath = "Builds/vfx-probe.done";
        private static bool launched;
        private static readonly List<string> Lines = new List<string>();

        public static void Run()
        {
            launched = false;
            Lines.Clear();
            Directory.CreateDirectory("Builds");
            EditorApplication.isPaused = false;
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/MoriMain.unity");
            File.WriteAllText(ArmedPath, "run");
            EditorApplication.EnterPlaymode();
        }

        public static void Tick()
        {
            if (!EditorApplication.isPlaying || !File.Exists(ArmedPath))
            {
                launched = false;
                return;
            }
            if (launched)
            {
                return;
            }
            launched = true;
            new GameObject("VfxRenderProbeRunner").AddComponent<ProbeRunner>();
        }

        private sealed class ProbeRunner : MonoBehaviour
        {
            private void Start()
            {
                StartCoroutine(Drive());
            }

            private IEnumerator Drive()
            {
                var steps = VfxRenderProbe.Run(line => Lines.Add(line));
                while (true)
                {
                    bool next;
                    try
                    {
                        next = steps.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Finish(false, e.ToString().Replace('\n', ' ').Replace('\r', ' '));
                        yield break;
                    }
                    if (!next)
                    {
                        yield break;
                    }
                    yield return steps.Current;
                }
            }

            private void OnDisable()
            {
                Flush("runner-disabled");
            }

            private void OnDestroy()
            {
                Flush("runner-destroyed");
            }
        }

        public static void Finish(bool ok, string details)
        {
            Flush(details);
            launched = false;
            Directory.CreateDirectory("Builds");
            if (File.Exists(ArmedPath))
            {
                File.Delete(ArmedPath);
            }
            File.WriteAllText(DonePath, "EXITCODE=" + (ok ? "0" : "1") + " " + details + " " +
                                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (EditorApplication.isPlaying)
            {
                EditorApplication.ExitPlaymode();
            }
        }

        private static void Flush(string reason)
        {
            if (Lines.Count == 0)
            {
                return;
            }
            File.AppendAllText("Builds/vfx-probe.log", "=== " + reason + " " +
                              DateTime.Now.ToString("HH:mm:ss") + " ===\n" + string.Join("\n", Lines) + "\n");
            Lines.Clear();
        }
    }
}
