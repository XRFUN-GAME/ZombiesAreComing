using System;
using System.Collections;
using System.IO;
using MoriGame.Combat;
using MoriGame.Devices;
using MoriGame.Waves;
using MoriGame.Zombies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoriGame.Editor
{
    public static class ZombieAnimationPlayAudit
    {
        private const string DonePath = "Builds/zombie-animation-audit.done";
        private const string ArmedPath = "Builds/zombie-animation-audit.armed";
        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");
        private static bool launched;
        private const string GunPathSessionKey = "MoriGame.Audit.GunPath";

        public static void Run()
        {
            launched = false;
            Directory.CreateDirectory("Builds");
            EditorApplication.isPaused = false;
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/MoriMain.unity");

            var weapon = UnityEngine.Object.FindFirstObjectByType<WeaponController>();
            var gunModel = weapon != null && weapon.visualRoot != null
                ? weapon.visualRoot.transform.Find("GunModel")
                : null;
            var gunSource = gunModel != null
                ? PrefabUtility.GetCorrespondingObjectFromSource(gunModel.gameObject)
                : null;
            var gunPath = gunSource != null ? AssetDatabase.GetAssetPath(gunSource) : "missing";
            SessionState.SetString(GunPathSessionKey, gunPath);
            if (gunPath != GunModelSwap.PrefabPath)
            {
                Finish(false, $"scene prefab mismatch gunPath={gunPath}");
                return;
            }

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
            var host = new GameObject("ZombieAnimationPlayAudit");
            host.AddComponent<AuditRunner>();
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
            if (pool == null)
            {
                Finish(false, "missing Pool");
                yield break;
            }
            pool.ClearAll();

            var match0 = MatchBootstrap.Instance;
            if (match0 == null || match0.playSpaceAnchor == null)
            {
                Finish(false, "missing Match/playSpaceAnchor");
                yield break;
            }
            var anchorPos = match0.playSpaceAnchor.position;
            Vector3 Spot(float dx, float dz) => new Vector3(anchorPos.x + dx, anchorPos.y, anchorPos.z + dz);

            var core = UnityEngine.Object.FindFirstObjectByType<CoreDevice>();
            if (core == null)
            {
                Finish(false, "missing CoreDevice");
                yield break;
            }
            var sceneModelsOk = ValidateSceneModels(core, match0.playSpaceAnchor, out var sceneModelDiagnostics);

            var ids = new[] { "Normal", "Runner", "Spitter", "ShieldGuard", "Brute", "Overseer" };
            var expectedScales = new[] { 0.6f, 0.78f, 0.96f, 1.14f, 1.32f, 1.5f }; // 2026-11-09 实测反馈：翻倍版全员再 ×0.6
            var footMaxAbs = 0f;
            var sizesOk = true;
            var previousScale = 0f;
            var sizeDiagnostics = "";
            for (var i = 0; i < ids.Length; i++)
            {
                var model = pool.Spawn(ids[i], Spot(0f, 6f));
                if (model == null)
                {
                    Finish(false, "spawn failed " + ids[i]);
                    yield break;
                }
                var scale = model.visualRoot.localScale;
                var nav = model.GetComponent<UnityEngine.AI.NavMeshAgent>();
                sizesOk &= scale.x > previousScale &&
                            Vector3.Distance(scale, Vector3.one * expectedScales[i]) < 0.0001f &&
                            Mathf.Abs(nav.radius - 0.28f * expectedScales[i]) < 0.0001f &&
                            Mathf.Abs(nav.height - 1.7f * expectedScales[i]) < 0.0001f;
                sizeDiagnostics += $" {ids[i]}={scale.x:F2}/r{nav.radius:F3}/h{nav.height:F3}";
                previousScale = scale.x;
                footMaxAbs = Mathf.Max(footMaxAbs, Mathf.Abs(FootMinY(model)));
                pool.Return(model);
            }

            var zombie = pool.Spawn("Normal", Spot(0f, 9f));
            if (zombie == null)
            {
                Finish(false, "spawn Normal failed");
                yield break;
            }
            var startPosition = zombie.transform.position;
            var startFrame = Time.frameCount;
            var walkStart = Time.realtimeSinceStartup;
            var walkLast = walkStart;
            var walkSeconds = 0f;
            var leftFoot = zombie.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            var footStart = leftFoot != null ? leftFoot.localRotation : Quaternion.identity;
            var footMotionMax = 0f;
            var clipBound = false;
            while (Time.realtimeSinceStartup - walkStart < 2.5f)
            {
                var now = Time.realtimeSinceStartup;
                if (IsWalking(zombie.animator) && zombie.animator.GetBool(MovingId))
                {
                    walkSeconds += now - walkLast;
                }
                clipBound |= zombie.animator.GetCurrentAnimatorClipInfo(0).Length > 0;
                if (leftFoot != null)
                {
                    footMotionMax = Mathf.Max(footMotionMax,
                        Quaternion.Angle(footStart, leftFoot.localRotation));
                }
                walkLast = now;
                yield return null;
            }
            var navAgent = zombie.GetComponent<UnityEngine.AI.NavMeshAgent>();
            var walkDiagnostics = $"active={zombie.gameObject.activeInHierarchy} agentEnabled={zombie.enabled} " +
                                  $"navEnabled={navAgent.enabled} navStopped={navAgent.isStopped} onNav={navAgent.isOnNavMesh} " +
                                  $"velocity={navAgent.velocity.magnitude:F2} moved={Vector3.Distance(startPosition, zombie.transform.position):F2} " +
                                  $"frames={Time.frameCount - startFrame} timeScale={Time.timeScale:F2} paused={EditorApplication.isPaused} " +
                                  $"state={StateName(zombie.animator)} moving={zombie.animator.GetBool(MovingId)}";
            var walkSpeed = zombie.animator.GetFloat(WalkSpeedId);
            zombie.ApplyHit(1, false);
            var damageAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - damageAt < 1.3f)
            {
                yield return null;
            }
            var damageRecovered = IsWalking(zombie.animator) && zombie.animator.GetBool(MovingId);

            zombie.ApplyHit(9999, false);
            var deathAt = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - deathAt < 0.25f)
            {
                yield return null;
            }
            var deathState = zombie.animator.GetCurrentAnimatorStateInfo(0);
            var deathStateName = StateName(zombie.animator);
            var deathPlayed = deathState.IsName("Death") && deathState.normalizedTime > 0.01f;
            while (Time.realtimeSinceStartup - deathAt < 2.1f)
            {
                yield return null;
            }
            var recycled = !zombie.gameObject.activeSelf && pool.ActiveCount == 0;

            var match = MatchBootstrap.Instance;
            if (match == null)
            {
                Finish(false, "missing Match");
                yield break;
            }
            var playerHpAtStart = match.Vitals.Hp;

            // 酸液命中玩家路径回归（目标=承伤体工作台；高台制下近战够不到玩家，酸液是玩家扣血通路）
            var playerHpBeforeAcid = match.Vitals.Hp;
            var acidTarget = match.PlayerTarget;
            var acid = AcidProjectile.Spawn(
                acidTarget.position + Vector3.up * 1.1f + acidTarget.forward * 1.5f,
                acidTarget, 3, 30f, null);
            var acidStarted = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - acidStarted < 2f && acid != null && match.Vitals.Hp == playerHpBeforeAcid)
            {
                yield return null;
            }
            var acidPlayerHit = match.Vitals.Hp < playerHpBeforeAcid;
            if (acid != null)
            {
                UnityEngine.Object.Destroy(acid.gameObject);
            }
            yield return null;
            var playerHpAfterAcid = match.Vitals.Hp; // 免伤基线取酸液之后（酸液合法扣血）

            // 2026-11-06 高台制：四方向+督战者啃高台（CoreDevice）扣血；玩家在塔上不受伤
            var coreHpBefore = core.State.Hp;
            var coreAttacked = true;
            var coreContactOk = true;
            var coreDiagnostics = "";
            var coreCollider = core.GetComponent<Collider>();
            var coreIds = new[] { "Normal", "Normal", "Normal", "Normal", "Overseer" };
            var coreLabels = new[] { "-Z", "+Z", "-X", "+X", "-Z-Overseer" };
            var coreOffsets = new[]
            {
                new Vector3(0f, 0f, -3f), new Vector3(0f, 0f, 3f),
                new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f),
                new Vector3(0f, 0f, -3f)
            };
            for (var i = 0; i < coreOffsets.Length; i++)
            {
                var towerHp = core.State.Hp;
                var towerZombie = pool.Spawn(coreIds[i], Spot(coreOffsets[i].x, coreOffsets[i].z));
                var towerNav = towerZombie.GetComponent<UnityEngine.AI.NavMeshAgent>();
                var towerStarted = Time.realtimeSinceStartup;
                var nextSample = 1f;
                var towerSamples = "";
                while (Time.realtimeSinceStartup - towerStarted < 8f && core.State.Hp == towerHp)
                {
                    var elapsed = Time.realtimeSinceStartup - towerStarted;
                    if (elapsed >= nextSample)
                    {
                        towerSamples += $" t{nextSample}:p={towerZombie.transform.position} " +
                                        $"v={towerNav.velocity.magnitude:F2}/rem{towerNav.remainingDistance:F3}/" +
                                        $"stop{towerNav.isStopped}/pending{towerNav.pathPending}/dest{towerNav.destination}";
                        nextSample += 1f;
                    }
                    yield return null;
                }
                var attacked = core.State.Hp < towerHp;
                // 高台与僵尸有垂直落差——贴面距按碰撞体中心高度取点，只量水平距离；
                // 工作台障碍会挡出 carve 洞（隔障碍啃塔），接触放宽到近战圈 MeleeAttackRangeMeters
                var atTowerBand = towerZombie.transform.position;
                atTowerBand.y = coreCollider != null ? coreCollider.bounds.center.y : atTowerBand.y;
                var surfacePoint = coreCollider != null ? coreCollider.ClosestPoint(atTowerBand) : atTowerBand;
                var dx = surfacePoint.x - towerZombie.transform.position.x;
                var dz = surfacePoint.z - towerZombie.transform.position.z;
                var surfaceDistance = Mathf.Sqrt(dx * dx + dz * dz);
                var contactOk = attacked && (surfaceDistance <= towerNav.radius + 0.12f ||
                                             surfaceDistance <= ZombieAgent.MeleeAttackRangeMeters);
                coreAttacked &= attacked;
                coreContactOk &= contactOk;
                coreDiagnostics += $" {coreLabels[i]}:attacked={attacked} contactOk={contactOk} " +
                                   $"towerHp={core.State.Hp}/{towerHp} surfaceDistXZ={surfaceDistance:F3}/r{towerNav.radius:F3} " +
                                   $"pos={towerZombie.transform.position} " +
                                   $"nav={towerNav.isOnNavMesh}/{towerNav.isStopped}/{towerNav.pathStatus}";
                if (!attacked)
                {
                    coreDiagnostics += towerSamples;
                }
                pool.Return(towerZombie);
                yield return null;
            }
            var playerUntouched = match.Vitals.Hp == playerHpAfterAcid;

            // 蓝块可见性探针（2026-11-09 视角裁切排查）：临时相机绕地盘 8 个视角强制渲染，
            // 采样 MeshRenderer.isVisible——false=被剔除（bounds/视锥/遮挡），true=已画但被深度/混合吃掉
            var overlayVisibleDiagnostics = "";
            var debuggerGo = GameObject.Find("TerritoryDebugger");
            if (debuggerGo != null)
            {
                var overlayRenderer = debuggerGo.GetComponent<MeshRenderer>();
                var anchorT = match.playSpaceAnchor;
                var cellCenter = anchorT != null
                    ? new Vector3(anchorT.position.x, MoriGame.Territory.TerritoryGrid.WorldFloorY, anchorT.position.z)
                    : Vector3.zero;
                var probeGo = new GameObject("OverlayVisibilityProbe");
                var probeCamera = probeGo.AddComponent<Camera>();
                probeCamera.stereoTargetEye = StereoTargetEyeMask.None;
                probeCamera.fieldOfView = 60f;
                probeCamera.nearClipPlane = 0.05f;
                probeCamera.farClipPlane = 200f;
                // 直渲后台缓冲会与 Game 视图 uGUI Overlay 的 RenderPass 尺寸规格冲突（2026-11-09 报错，
                // Error Pause 开着时会冻死审计）——渲染到独立 RT，尺寸自持，isVisible 语义不变
                var probeRt = new RenderTexture(64, 64, 16);
                probeCamera.targetTexture = probeRt;
                foreach (var angle in new[] { 0f, 90f, 180f, 270f })
                {
                    foreach (var height in new[] { 1.2f, 3f })
                    {
                        var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                        probeGo.transform.position = cellCenter + dir * 2.5f + Vector3.up * height;
                        probeGo.transform.LookAt(cellCenter);
                        probeCamera.Render();
                        overlayVisibleDiagnostics += $" a{(int)angle}h{(int)height}:{(overlayRenderer.isVisible ? "V" : "H")}";
                    }
                }
                probeCamera.targetTexture = null;
                UnityEngine.Object.Destroy(probeRt);
                UnityEngine.Object.Destroy(probeGo);
                var meshB = debuggerGo.GetComponent<MeshFilter>().sharedMesh != null
                    ? debuggerGo.GetComponent<MeshFilter>().sharedMesh.bounds
                    : new Bounds();
                overlayVisibleDiagnostics += $" boundsC={meshB.center} boundsS={meshB.size}";
            }
            else
            {
                overlayVisibleDiagnostics = " debuggerMissing";
            }

            var feetAligned = footMaxAbs <= 0.03f;
            var walkedContinuously = walkSeconds >= 2f;

            // ---- 落地卡顿探针（2026-11-09）：程序化放塔+电网各一，量确认同步耗时与后续帧间隔 ----
            var placeProbe = "skip";
            var buildMgr = UnityEngine.Object.FindFirstObjectByType<MoriGame.Build.BuildManager>();
            if (buildMgr != null && match0 != null && match0.Grid != null)
            {
                var camGo = new GameObject("PlaceProbeCam");
                var cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.cullingMask = 0; // 必须保持 enabled 供 Camera.main 解析，零剔除不参与实际渲染
                var turret = UnityEditor.AssetDatabase.LoadAssetAtPath<MoriGame.Configs.PropConfig>(
                    "Assets/_MoriGame/Configs/Props/Prop_AutoTurret.asset");
                var fence = UnityEditor.AssetDatabase.LoadAssetAtPath<MoriGame.Configs.PropConfig>(
                    "Assets/_MoriGame/Configs/Props/Prop_ElectricFence.asset");
                if (turret != null && fence != null)
                {
                    placeProbe = "placed";
                    foreach (var (cfg, cellX, cellZ) in new[] { (turret, 24, 24), (fence, 26, 24) })
                    {
                        var tx = MoriGame.Territory.TerritoryGrid.CellCenterWorldX(cellX);
                        var tz = MoriGame.Territory.TerritoryGrid.CellCenterWorldZ(cellZ);
                        var ty = MoriGame.Territory.TerritoryGrid.WorldFloorY;
                        camGo.transform.position = new Vector3(tx, ty + 4f, tz);
                        camGo.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                        buildMgr.BeginPlacement(cfg);
                        yield return null;
                        yield return null; // 让 UpdatePlacement 产出合法预览
                        var confirmMi = typeof(MoriGame.Build.BuildManager).GetMethod("ConfirmPlacement",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        confirmMi.Invoke(buildMgr, null);
                        sw.Stop();
                        var worst = 0f;
                        var sum = 0f;
                        const int frames = 20;
                        var last = Time.realtimeSinceStartup;
                        for (var f = 0; f < frames; f++)
                        {
                            yield return null;
                            var now = Time.realtimeSinceStartup;
                            var d = (now - last) * 1000f;
                            if (d > worst) worst = d;
                            sum += d;
                            last = now;
                        }
                        placeProbe += $" {cfg.propId}:sync={sw.Elapsed.TotalMilliseconds:0.0}ms " +
                                      $"worstFrame={worst:0.0}ms avgFrame={sum / frames:0.0}ms";
                    }
                }
                UnityEngine.Object.Destroy(camGo);
            }

            var ok = sceneModelsOk && sizesOk && feetAligned && walkedContinuously && clipBound && footMotionMax > 1f &&
                     damageRecovered && deathPlayed && recycled && acidPlayerHit &&
                     coreAttacked && coreContactOk && playerUntouched;
            Finish(ok, $"sceneModelsOk={sceneModelsOk} {sceneModelDiagnostics} " +
                       $"{placeProbe} " +
                       $"sizesOk={sizesOk}{sizeDiagnostics} footMaxAbs={footMaxAbs:F4} " +
                       $"walkSeconds={walkSeconds:F2} walkSpeed={walkSpeed:F2} " +
                       $"clipBound={clipBound} footMotionMax={footMotionMax:F2} " +
                       $"damageRecovered={damageRecovered} deathPlayed={deathPlayed} deathState={deathStateName} " +
                       $"recycled={recycled} acidPlayerHit={acidPlayerHit} " +
                       $"coreAttacked={coreAttacked} coreContactOk={coreContactOk} " +
                       $"playerUntouched={playerUntouched} hp={match.Vitals.Hp}/{playerHpAtStart} " +
                       $"towerHp={core.State.Hp}/{coreHpBefore} {walkDiagnostics}{coreDiagnostics}" +
                       $"{overlayVisibleDiagnostics}");
        }

        private static bool ValidateSceneModels(CoreDevice core, Transform anchor, out string diagnostics)
        {
            // 2026-11-06 高台制：CoreDevice=玩家高台（0.5×0.5×塔高灰盒 TowerVisual），底座落地、玩家站塔顶；
            // 旧大模型 Visual 禁用留档
            var towerVisual = core.transform.Find("TowerVisual");
            var oldVisual = core.transform.Find("Visual");
            var coreBounds = ModelBounds(towerVisual);
            var coreCollider = core.GetComponent<BoxCollider>();
            var coreObstacle = core.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            var oldVisualOff = oldVisual == null || !oldVisual.gameObject.activeSelf;
            var towerHeight = towerVisual != null ? towerVisual.lossyScale.y : 0f;
            var towerGroundY = RealSpaceMigration.GroundProbe(
                anchor.position + new Vector3(1.2f, 5f, 0f));
            var coreTransformOk = anchor != null && core.transform.parent == anchor &&
                                  Vector3.Distance(core.transform.localPosition,
                                      new Vector3(0f, towerGroundY - anchor.position.y, 0f)) < 0.0001f &&
                                  Quaternion.Angle(core.transform.localRotation, Quaternion.identity) < 0.01f;
            var coreSizeOk = coreBounds.HasValue && towerVisual != null &&
                             Vector3.Distance(coreBounds.Value.size, towerVisual.lossyScale) < 0.02f;
            var relCenter = coreBounds.HasValue ? coreBounds.Value.center - anchor.position : Vector3.zero;
            var coreFootprintOk = coreBounds.HasValue &&
                                  Mathf.Abs(relCenter.x) < 0.02f && Mathf.Abs(relCenter.z) < 0.02f;
            var coreGrounded = coreBounds.HasValue &&
                               Mathf.Abs(coreBounds.Value.min.y - towerGroundY) < 0.03f;
            var coreColliderMatchesBounds = coreBounds.HasValue && coreCollider != null && towerVisual != null &&
                                            Vector3.Distance(coreCollider.center, new Vector3(0f, towerHeight * 0.5f, 0f)) < 0.01f &&
                                            Vector3.Distance(coreCollider.size, towerVisual.localScale) < 0.01f;
            var coreOk = coreTransformOk && coreSizeOk && coreFootprintOk && coreGrounded && oldVisualOff &&
                         coreColliderMatchesBounds &&
                         coreObstacle != null && coreObstacle.carving &&
                         Vector3.Distance(coreObstacle.center, coreCollider.center) < 0.0001f &&
                         Vector3.Distance(coreObstacle.size, coreCollider.size) < 0.0001f;

            var weapon = UnityEngine.Object.FindFirstObjectByType<WeaponController>();
            var gunVisual = weapon != null && weapon.visualRoot != null
                ? weapon.visualRoot.transform
                : null;
            var gunModel = gunVisual != null ? gunVisual.Find("GunModel") : null;
            var gunPath = SessionState.GetString(GunPathSessionKey, "missing");
            var gunBounds = ModelBounds(gunModel);
            var gunMaterialsOk = gunModel != null;
            if (gunModel != null)
            {
                foreach (var renderer in gunModel.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        gunMaterialsOk &= material != null;
                    }
                }
            }
            var gunOk = gunVisual != null && gunModel != null &&
                        gunPath == GunModelSwap.PrefabPath &&
                        gunBounds.HasValue && gunMaterialsOk &&
                        gunModel.GetComponentsInChildren<MeshRenderer>(true).Length > 0;

            var workbench = GameObject.Find("Workbench");
            var workbenchCollider = workbench != null ? workbench.GetComponent<BoxCollider>() : null;
            var workbenchObstacle = workbench != null
                ? workbench.GetComponent<UnityEngine.AI.NavMeshObstacle>()
                : null;
            // 悬浮版无实体台面：旧工作台大盒/导航障碍必须移除，承伤代理迁到 XR 根的小胶囊
            var workbenchCollisionOk = workbench != null && anchor != null && workbench.transform.parent == anchor &&
                                       workbenchCollider == null && workbenchObstacle == null;
            var match = MatchBootstrap.Instance;
            var playerHitProxy = match != null && match.playerRig != null
                ? match.playerRig.GetComponent<CapsuleCollider>()
                : null;
            var playerProxyOk = playerHitProxy != null && playerHitProxy.isTrigger &&
                                Mathf.Abs(playerHitProxy.radius - 0.25f) < 0.0001f &&
                                Mathf.Abs(playerHitProxy.height - 1.8f) < 0.0001f &&
                                Vector3.Distance(playerHitProxy.center, new Vector3(0f, 0.9f, 0f)) < 0.0001f;
            var playerTargetOk = match != null && match.playerRig != null && anchor != null && playerProxyOk &&
                                 match.playerAttackTarget == match.playerRig &&
                                 match.PlayerTarget == match.playerRig &&
                                 match.playerRig.parent == anchor &&
                                 // RealSpace Y=塔顶（玩家脚位），XR 局部归零
                                 Vector3.Distance(match.playerRig.localPosition, Vector3.zero) < 0.0001f &&
                                 Quaternion.Angle(match.playerRig.localRotation, Quaternion.identity) < 0.01f &&
                                 anchor != null && coreBounds.HasValue &&
                                 Mathf.Abs(anchor.position.y - coreBounds.Value.max.y) < 0.02f;
            var navMeshRadius = UnityEngine.AI.NavMesh.GetSettingsByID(0).agentRadius;
            var navMeshRadiusOk = Mathf.Abs(navMeshRadius - 0.14f) < 0.0001f;
            // 初始地盘 8×8（16㎡）以锚点格为中心，高塔 2×2 居中对称（2026-11-09 二次校准）
            var territoryOk = navMeshRadiusOk && match != null && match.Grid != null &&
                              match.Grid.GetOwnedCellCount(MoriGame.Territory.CellOwner.P1) == 64 && // 8×8 初始地盘（2026-11-09 二次校准）
                              Mathf.Abs(match.Grid.GetAreaSqm(MoriGame.Territory.CellOwner.P1) - 16f) < 0.0001f &&
                              match.Grid.GetOwner(new MoriGame.Territory.CellCoord(24, 24)) == MoriGame.Territory.CellOwner.P1 &&
                              match.Grid.GetOwner(new MoriGame.Territory.CellCoord(31, 31)) == MoriGame.Territory.CellOwner.P1;
            // 地图导航（2026-11-06 高台制：僵尸走真实地面）——刷怪环采样必须落地面高度
            var terrainSampleOk = false;
            if (anchor != null)
            {
                var terrainProbe = anchor.position + new Vector3(12.5f, 0.5f, 0f);
                terrainSampleOk = UnityEngine.AI.NavMesh.SamplePosition(terrainProbe, out var terrainHit, 2f, UnityEngine.AI.NavMesh.AllAreas) &&
                                  Mathf.Abs(terrainHit.position.y - towerGroundY) < 0.5f;
            }
            // 地盘棋盘包围盒必须落在锚点（2026-11-09 视锥剔除整板消失实测回归锁）
            var board = GameObject.Find("TerritoryDebugger");
            var boardMesh = board != null ? board.GetComponent<MeshFilter>().sharedMesh : null;
            var boardWorldCenter = board != null && boardMesh != null
                ? board.transform.TransformPoint(boardMesh.bounds.center)
                : Vector3.zero;
            var boardOk = board != null && anchor != null && boardMesh != null &&
                          Vector3.Distance(boardWorldCenter,
                              new Vector3(anchor.position.x, anchor.position.y + 0.05f, anchor.position.z)) < 1f;
            var arcCount = 0;
            var arcRadiusOk = workbench != null;
            if (workbench != null)
            {
                foreach (Transform child in workbench.transform)
                {
                    if (!child.name.StartsWith("ArcTop_"))
                    {
                        continue;
                    }
                    arcCount++;
                    arcRadiusOk &= Mathf.Abs(new Vector2(child.localPosition.x, child.localPosition.z).magnitude - 0.65f) < 0.0001f;
                }
            }
            var slot = workbench != null ? workbench.transform.Find("PistolSlotAnchor") : null;
            var turret = workbench != null ? workbench.transform.Find("ShopStands/Stand_AutoTurret") : null;
            var fence = workbench != null ? workbench.transform.Find("ShopStands/Stand_ElectricFence") : null;
            var supply = workbench != null ? workbench.transform.Find("SupplyCrate") : null;
            var slotExpected = Vector3.one * (0.65f * 0.70710678f);
            slotExpected.y = 0.87f;
            var layoutOk = workbench != null && arcCount == 9 && arcRadiusOk &&
                           slot != null && Vector3.Distance(slot.localPosition, slotExpected) < 0.0001f &&
                           turret != null && Vector3.Distance(turret.localPosition, new Vector3(-0.51f, 0.73f, 0.40f)) < 0.0001f &&
                           fence != null && Vector3.Distance(fence.localPosition, new Vector3(-0.205f, 0.73f, 0.615f)) < 0.0001f &&
                           supply != null && Vector3.Distance(supply.localPosition, new Vector3(0.03f, 0.78f, 0.62f)) < 0.0001f;
            var supplyCrate = supply != null ? supply.GetComponent<Economy.SupplyCrate>() : null;
            var gazeCollider = supplyCrate != null ? supplyCrate.gazeCollider : null;
            var supplyGazeOk = false;
            if (gazeCollider != null && match != null && match.playerRig != null)
            {
                var eye = match.playerRig.position + Vector3.up * 1.6f;
                var gazeRay = new Ray(eye, (gazeCollider.bounds.center - eye).normalized);
                supplyGazeOk = Physics.Raycast(gazeRay, out var gazeHit, supplyCrate.gazeRangeMeters) &&
                               gazeHit.collider == gazeCollider;
            }

            diagnostics = $"coreOk={coreOk} coreTransformOk={coreTransformOk} coreSizeOk={coreSizeOk} " +
                          $"coreFootprintOk={coreFootprintOk} coreGrounded={coreGrounded} oldVisualOff={oldVisualOff} " +
                          $"coreColliderMatchesBounds={coreColliderMatchesBounds} " +
                          $"corePos={core.transform.position:F3} coreLocalPos={core.transform.localPosition:F3} " +
                          $"coreSize={(coreBounds.HasValue ? coreBounds.Value.size.ToString("F3") : "none")} " +
                          $"coreCenter={(coreBounds.HasValue ? coreBounds.Value.center.ToString("F3") : "none")} " +
                          $"coreMinY={(coreBounds.HasValue ? coreBounds.Value.min.y.ToString("F4") : "none")} " +
                          $"gunOk={gunOk} gunPath={gunPath} gunMaterialsOk={gunMaterialsOk} " +
                          $"gunSize={(gunBounds.HasValue ? gunBounds.Value.size.ToString("F3") : "none")} " +
                          $"layoutOk={layoutOk} arcCount={arcCount} arcRadiusOk={arcRadiusOk} " +
                          $"workbenchCollisionOk={workbenchCollisionOk} playerTargetOk={playerTargetOk} " +
                          $"territoryOk={territoryOk} navMeshRadius={navMeshRadius:F3} " +
                          $"terrainSampleOk={terrainSampleOk} boardOk={boardOk} supplyGazeOk={supplyGazeOk}";
            return coreOk && gunOk && layoutOk && workbenchCollisionOk && playerTargetOk && territoryOk &&
                   supplyGazeOk && terrainSampleOk && boardOk;
        }

        private static Bounds? ModelBounds(Transform root)
        {
            if (root == null)
            {
                return null;
            }
            var initialized = false;
            var bounds = new Bounds();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                {
                    continue;
                }
                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return initialized ? bounds : (Bounds?)null;
        }

        private static bool IsWalking(Animator animator)
        {
            return animator.GetCurrentAnimatorStateInfo(0).IsName("WalkA") ||
                   animator.GetCurrentAnimatorStateInfo(0).IsName("WalkB") ||
                   animator.GetNextAnimatorStateInfo(0).IsName("WalkA") ||
                   animator.GetNextAnimatorStateInfo(0).IsName("WalkB");
        }

        private static string StateName(Animator animator)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);
            foreach (var name in new[] { "Idle", "WalkA", "WalkB", "AttackA", "AttackB", "Damage", "Death" })
            {
                if (state.IsName(name))
                {
                    return name;
                }
            }
            return "Other";
        }

        private static float FootMinY(ZombieAgent zombie)
        {
            var minY = float.MaxValue;
            var renderers = zombie.visualRoot.GetComponentsInChildren<Renderer>(false);
            foreach (var renderer in renderers)
            {
                if (renderer.enabled)
                {
                    minY = Mathf.Min(minY, renderer.bounds.min.y);
                }
            }
            return minY - zombie.transform.position.y;
        }

        private static void Finish(bool ok, string details)
        {
            launched = false;
            Directory.CreateDirectory("Builds");
            if (File.Exists(ArmedPath))
            {
                File.Delete(ArmedPath);
            }
            File.WriteAllText(DonePath, "EXITCODE=" + (ok ? "0" : "1") + " " + details + " " +
                                      DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Debug.Log("[MoriGame] 僵尸 Play 审计：" + (ok ? "通过 " : "失败 ") + details);
            EditorApplication.isPlaying = false;
        }
    }
}
