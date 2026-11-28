using System;
using System.Collections.Generic;
using Ring.Combat;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>场景里可序列化的墙（格坐标，中心 + 尺寸）。</summary>
    [Serializable]
    public struct CombatWallDefinition
    {
        public Vector2 center;
        public Vector2 size;

        public CombatWall ToWall() => new CombatWall(new Vec2(center.x, center.y), new Vec2(size.x, size.y));
    }

    /// <summary>
    /// 角色运动体：横版地面左右移动（可关掉地面锁定恢复四向）、2 格跳跃、闪避无敌帧、
    /// 击退/强制拉回与墙体碰撞。跳跃是 <c>JumpOffset</c> 的视觉高度，不改变根节点在地面线上的位置。
    /// 只依赖 CharacterInstance 的派生数值与 Combat 状态，玩家和 AI 都可以挂。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterInstance))]
    [AddComponentMenu("钟声/战斗运动体 CombatBody")]
    public sealed class CombatBody : MonoBehaviour
    {
        [Header("场地")]
        [SerializeField] private float unitsPerCell = 1f;
        [SerializeField] private bool clampToArena = true;
        [SerializeField] private Vector2 arenaCenter;
        [SerializeField] private float arenaRadius = 6.5f;
        [SerializeField] private float bodyRadius = 0.3f;
        [SerializeField] private CombatWallDefinition[] walls = Array.Empty<CombatWallDefinition>();

        [Header("矩形场地（25×12 横版地图）")]
        [Tooltip("开：用轴对齐矩形约束（左右边界）。是否允许竖向移动由下面的「地面锁定」决定。关：沿用旧的圆形约束。")]
        [SerializeField] private bool useRectArena = true;
        [Tooltip("矩形可玩区左下角（世界坐标）。由 MatchSpawner 按当前地图写入。")]
        [SerializeField] private Vector2 arenaRectMin = new Vector2(-6.5f, -6.5f);
        [Tooltip("矩形可玩区右上角（世界坐标）。由 MatchSpawner 按当前地图写入。")]
        [SerializeField] private Vector2 arenaRectMax = new Vector2(6.5f, 6.5f);

        [Header("地面锁定（横版左右移动）")]
        [Tooltip("开：角色只能沿地面左右移动，Y 恒等于地面线；跳跃只走 JumpOffset 的视觉偏移，不改变根节点高度。关：恢复四向自由移动。")]
        [SerializeField] private bool lockToGround = true;
        [Tooltip("地面线世界 Y（角色脚底）。由 MatchSpawner 按当前地图的地面顶面写入；未写入时取角色出生高度。")]
        [SerializeField] private float groundLineY;

        [Header("动作")]
        [SerializeField] private bool enableDodge = true;
        [SerializeField] private bool enableJump = true;

        [Header("闪避（全角色统一规格，见架构分析 md §M2.1）")]
        [SerializeField] private float dodgeDistanceCells = 1f;
        [SerializeField] private float dodgeCooldownSeconds = 0.5f;
        [SerializeField] private float dodgeInvulnerabilitySeconds = 0.1f;
        [SerializeField] private float jumpDurationSeconds = 0.45f;
        [SerializeField] private float knockbackDurationSeconds = 0.2f;
        [SerializeField] private bool logActions;

        private CharacterInstance instance;
        private readonly List<CombatWall> wallList = new List<CombatWall>(8);

        private Vec2 moveInput;
        private Vec2 facing = Vec2.Right;
        private int facingSign = 1;

        private float jumpHeight;
        private float jumpVelocity;
        private float jumpGravity;
        private bool airborne;
        private bool launched;
        private Vec2 launchVelocity;
        private Transform jumpOffset;
        private SpriteRenderer spriteRenderer;

        private float dodgeRemaining;
        private float dodgeSpeed;
        private Vec2 dodgeDirection;
        private float dodgeReadyTime;
        private float dodgeInvulnerableUntil;

        private Vec2 knockbackDirection;
        private float knockbackRemaining;
        private float knockbackSpeed;

        private Vec2 pullTarget;
        private float pullSpeed;
        private float pullTimeout;
        private bool pulling;

        public Vec2 FacingVector => facing;
        public int FacingSign => facingSign;
        public bool IsAirborne => airborne;
        public bool IsLaunched => launched;
        public bool IsDodging => dodgeRemaining > 0f;
        public bool IsPulling => pulling;
        public bool HasDodgeInvulnerability => Time.time < dodgeInvulnerableUntil;
        public float JumpHeight => jumpHeight;
        public IReadOnlyList<CombatWall> Walls => wallList;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
            // 地面锁定默认开启：先把出生高度当成地面线，避免开局第一帧被拉到 y=0。
            // 真正的落位由 MatchSpawner 用当前地图的 GroundSurfaceY 覆盖。
            groundLineY = transform.position.y;
            RebuildWalls();
        }

        private void Update()
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vec2 position = new Vec2(transform.position.x, transform.position.y);
            Vec2 delta = Vec2.Zero;

            if (dodgeRemaining > 0f)
            {
                float step = Math.Min(dodgeRemaining, deltaTime);
                delta = dodgeDirection * (dodgeSpeed * step);
                dodgeRemaining -= step;
            }
            else
            {
                if (pulling)
                {
                    pullTimeout -= deltaTime;
                    Vec2 toTarget = pullTarget - position;
                    float distance = toTarget.Magnitude;
                    if (distance <= 0.05f || pullTimeout <= 0f)
                    {
                        pulling = false;
                    }
                    else
                    {
                        delta = toTarget.Normalized * Math.Min(distance, pullSpeed * deltaTime);
                    }
                }

                if (knockbackRemaining > 0f)
                {
                    float step = Math.Min(knockbackRemaining, knockbackSpeed * deltaTime);
                    delta += knockbackDirection * step;
                    knockbackRemaining -= step;
                }

                if (launched)
                {
                    delta += launchVelocity * deltaTime;
                }

                if (!instance.Combat.Immobile && !instance.Derived.Immobile)
                {
                    delta += moveInput * (instance.MoveSpeedCellsPerSecond * unitsPerCell * deltaTime);
                }
            }

            // 地面锁定时即使本帧没有任何位移输入，也要解算一次：
            // 这样 Teleport / 拉回 / 落位把角色放到别的高度后，会被立刻压回地面线，
            // 不会出现「站着的位置比地面高一截、要按一下方向键才掉回去」。
            if (lockToGround || delta.SqrMagnitude > 0f)
            {
                position = SolveMotion(position, delta);
                transform.position = new Vector3(position.X, position.Y, transform.position.z);
            }

            UpdateJump(deltaTime);
        }

        private void LateUpdate()
        {
            if (jumpOffset == null)
            {
                jumpOffset = transform.Find("JumpOffset");
            }

            if (jumpOffset != null)
            {
                jumpOffset.localPosition = new Vector3(0f, jumpHeight * unitsPerCell, 0f);
            }
        }

        // ---------------- 输入 ----------------

        public void SetMoveInput(Vec2 input)
        {
            SetMoveInput(input, true);
        }

        /// <summary>
        /// 更新移动输入；动作期间可关闭「移动方向带动朝向」，保留攻击锁定的朝向。
        /// </summary>
        public void SetMoveInput(Vec2 input, bool updateFacing)
        {
            moveInput = input.SqrMagnitude > 1f ? input.Normalized : input;
            if (updateFacing && moveInput.SqrMagnitude > CombatMath.Epsilon)
            {
                SetFacing(moveInput);
            }
        }

        public void SetFacing(Vec2 direction)
        {
            if (direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                return;
            }

            facing = direction.Normalized;
            int sign = facing.X < 0f ? -1 : 1;
            if (sign == facingSign)
            {
                return;
            }

            facingSign = sign;
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (spriteRenderer != null)
            {
                spriteRenderer.flipX = sign < 0;
            }
        }

        public void SetArenaCenter(Vector2 center)
        {
            arenaCenter = center;
        }

        public void SetArenaCenter(Vector3 center)
        {
            arenaCenter = new Vector2(center.x, center.y);
        }

        /// <summary>
        /// 只改「一格 = 多少世界单位」。地图按 25×12 铺满视口时一格约 0.83 世界单位，
        /// 移速 / 闪避距离 / 跳跃高度的格数口径必须跟着改，否则角色相对地图会快 20%。
        /// 不改场地半径，半径由 <see cref="ConfigureArena"/> 单独给。
        /// </summary>
        public void SetUnitsPerCell(float units)
        {
            unitsPerCell = units > 0f ? units : 1f;
        }

        /// <summary>当前一格对应的世界单位。</summary>
        public float UnitsPerCell
        {
            get { return unitsPerCell; }
        }

        public void ConfigureArena(Vector2 center, bool clamp, float radius, float units)
        {
            arenaCenter = center;
            clampToArena = clamp;
            arenaRadius = Mathf.Max(0f, radius);
            unitsPerCell = units > 0f ? units : 1f;
            // 调用圆形配置即明确切回圆形约束；矩形配置请用 ConfigureArenaRect。
            useRectArena = false;
        }

        /// <summary>
        /// 把场地约束切成「轴对齐矩形」，供 25×12 横版地图落位层调用。
        /// 只定义**左右**可走区间；竖向是否可走由 <see cref="lockToGround"/> 决定：
        /// 开着就锁在地面线上左右移动，关掉才是四向自由移动（旧口径）。
        /// </summary>
        /// <param name="min">矩形左下角（世界坐标，未内缩角色半径）。</param>
        /// <param name="max">矩形右上角（世界坐标，未内缩角色半径）。</param>
        /// <param name="units">一格对应的世界单位。</param>
        public void ConfigureArenaRect(Vector2 min, Vector2 max, float units)
        {
            ConfigureArenaRect(min, max, units, Mathf.Min(min.y, max.y));
        }

        /// <summary>
        /// 矩形场地 + 显式地面线。地面锁定时角色只会在这条线上左右移动。
        /// </summary>
        /// <param name="groundY">角色脚底所在的地面线世界 Y（由 MatchSpawner 按地图 GroundSurfaceY 给）。</param>
        public void ConfigureArenaRect(Vector2 min, Vector2 max, float units, float groundY)
        {
            useRectArena = true;
            arenaRectMin = new Vector2(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y));
            arenaRectMax = new Vector2(Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            arenaCenter = (arenaRectMin + arenaRectMax) * 0.5f;
            arenaRadius = Mathf.Max(0f,
                Mathf.Min(arenaRectMax.x - arenaRectMin.x, arenaRectMax.y - arenaRectMin.y) * 0.5f);
            clampToArena = true;
            unitsPerCell = units > 0f ? units : 1f;
            groundLineY = groundY;
        }

        /// <summary>运行时切换「只能沿地面左右移动」。</summary>
        public void SetGroundLock(bool enabled, float groundY)
        {
            lockToGround = enabled;
            groundLineY = groundY;
        }

        /// <summary>只改地面线高度（切地图 / 换视口后重摆时用）。</summary>
        public void SetGroundLine(float groundY)
        {
            groundLineY = groundY;
        }

        /// <summary>当前是否只能沿地面左右移动。</summary>
        public bool IsGroundLocked => lockToGround;

        /// <summary>当前地面线的世界 Y（角色脚底）。</summary>
        public float GroundLineY => groundLineY;

        /// <summary>当前是否使用矩形场地约束（自检 / 编辑器工具用）。</summary>
        public bool UsesRectArena => useRectArena;

        public void SetActionEnabled(bool dodgeEnabled, bool jumpEnabled)
        {
            enableDodge = dodgeEnabled;
            enableJump = jumpEnabled;
        }

        public void SetWalls(IEnumerable<CombatWall> newWalls)
        {
            wallList.Clear();
            if (newWalls != null)
            {
                foreach (CombatWall wall in newWalls)
                {
                    wallList.Add(wall);
                }
            }
        }

        private void RebuildWalls()
        {
            wallList.Clear();
            if (walls == null)
            {
                return;
            }

            for (int i = 0; i < walls.Length; i++)
            {
                wallList.Add(walls[i].ToWall());
            }
        }

        // ---------------- 动作 ----------------

        public bool RequestJump()
        {
            if (!enableJump || airborne || instance == null || instance.IsDead || instance.Combat.Immobile)
            {
                return false;
            }

            float height = instance.Stats != null ? instance.Stats.JumpHeightCells : 2f;
            if (height <= 0f)
            {
                height = 2f;
            }

            float duration = Mathf.Max(0.1f, jumpDurationSeconds);
            float actionSpeed = instance.Derived.ActionSpeedMultiplier;
            if (actionSpeed > 0.01f)
            {
                duration /= actionSpeed;
            }
            jumpHeight = 0f;
            jumpVelocity = 4f * height / duration;
            jumpGravity = 8f * height / (duration * duration);
            airborne = true;
            launched = false;
            launchVelocity = Vec2.Zero;
            if (logActions)
            {
                Debug.Log("[" + instance.DisplayName + "] 跳跃 " + height + " 格", this);
            }

            return true;
        }

        public bool RequestDodge(Vec2 direction)
        {
            if (!enableDodge || instance == null || instance.IsDead)
            {
                return false;
            }

            if (dodgeRemaining > 0f || Time.time < dodgeReadyTime)
            {
                return false;
            }

            if (instance.Combat.Status.IsStunned || instance.Combat.Status.IsRooted)
            {
                return false;
            }

            if (direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                direction = facing;
            }

            float distanceCells = Mathf.Max(0.5f, dodgeDistanceCells);
            float duration = instance.Stats != null ? Mathf.Max(0.05f, instance.Stats.DodgeDurationSeconds) : 0.25f;
            dodgeDirection = direction.Normalized;
            dodgeRemaining = duration;
            dodgeSpeed = distanceCells * unitsPerCell / duration;
            dodgeReadyTime = Time.time + Mathf.Max(0.1f, dodgeCooldownSeconds);
            dodgeInvulnerableUntil = Time.time + Mathf.Max(0f, dodgeInvulnerabilitySeconds);
            instance.GrantInvulnerability(Mathf.Max(0f, dodgeInvulnerabilitySeconds));
            SetFacing(dodgeDirection);
            if (logActions)
            {
                Debug.Log("[" + instance.DisplayName + "] 闪避", this);
            }

            return true;
        }

        public void ApplyKnockback(Vec2 direction, float cells)
        {
            if (instance == null || instance.IsDead || cells <= 0f || instance.Combat.KnockbackImmune)
            {
                return;
            }

            if (direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                direction = -facing;
            }

            knockbackDirection = direction.Normalized;
            knockbackRemaining = cells;
            knockbackSpeed = cells / Mathf.Max(0.05f, knockbackDurationSeconds);
            pulling = false;
        }

        /// <summary>击飞：水平位移 + 垂直抛物线，落地前仍可被墙体阻挡。霸体/击退免疫时不生效。</summary>
        public bool ApplyLaunch(Vec2 direction, float horizontalCells, float heightCells, float durationSeconds = 0.7f)
        {
            if (instance == null || instance.IsDead || instance.Combat.KnockbackImmune)
            {
                return false;
            }

            float duration = Mathf.Max(0.2f, durationSeconds);
            if (direction.SqrMagnitude <= CombatMath.Epsilon)
            {
                direction = -facing;
            }

            launchVelocity = direction.Normalized * (Mathf.Max(0f, horizontalCells) * unitsPerCell / duration);
            launched = true;
            airborne = true;
            jumpHeight = 0f;
            jumpVelocity = 4f * Mathf.Max(0.1f, heightCells) / duration;
            jumpGravity = 8f * Mathf.Max(0.1f, heightCells) / (duration * duration);
            dodgeRemaining = 0f;
            knockbackRemaining = 0f;
            pulling = false;
            return true;
        }

        /// <summary>强制拉回：以固定速度飞向目标点，途中仍受墙体约束。</summary>
        public void PullTo(Vec2 target, float speed, float timeoutSeconds = 1.5f)
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            pullTarget = target;
            pullSpeed = Mathf.Max(0.1f, speed);
            pullTimeout = Mathf.Max(0.05f, timeoutSeconds);
            pulling = true;
            dodgeRemaining = 0f;
            knockbackRemaining = 0f;
        }

        public void CancelPull()
        {
            pulling = false;
        }

        public void Teleport(Vec2 position)
        {
            transform.position = new Vector3(position.X, position.Y, transform.position.z);
            dodgeRemaining = 0f;
            knockbackRemaining = 0f;
            pulling = false;
            launched = false;
            airborne = false;
            jumpHeight = 0f;
            jumpVelocity = 0f;
            launchVelocity = Vec2.Zero;
        }

        /// <summary>瞬移一段距离（冲刺/突进），仍然遵守墙体与场地边界。</summary>
        public Vec2 MoveInstant(Vec2 direction, float cells)
        {
            Vec2 position = new Vec2(transform.position.x, transform.position.y);
            if (direction.SqrMagnitude <= CombatMath.Epsilon || cells <= 0f)
            {
                return position;
            }

            Vec2 delta = direction.Normalized * (cells * unitsPerCell);
            position = SolveMotion(position, delta);
            transform.position = new Vector3(position.X, position.Y, transform.position.z);
            SetFacing(direction);
            return position;
        }

        /// <summary>
        /// 按当前场地形状求解位移：
        /// - <see cref="lockToGround"/> 开：横版地面锁定，只解算 X，Y 恒等于地面线（跳跃是视觉偏移）；
        /// - 否则矩形给 25×12 地图（四向），圆形留给旧逻辑。
        /// 三条路径都走同一个 <see cref="MotionSolver"/> 的墙体阻挡，不会出现「某种模式会穿墙」。
        /// </summary>
        private Vec2 SolveMotion(Vec2 position, Vec2 delta)
        {
            float radius = Math.Max(0.01f, bodyRadius);
            if (lockToGround)
            {
                return MotionSolver.MoveGround(position, delta, radius, wallList,
                    arenaRectMin.x, arenaRectMax.x, groundLineY);
            }

            if (useRectArena)
            {
                return MotionSolver.MoveRect(position, delta, radius, wallList,
                    ToVec2(arenaRectMin), ToVec2(arenaRectMax));
            }

            return MotionSolver.Move(position, delta, radius, wallList,
                clampToArena, ToVec2(arenaCenter), arenaRadius);
        }

        private void UpdateJump(float deltaTime)
        {
            if (!airborne)
            {
                jumpHeight = 0f;
                launched = false;
                return;
            }

            jumpHeight += jumpVelocity * deltaTime;
            jumpVelocity -= jumpGravity * deltaTime;
            if (jumpHeight <= 0f && jumpVelocity < 0f)
            {
                jumpHeight = 0f;
                jumpVelocity = 0f;
                jumpGravity = 0f;
                airborne = false;
                launched = false;
                launchVelocity = Vec2.Zero;
            }
        }

        private static Vec2 ToVec2(Vector2 value) => new Vec2(value.x, value.y);
    }
}
