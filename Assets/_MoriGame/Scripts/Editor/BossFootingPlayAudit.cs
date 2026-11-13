using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MoriGame.Waves;
using MoriGame.Zombies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoriGame.Editor
{
    public static class BossFootingPlayAudit
    {
        private const string DonePath = "Builds/boss-footing-audit.done";
        private const string ReportPath = "Builds/boss-footing-report.txt";
        private const string ArmedPath = "Builds/boss-footing-audit.armed";
        private static bool launched;
        private static StringBuilder report;

        public static void Run()
        {
            launched = false;
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
            new GameObject("BossFootingPlayAudit").AddComponent<AuditRunner>();
        }

        private sealed class AuditRunner : MonoBehaviour
        {
            private void Start()
            {
                StartCoroutine(Drive());
            }

            private IEnumerator Drive()
            {
                var steps = RunAudit();
                while (true)
                {
                    bool next;
                    try
                    {
                        next = steps.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Finish(false, e.ToString());
                        yield break;
                    }
                    if (!next)
                    {
                        yield break;
                    }
                    yield return steps.Current;
                }
            }
        }

        private static IEnumerator RunAudit()
        {
            yield return null;
            var director = UnityEngine.Object.FindFirstObjectByType<WaveDirector>();
            if (director != null)
            {
                director.enabled = false;
            }
            var devSpawner = UnityEngine.Object.FindFirstObjectByType<ZombieDevSpawner>();
            if (devSpawner != null)
            {
                devSpawner.enabled = false;
            }
            var pool = UnityEngine.Object.FindFirstObjectByType<ZombiePool>();
            var match = MatchBootstrap.Instance;
            if (pool == null || match == null || match.playSpaceAnchor == null)
            {
                Finish(false, "missing Pool/Match/playSpaceAnchor");
                yield break;
            }
            pool.ClearAll();
            report = new StringBuilder();
            report.AppendLine("Boss footing: heights are relative to the NavMesh zombie root; baked=skinned vertices, bounds=renderer AABB.");
            var origin = match.playSpaceAnchor.position;
            foreach (var id in new[] { "Normal", "Runner", "ShieldGuard", "Brute", "Overseer" })
            {
                var zombie = pool.Spawn(id, origin + new Vector3(0f, 0f, 9f));
                if (zombie == null)
                {
                    Finish(false, "spawn failed " + id);
                    yield break;
                }
                yield return null;
                report.AppendLine($"[{id}] scale={zombie.visualRoot.localScale:F3} visualY={zombie.visualRoot.localPosition.y:F4} " +
                                  $"rootY={zombie.transform.position.y:F4} avatar={zombie.animator.avatar.name} human={zombie.animator.isHuman}");
                yield return SamplePhase(zombie, "Walk", 5f, true);
                zombie.ApplyHit(999999, false, zombie.transform.position + zombie.transform.forward, true);
                yield return SamplePhase(zombie, "Death", 1.82f, false);
                pool.Return(zombie);
                yield return null;
            }
            Finish(true, "measured Normal control + four Boss models Walk+Death");
        }

        private static IEnumerator SamplePhase(ZombieAgent zombie, string phase, float seconds, bool stopAfterWalkCycle)
        {
            var started = Time.realtimeSinceStartup;
            var nextSample = 0f;
            var min = float.MaxValue;
            var max = float.MinValue;
            var boundsMin = float.MaxValue;
            var boundsMax = float.MinValue;
            var samples = 0;
            while (Time.realtimeSinceStartup - started < seconds && zombie.gameObject.activeSelf)
            {
                var elapsed = Time.realtimeSinceStartup - started;
                if (elapsed >= nextSample)
                {
                    Measure(zombie, out var baked, out var bounds);
                    min = Mathf.Min(min, baked);
                    max = Mathf.Max(max, baked);
                    boundsMin = Mathf.Min(boundsMin, bounds);
                    boundsMax = Mathf.Max(boundsMax, bounds);
                    samples++;
                    var state = zombie.animator.GetCurrentAnimatorStateInfo(0);
                    var leftFoot = zombie.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                    var hips = zombie.animator.GetBoneTransform(HumanBodyBones.Hips);
                    report.AppendLine($"{id(zombie)} {phase} t={elapsed:F2} state={state.shortNameHash} nt={state.normalizedTime:F3} " +
                                      $"baked={baked:F4} bounds={bounds:F4} visualY={zombie.visualRoot.localPosition.y:F4} " +
                                      $"hipsY={(hips != null ? hips.position.y - zombie.transform.position.y : 0f):F4} " +
                                      $"leftFootY={(leftFoot != null ? leftFoot.position.y - zombie.transform.position.y : 0f):F4}");
                    nextSample += 0.15f;
                    if (stopAfterWalkCycle && elapsed >= 1f && state.normalizedTime >= 1.05f &&
                        (state.IsName("WalkA") || state.IsName("WalkB")))
                    {
                        break;
                    }
                }
                yield return null;
            }
            report.AppendLine($"{id(zombie)} {phase} SUMMARY samples={samples} bakedMin={min:F4} bakedMax={max:F4} " +
                              $"boundsMin={boundsMin:F4} boundsMax={boundsMax:F4} death={death}");
        }

        private static string id(ZombieAgent zombie)
        {
            return zombie.config != null ? zombie.config.zombieId : zombie.name;
        }

        private static void Measure(ZombieAgent zombie, out float bakedMin, out float boundsMin)
        {
            bakedMin = float.MaxValue;
            boundsMin = float.MaxValue;
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            foreach (var renderer in zombie.visualRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (!renderer.enabled)
                {
                    continue;
                }
                boundsMin = Mathf.Min(boundsMin, renderer.bounds.min.y - zombie.transform.position.y);
                if (!(renderer is SkinnedMeshRenderer skinned) || skinned.sharedMesh == null)
                {
                    continue;
                }
                skinned.BakeMesh(baked);
                baked.GetVertices(vertices);
                for (var i = 0; i < vertices.Count; i++)
                {
                    var world = skinned.transform.TransformPoint(vertices[i]);
                    bakedMin = Mathf.Min(bakedMin, world.y - zombie.transform.position.y);
                }
            }
            UnityEngine.Object.Destroy(baked);
            if (bakedMin == float.MaxValue)
            {
                bakedMin = boundsMin;
            }
        }

        private static void Finish(bool ok, string details)
        {
            launched = false;
            Directory.CreateDirectory("Builds");
            if (File.Exists(ArmedPath))
            {
                File.Delete(ArmedPath);
            }
            if (report != null)
            {
                File.WriteAllText(ReportPath, report.ToString());
            }
            File.WriteAllText(DonePath, "EXITCODE=" + (ok ? "0" : "1") + " " +
                                      details.Replace('\n', ' ').Replace('\r', ' ') +