using MoriGame.Audio;
using MoriGame.Build;
using MoriGame.Configs;
using MoriGame.Economy;
using MoriGame.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace MoriGame.Zombies
{
    public class ZombieWeakpoint : MonoBehaviour
    {
    }

    public class ZombieLeg : MonoBehaviour
    {
    }

    /// <summary>NavMesh 移动、类型目标选择、承伤与 Animator 联动。</summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class ZombieAgent : MonoBehaviour
    {
        public const float MeleeAttackIntervalSeconds = 1f;
        public const float MeleeAttackRangeMeters = 1.15f;
        public const float TerritorySlowFactor = 0.85f;
        private const float RetargetIntervalSeconds = 0.5f;
        private const float PathBuildingDistanceMeters = 2f;
        private const float DeathAnimationSeconds = 1.9f;
        private const float WalkReferenceSpeedMps = 0.8f;

        private static readonly int MovingId = Animator.StringToHash("Moving");
        private static readonly int WalkBId = Animator.StringToHash("WalkB");
        private static readonly int WalkSpeedId = Animator.StringToHash("WalkSpeed");
        private static readonly int AttackId = Animator.StringToHash("Attack");
        private static readonly int AttackBId = Animator.StringToHash("AttackB");
        private static readonly int DamageId = Animator.StringToHash("Damage");
        private static readonly int DeathStateId = Animator.StringToHash("Death");

        public ZombieConfig config;
        public Animator animator;
        public Transform visualRoot;
        public ZombieVitals Vitals { get; private set; }
        public bool IsAlive => Vitals != null && Vitals.IsAlive;
        public bool IsStaggered => stagger != null && stagger.Remaining > 0f;

        private NavMeshAgent agent;
        private Transform target;
        private float retargetRemaining;
        private float attackCooldown;
        private float baseSpeedMps = 1f;
        private float deathRemaining;
        private ZombiePool pool;
        private HitStaggerState stagger;
        private Collider[] hitColliders;
        private bool lastHitWeak; // 致命一击是否弱点击杀（死亡音分档用；编辑器编译也需可见）

#if DEVELOPMENT_BUILD
        private static bool diagnosticClaimed;
        private bool diagnosticEnabled;
        private float diagnosticElapsed;
        private float diagnosticNext;
#endif

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            agent.updateRotation = false;
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        public void Initialize(ZombieConfig zombieConfig, ZombiePool ownerPool)
        {
            config = zombieConfig;
            pool = ownerPool;
            if (Vitals != null)
            {
                Vitals.Died -= HandleDeath;
            }
            Vitals = new ZombieVitals(
                zombieConfig.maxHealth, zombieConfig.weakpointMultiplier,
                zombieConfig.killGold, zombieConfig.combatScore, zombieConfig.frontShieldHealth);
            Vitals.Died += HandleDeath;
            healthBar?.ResetDisplay();
            hitFeedbackHeadLocalY = null;
            stagger = zombieConfig.zombieId == "Runner"
                ? new HitStaggerState(zombieConfig.hitsToStagger, zombieConfig.staggerSeconds,
                    zombieConfig.staggerChainResetSeconds)
                : null;
            baseSpeedMps = zombieConfig.moveSpeedMps;
            agent.enabled = true;
            agent.speed = baseSpeedMps;
            agent.Warp(transform.position);
            target = null;
            retargetRemaining = 0f;
            attackCooldown = 0f;
            deathRemaining = 0f;
            hitColliders = GetComponentsInChildren<Collider>(true);
            foreach (var hitCollider in hitColliders)
            {
                hitCollider.enabled = true;
            }
            var toCenter = -transform.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(toCenter.normalized);
            }
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.Rebind();
                animator.SetBool(WalkBId, Random.value < 0.5f);
                animator.SetBool(MovingId, false);
                animator.SetFloat(WalkSpeedId, baseSpeedMps / WalkReferenceSpeedMps);
                animator.ResetTrigger(AttackId);
                animator.ResetTrigger(DamageId);
            }
#if DEVELOPMENT_BUILD
            if (!diagnosticClaimed)
            {
                diagnosticClaimed = true;
                diagnosticEnabled = true;
                diagnosticElapsed = 0f;
                diagnosticNext = 0f;
            }
#endif
        }

        private float empUntil; // 瘫痪 EMP 眩晕截止（2026-11-11 新手翻身机制）

        /// <summary>练习靶模式（M4-T0 教学关）：不移动不索敌不攻击，可被射击/击杀（毕业前靶场）</summary>
        public bool dummyMode;

        private void Update()
        {
            if (Vitals == null)
            {
                return;
            }
#if DEVELOPMENT_BUILD
            TickDiagnostics();
#endif
            if (!IsAlive)
            {
                deathRemaining -= Time.deltaTime;
                if (deathRemaining <= 0f)
                {
                    pool?.Return(this);
                }
                return;
            }
            if (dummyMode || Tutorial.TutorialSession.Active) // 教学靶/教学会话硬闸：站桩受击，永不索敌/移动/攻击
            {
                StopMoving();
                UpdateAnimation();
                return;
            }
            if (Time.time < empUntil) // EMP 眩晕：停走停攻（惩罚事件变博弈喘息窗口）
            {
                StopMoving();
                UpdateAnimation();
                return;
            }

            if (acidLaunchAt > 0f) // 起手延迟到点发射（目标失效则取消）
            {
                if (Time.time >= acidLaunchAt)
                {
                    acidLaunchAt = -1f;
                    if (acidTarget != null && acidTarget.gameObject.activeInHierarchy)
                    {
                        LaunchAcid(acidTarget);
                    }
                    acidTarget = null;
                }
            }

            attackCooldown -= Time.deltaTime;
            stagger?.Tick(Time.deltaTime);
            if (IsStaggered)
            {
                StopMoving();
                UpdateAnimation();
                return;
            }

            retargetRemaining -= Time.deltaTime;
            if (retargetRemaining <= 0f)
            {
                retargetRemaining = RetargetIntervalSeconds;
                target = SelectTarget();
            }
            if (target == null)
            {
                StopMoving();
                UpdateAnimation();
                return;
            }

            RefreshSpeed();
            var targetPoint = TargetPoint(target);
            var toTarget = targetPoint - transform.position;
            toTarget.y = 0f;
            var ranged = config.acidRangeMeters > 0f && config.acidDamage > 0;
            var attackRange = ranged ? config.acidRangeMeters : MeleeRange(target);
            // 隔障碍近战（2026-11-09 实测）：目的地落在导航 carve 洞里（塔+工作台障碍）时 agent 停在洞边，
            // 表面距永不满足 melee 半径——导航到底且静止、XZ 表面距仍在近战圈内即开啃
            var atRestAtPathEnd = !ranged && !agent.pathPending &&
                                  agent.remainingDistance <= 0.08f &&
                                  agent.velocity.magnitude < 0.1f;
            var inMelee = toTarget.magnitude <= attackRange ||
                          (atRestAtPathEnd && toTarget.magnitude <= MeleeAttackRangeMeters);
            if (inMelee)
            {
                StopMoving();
                Face(targetPoint);
                if (attackCooldown <= 0f)
                {
                    attackCooldown = ranged
                        ? Mathf.Max(0.05f, config.acidCooldownSeconds)
                        : MeleeAttackIntervalSeconds;
                    Attack(target, ranged);
                }
            }
            else
            {
                agent.isStopped = false;
                agent.destination = targetPoint;
            }
            UpdateAnimation();
        }

        private void RefreshSpeed()
        {
            // 电网 30% 减速仅督战者（isFinalBoss）——蛮兽虽 isBossType 但吃普通 60%（2026-11-10 用户定版）
            var fieldFactor = ElectricFence.SpeedFactorAt(
                transform.position, config != null && config.zombieId == "Overseer");
            if (fieldFactor < 1f)
            {
                agent.speed = baseSpeedMps * fieldFactor;
                return;
            }
            var match = MatchBootstrap.Instance;
            var grid = match != null ? match.Grid : null;
            agent.speed = grid != null && grid.IsInPlayerTerritory(transform.position.x, transform.position.z)
                ? baseSpeedMps * TerritorySlowFactor
                : baseSpeedMps;
        }

        private Transform SelectTarget()
        {
            var match = MatchBootstrap.Instance;
            if (match == null)
            {
                return null;
            }
            if (config.zombieId == "Brute" || config.zombieId == "Overseer")
            {
                var building = NearestBuildingToAgent();
                if (building != null)
                {
                    return building.transform;
                }
            }

            // 2026-11-06 高台制：玩家在高台上输出，僵尸主攻高台（CoreDevice，血量=旧中控）；
            // 路径建筑优先由 BuildingNearPath 接管，塔缺失才退回玩家点；
            // 2026-11-11 性能：NavMesh.CalculatePath 错峰（静态预算每帧 2 次，超预算当帧退回塔/玩家直取）
            if (match.coreDevice != null)
            {
                var towerTarget = match.coreDevice.transform;
                var pathBuilding = TryConsumeRepathBudget() ? BuildingNearPath(towerTarget) : null;
                return pathBuilding != null ? pathBuilding.transform : towerTarget;
            }
            if (!PlayerAvailable(match))
            {
                return null;
            }
            var defaultTarget = match.PlayerTarget;
            var pathBuilding2 = TryConsumeRepathBudget() ? BuildingNearPath(defaultTarget) : null;
            return pathBuilding2 != null ? pathBuilding2.transform : defaultTarget;
        }

        private static bool PlayerAvailable(MatchBootstrap match)
        {
            return match.playerRig != null && match.Vitals != null &&
                   match.Vitals.State != MoriGame.Player.VitalsState.Downed;
        }

        private PropBehaviour NearestBuildingToAgent()
        {
            PropBehaviour best = null;
            var bestSqr = float.MaxValue;
            var placed = PropBehaviour.Placed;
            for (var i = 0; i < placed.Count; i++)
            {
                var prop = placed[i];
                if (prop == null || prop.IsDestroyed)
                {
                    continue;
                }
                var sqr = (prop.ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = prop;
                }
            }
            return best;
        }

        private static int repathBudgetFrame = -1;
        private static int repathBudgetUsed;

        /// <summary>全局重寻路预算：每帧最多 2 次 CalculatePath（30 僵尸同帧重选目标不再同帧算 30 条路径）</summary>
        private static bool TryConsumeRepathBudget()
        {
            if (repathBudgetFrame != Time.frameCount)
            {
                repathBudgetFrame = Time.frameCount;
                repathBudgetUsed = 0;
            }
            if (repathBudgetUsed >= 2)
            {
                return false;
            }
            repathBudgetUsed++;
            return true;
        }

        private PropBehaviour BuildingNearPath(Transform defaultTarget)
        {
            if (PropBehaviour.Placed.Count == 0)
            {
                return null;
            }
            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(transform.position, TargetPoint(defaultTarget), NavMesh.AllAreas, path) ||
                path.corners.Length == 0)
            {
                return null;
            }
            PropBehaviour best = null;
            var bestSqr = float.MaxValue;
            var placed = PropBehaviour.Placed;
            for (var i = 0; i < placed.Count; i++)
            {
                var prop = placed[i];
                if (prop == null || prop.IsDestroyed)
                {
                    continue;
                }
                var point = prop.ClosestPoint(transform.position);
                if (DistanceToPath(point, path.corners) > PathBuildingDistanceMeters)
                {
                    continue;
                }
                var sqr = (point - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = prop;
                }
            }
            return best;
        }

        public static float DistanceToPath(Vector3 point, Vector3[] corners)
        {
            if (corners == null || corners.Length == 0)
            {
                return float.MaxValue;
            }
            point.y = 0f;
            var best = float.MaxValue;
            for (var i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                a.y = 0f;
                if (i == corners.Length - 1)
                {
                    best = Mathf.Min(best, Vector3.Distance(point, a));
                    break;
                }
                var b = corners[i + 1];
                b.y = 0f;
                var ab = b - a;
                var t = ab.sqrMagnitude > 0.0001f
                    ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude)
                    : 0f;
                best = Mathf.Min(best, Vector3.Distance(point, a + ab * t));
            }
            return best;
        }

        private float MeleeRange(Transform targetTransform)
        {
            // 碰撞体目标从表面起算，只需容纳自身半径；不能再叠加旧中心攻击圈。
            return targetTransform.GetComponent<Collider>() != null
                ? agent.radius + 0.05f
                : MeleeAttackRangeMeters;
        }

        private Vector3 TargetPoint(Transform targetTransform)
        {
            var prop = targetTransform.GetComponent<PropBehaviour>();
            if (prop != null)
            {
                return prop.ClosestPoint(transform.position);
            }
            // 中控扩为 2×2 米后按表面距离判定，避免大体型僵尸被中心攻击圈挡在外面。
            var targetCollider = targetTransform.GetComponent<Collider>();
            return targetCollider != null ? targetCollider.ClosestPoint(transform.position) : targetTransform.position;
        }

        private float acidLaunchAt = -1f; // 酸液起手发射时刻（2026-11-11：先播 Attack 动画再发射）
        private Transform acidTarget;

        private void LaunchAcid(Transform targetTransform)
        {
            var mouth = transform.position + Vector3.up * 1.15f;
            SfxBus.World(SfxBus.Lib != null ? SfxBus.Lib.acidSpit : null, mouth);
            AcidProjectile.Spawn(mouth,
                targetTransform, config.acidDamage, config.acidProjectileSpeedMps,
                pool != null ? pool.acidMaterial : null);
        }

        private void Attack(Transform targetTransform, bool ranged)
        {
            if (animator != null)
            {
                animator.SetBool(AttackBId, Random.value < 0.5f);
                animator.SetTrigger(AttackId);
            }
            if (ranged)
            {
                var windup = config != null ? Mathf.Max(0f, config.acidWindupSeconds) : 0f;
                if (windup <= 0f)
                {
                    LaunchAcid(targetTransform);
                }
                else
                {
                    acidTarget = targetTransform;
                    acidLaunchAt = Time.time + windup; // 起手延迟：动画先出动作
                }
                return;
            }
            var match = MatchBootstrap.Instance;
            if (match == null)
            {
                return;
            }
            SfxBus.World(SfxBus.Lib != null ? SfxBus.Lib.zombieMeleeAttack : null,
                transform.position, true); // 近战攻击人声，pitch 随机防重复
            var prop = targetTransform.GetComponent<PropBehaviour>();
            if (prop != null)
            {
                prop.TakeDamage(config.meleeDamage); // 2026-11-12 用户定版：电网全类型可攻击，破坏权过滤取消
            }
            else if (match.IsPlayerTarget(targetTransform))
            {
                match.Vitals.TakeDamage(config.meleeDamage);
            }
            else if (match.coreDevice != null && targetTransform == match.coreDevice.transform)
            {
                match.coreDevice.TakeDamage(config.meleeDamage);
#if DEVELOPMENT_BUILD
                Debug.Log($"[MoriGame][TowerAttack] id={config.zombieId} pos={transform.position} " +
                          $"hp={match.coreDevice.State.Hp}/{match.coreDevice.State.MaxHp} " +
                          $"state={match.coreDevice.State.State}");
#endif
            }
        }

        public bool ApplyHit(int damage, bool hitWeakpoint)
        {
            return ApplyHit(damage, hitWeakpoint, transform.position + transform.forward);
        }

        /// <summary>瘫痪 EMP 反冲（2026-11-11 新手翻身机制）：击退 meters + 眩晕 seconds（位置直推，导航自动重规划）</summary>
        public void ApplyEmpStun(float meters, float seconds, Vector3 empOrigin)
        {
            var away = transform.position - empOrigin;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
            {
                transform.position += away.normalized * meters;
            }
            empUntil = Time.time + seconds;
            StopMoving();
        }

        public bool ApplyHit(int damage, bool hitWeakpoint, Vector3 sourcePosition,
            bool bypassShield = false)        {
            if (!IsAlive)
            {
                return true;
            }
            var toSource = sourcePosition - transform.position;
            toSource.y = 0f;
            var frontal = toSource.sqrMagnitude > 0.0001f &&
                          Vector3.Dot(transform.forward, toSource.normalized) >= 0.5f;
            lastHitWeak = hitWeakpoint; // 死亡音区分弱点击杀（2026-11-10 音效系统）
            if (hitWeakpoint)
            {
                Vfx.VfxBus.Play(l => l.bloodWeak, transform.position + Vector3.up * 0.8f);
            }
            else
            {
                Vfx.VfxBus.Play(l => l.bloodHit, transform.position + Vector3.up * 0.8f); // 溅血（上限 10 丢弃最旧）
            }
            var applied = Vitals.TakeDamage(damage, hitWeakpoint, frontal, bypassShield);
            ShowHitFeedback(applied, hitWeakpoint, sourcePosition); // 头顶血条 + 伤害数字（2026-11-12 WW UI）
            if (IsAlive)
            {
                if (animator != null)
                {
                    animator.SetTrigger(DamageId);
                }
                if (stagger != null && stagger.RegisterHit())
                {
                    StopMoving();
                }
            }
            return !IsAlive;
        }

        // ---------- WW UI 命中反馈（2026-11-12 用户定版）：头顶血条 + 伤害数字 ----------

        private ZombieHealthBar healthBar;
        public TMP_FontAsset nameLabelFont; // 池生成时注入（血条上方类型名，中文字体）
        private float? hitFeedbackHeadLocalY;

        private void ShowHitFeedback(int appliedDamage, bool hitWeakpoint, Vector3 sourcePosition)
        {
            var headLocalY = HitFeedbackHeadLocalY();
            if (appliedDamage > 0)
            {
                var toSource = sourcePosition - transform.position;
                toSource.y = 0f;
                var dir = toSource.sqrMagnitude > 0.0001f ? toSource.normalized : -transform.forward;
                DamageNumbers.Spawn(
                    transform.position + dir * 0.18f + Vector3.up * (headLocalY - 0.3f),
                    appliedDamage, hitWeakpoint);
            }
            if (healthBar == null)
            {
                healthBar = ZombieHealthBar.Create(transform, HealthBarWidth(), headLocalY + 0.16f,
                    nameLabelFont, config != null ? config.displayName : null);
            }
            healthBar.transform.localPosition = new Vector3(0f, headLocalY + 0.16f, 0f);
            healthBar.OnHit(Vitals.HealthBarFraction);
        }

        /// <summary>血条宽（世界米）：胶囊尺寸由体型表派生（高=1.7×scale）——反推缩放，条宽随体型成比例
        /// （用户 2026-11-12：写死 0.42 在小体型僵尸上宽近半米，比例失真）</summary>
        private float HealthBarWidth()
        {
            var col = GetComponent<CapsuleCollider>();
            var scale = col != null ? col.height / 1.7f : 1f;
            return 0.42f * Mathf.Clamp(scale, 0.6f, 1.6f);
        }

        /// <summary>头顶高度取碰撞体与当前模型渲染顶部的较高者，避免不同模型遮住血条。</summary>
        private float HitFeedbackHeadLocalY()
        {
            if (hitFeedbackHeadLocalY.HasValue)
            {
                return hitFeedbackHeadLocalY.Value;
            }
            float? topWorld = null;
            var col = GetComponent<CapsuleCollider>();
            if (col != null)
            {
                topWorld = col.bounds.max.y;
            }
            if (visualRoot != null)
            {
                var renderers = visualRoot.GetComponentsInChildren<Renderer>(false);
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i].enabled && (!topWorld.HasValue || renderers[i].bounds.max.y > topWorld.Value))
                    {
                        topWorld = renderers[i].bounds.max.y;
                    }
                }
            }
            hitFeedbackHeadLocalY = topWorld.HasValue
                ? (topWorld.Value - transform.position.y) / Mathf.Max(0.01f, transform.lossyScale.y) + 0.22f
                : 2.1f;
            return hitFeedbackHeadLocalY.Value;
        }

        private void UpdateAnimation()
        {
            if (animator == null)
            {
                return;
            }
            var speed = agent.enabled ? agent.velocity.magnitude : 0f;
            animator.SetBool(MovingId, agent.enabled && !agent.isStopped && speed > 0.1f);
            animator.SetFloat(WalkSpeedId, Mathf.Max(0.05f, speed / WalkReferenceSpeedMps));
            if (!agent.isStopped && agent.velocity.sqrMagnitude > 0.0025f)
            {
                var direction = agent.velocity;
                direction.y = 0f;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(direction.normalized),
                    agent.angularSpeed * Mathf.Deg2Rad * Time.deltaTime);
            }
        }

        private void StopMoving()
        {
            if (agent.enabled)
            {
                agent.isStopped = true;
            }
            if (animator != null)
            {
                animator.SetBool(MovingId, false);
            }
        }

        private void Face(Vector3 point)
        {
            var direction = point - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(direction.normalized), 10f * Time.deltaTime);
            }
        }

#if DEVELOPMENT_BUILD
        private void TickDiagnostics()
        {
            if (!diagnosticEnabled)
            {
                return;
            }
            diagnosticElapsed += Time.deltaTime;
            if (diagnosticElapsed < diagnosticNext)
            {
                return;
            }
            diagnosticNext += 1f;
            if (diagnosticElapsed > 12f)
            {
                diagnosticEnabled = false;
                return;
            }
            var state = animator != null ? animator.GetCurrentAnimatorStateInfo(0) : default;
            var clips = animator != null ? animator.GetCurrentAnimatorClipInfo(0) : null;
            var clip = clips != null && clips.Length > 0 && clips[0].clip != null
                ? clips[0].clip.name
                : "none";
            var foot = animator != null && animator.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.LeftFoot)
                : null;
            Debug.Log($"[MoriGame][ZAnim] t={diagnosticElapsed:F1} id={config.zombieId} " +
                      $"pos={transform.position} vel={(agent.enabled ? agent.velocity.magnitude : 0f):F2} " +
                      $"nav={agent.enabled}/{agent.isOnNavMesh}/{agent.isStopped}/{agent.pathStatus} " +
                      $"anim={(animator != null && animator.isActiveAndEnabled)} " +
                      $"avatar={(animator != null && animator.avatar != null && animator.avatar.isValid)}/" +
                      $"{(animator != null && animator.isHuman)} " +
                      $"controller={(animator != null && animator.runtimeAnimatorController != null)} " +
                      $"state={state.shortNameHash} time={state.normalizedTime:F3} clip={clip} " +
                      $"moving={(animator != null && animator.GetBool(MovingId))} " +
                      $"walkSpeed={(animator != null ? animator.GetFloat(WalkSpeedId) : 0f):F2} " +
                      $"foot={(foot != null ? foot.localRotation.ToString("F3") : "none")}");
        }
#endif

        private void HandleDeath()
        {
            acidLaunchAt = -1f; // 死亡取消未发射的酸液起手
            var match = MatchBootstrap.Instance;
            if (match != null)
            {
                match.Wallet.Add(Vitals.KillGold);
                match.Recorder?.RecordKill(config.zombieId, Vitals.CombatScore);
                match.Recorder?.RecordGold(GoldLedgerCategory.Kill, Vitals.KillGold);
                SfxBus.Coin(Vitals.KillGold);
                if (config.zombieId == "Overseer")
                {
                    match.Bgm?.OnBossDied(); // Boss 死亡切回轮播（2026-11-10 BGM）
                }
                Debug.Log($"[MoriGame] 击杀 {config.displayName}：+{Vitals.KillGold} 金币（战斗积分 {Vitals.CombatScore}）");
            }
            SfxBus.Death(transform.position, config.zombieId, lastHitWeak); // 音池随机，同帧限 3
            if (config.zombieId == "ShieldGuard" || config.zombieId == "Brute" || config.zombieId == "Overseer")
            {
                Vfx.VfxBus.Play(l => l.deathBrute, transform.position); // 壮汉/Boss 血爆（上限 6 丢弃最旧）
            }
            else
            {
                Vfx.VfxBus.Play(l => l.deathNormal, transform.position);
            }
            if (Vitals.KillGold > 0)
            {
                Vfx.VfxBus.Play(l => l.goldGain, transform.position + Vector3.up * 0.5f); // 击杀金币入账
            }
            if (agent.enabled)
            {
                agent.isStopped = true;
                agent.enabled = false;
            }
            if (hitColliders != null)
            {
                foreach (var hitCollider in hitColliders)
                {
                    if (hitCollider != null)
                    {
                        hitCollider.enabled = false;
                    }
                }
            }
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.ResetTrigger(AttackId);
                animator.ResetTrigger(DamageId);
                animator.SetBool(MovingId, false);
                animator.Play(DeathStateId, 0, 0f);
            }
            deathRemaining = DeathAnimationSeconds;
        }
    }
}
