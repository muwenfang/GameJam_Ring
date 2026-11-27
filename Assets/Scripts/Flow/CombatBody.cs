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
    /// 角色运动体：四向移动、2 格跳跃、空中可移动、闪避无敌帧、击退/强制拉回与墙体碰撞。
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

            if (delta.SqrMagnitude > 0f)
            {
                position = MotionSolver.Move(position, delta, Math.Max(0.01f, bodyRadius), wallList,
                    clampToArena, ToVec2(arenaCenter), arenaRadius);
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

        public void ConfigureArena(Vector2 center, bool clamp, float radius, float units)
        {
            arenaCenter = center;
            clampToArena = clamp;
            arenaRadius = Mathf.Max(0f, radius);
            unitsPerCell = units > 0f ? units : 1f;
        }

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
            position = MotionSolver.Move(position, delta, Math.Max(0.01f, bodyRadius), wallList,
                clampToArena, ToVec2(arenaCenter), arenaRadius);
            transform.position = new Vector3(position.X, position.Y, transform.position.z);
            SetFacing(direction);
            return position;
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
