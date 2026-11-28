using Ring.Characters;
using Ring.Combat;
using Ring.Inputs;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 玩家输入适配层。移动/跳跃/闪避/击退的实际执行都在 <see cref="CombatBody"/>，
    /// 这样 AI 也能挂同一个运动体，而不必复制一份玩家逻辑。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterInstance))]
    [RequireComponent(typeof(CombatBody))]
    [AddComponentMenu("钟声/玩家移动 PlayerMotor")]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("场地")]
        [SerializeField] private float unitsPerCell = 1f;
        [SerializeField] private bool clampToArena = true;
        [SerializeField] private Vector2 arenaCenter;
        [SerializeField] private float arenaRadius = 6.5f;

        [Header("动作")]
        [SerializeField] private bool enableDodge = true;
        [SerializeField] private bool enableJump = true;
        [SerializeField] private bool logActionInputs;

        [Header("键位口径（横版地面）")]
        [Tooltip("开：W 与 Space 一样触发跳跃；S 不产生任何推进（角色只能在地面左右移动）。关：只认 Jump 键。")]
        [SerializeField] private bool moveUpAlsoJumps = true;

        private CharacterInstance instance;
        private CombatBody body;
        private SwordsmanSpriteAnimation spriteAnimation;
        private StrongmanSpriteAnimation strongmanAnimation;
        private SpartanSpriteAnimation spartanAnimation;
        private ElfRangerSpriteAnimation rangerAnimation;
        private bool wasMoving;

        public bool HasDodgeInvulnerability => body != null && body.HasDodgeInvulnerability;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
            body = GetComponent<CombatBody>();
            // 横版口径：可走区间来自 Inspector 的圆形外接矩形，竖向由 CombatBody 锁定在地面线上。
            // 地面线先取出生的世界高度，避免生成后第一帧被拉回收起；落位层随后会用真实地图覆盖。
            body.ConfigureArenaRect(
                new Vector2(arenaCenter.x - arenaRadius, arenaCenter.y - arenaRadius),
                new Vector2(arenaCenter.x + arenaRadius, arenaCenter.y + arenaRadius),
                unitsPerCell, transform.position.y);
            body.SetActionEnabled(enableDodge, enableJump);
        }

        private void Update()
        {
            // 场景中可能残留旧的测试 PlayerMotor；只有真正的玩家角色读取全局输入。
            if (instance == null || body == null || !instance.IsPlayer || instance.IsDead)
            {
                return;
            }

            GameInput input = GameInput.Instance;
            if (input == null)
            {
                return;
            }

            // 横版地面：只取水平分量。W/S 不再产生推进（S 完全无功能，W 改为跳跃）。
            // 竖向移动由 CombatBody 的地面锁定兜底，这里不把 W/S 传给运动体，避免「按住 W 一直往上飘」。
            Vector2 axis = input.MoveAxis;
            Vec2 move = new Vec2(axis.x, 0f);
            // 攻击动画期间保留施放时锁定的朝向，避免移动输入把 SpriteRenderer.flipX 改回去。
            bool actionPlaying = (SpriteAnimation != null && SpriteAnimation.IsActing)
                                 || (StrongmanAnimation != null && StrongmanAnimation.IsActing)
                                 || (SpartanAnimation != null && SpartanAnimation.IsActing)
                                 || (RangerAnimation != null && RangerAnimation.IsActing);
            body.SetMoveInput(move, updateFacing: !actionPlaying);

            bool moving = move.SqrMagnitude > CombatMath.Epsilon &&
                          !instance.Combat.Immobile && !instance.Derived.Immobile;
            SetAnimationsMoving(moving);

            if (input.DodgePressed && body.RequestDodge(move))
            {
                // 闪避分镜：有方向输入时按「与当前朝向的点积」判前后闪；无输入视为前闪。
                ElfRangerSpriteAnimation ranger = RangerAnimation;
                if (ranger != null)
                {
                    bool forward = move.SqrMagnitude <= CombatMath.Epsilon
                                   || Vec2.Dot(move, body.FacingVector) >= 0f;
                    ranger.PlayDodge(forward);
                }
            }

            // W 与 Space 同为跳跃键；W 走 MoveUp 动作的按下沿，按住不会连跳。
            bool jumpPressed = input.JumpPressed
                               || (moveUpAlsoJumps && input.WasPressed(InputActionId.MoveUp));

            if (jumpPressed && body.RequestJump())
            {
                // 跳跃分镜显式绑定：与 SetMoving / PlayPunch / PlayDash 一样由输入适配层驱动。
                // 力士有 Jump_00~07、游侠有 Jump_00~05；剑士与斯巴达没有跳跃序列帧，保持当前帧。
                // 动画组件内部对 IsAirborne 的轮询仅作兜底（覆盖 AI / 技能起跳），并做幂等保护。
                StrongmanSpriteAnimation strongman = StrongmanAnimation;
                if (strongman != null)
                {
                    strongman.PlayJump();
                }

                RangerAnimation?.PlayJump();
            }

            if (logActionInputs && (input.DodgePressed || jumpPressed))
            {
                Debug.Log("[" + instance.DisplayName + "] 输入 闪避=" + input.DodgePressed
                          + " 跳跃=" + jumpPressed, this);
            }
        }

        public void SetArenaCenter(Vector3 worldCenter)
        {
            arenaCenter = new Vector2(worldCenter.x, worldCenter.y);
            body?.SetArenaCenter(arenaCenter);
        }

        public void SetArenaCenter(Vector2 center)
        {
            arenaCenter = center;
            body?.SetArenaCenter(center);
        }

        /// <summary>
        /// 同步「一格 = 多少世界单位」。只改格距，不动场地半径——
        /// 半径由落位层按地图场地单独配置（见 MatchSpawner）。
        /// </summary>
        public void SetUnitsPerCell(float units)
        {
            unitsPerCell = units > 0f ? units : 1f;
            body?.SetUnitsPerCell(unitsPerCell);
        }

        private void SetAnimationsMoving(bool moving)
        {
            if (moving == wasMoving)
            {
                return;
            }

            wasMoving = moving;
            SwordsmanSpriteAnimation animation = SpriteAnimation;
            if (animation != null)
            {
                animation.SetMoving(moving);
            }

            StrongmanSpriteAnimation strongman = StrongmanAnimation;
            if (strongman != null)
            {
                strongman.SetMoving(moving);
            }
            SpartanAnimation?.SetMoving(moving);
            RangerAnimation?.SetMoving(moving);
        }

        private SwordsmanSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponentInChildren<SwordsmanSpriteAnimation>();
                }

                return spriteAnimation;
            }
        }

        private StrongmanSpriteAnimation StrongmanAnimation
        {
            get
            {
                if (strongmanAnimation == null)
                {
                    strongmanAnimation = GetComponentInChildren<StrongmanSpriteAnimation>();
                }

                return strongmanAnimation;
            }
        }

        private SpartanSpriteAnimation SpartanAnimation
        {
            get
            {
                if (spartanAnimation == null) spartanAnimation = GetComponentInChildren<SpartanSpriteAnimation>();
                return spartanAnimation;
            }
        }

        private ElfRangerSpriteAnimation RangerAnimation
        {
            get
            {
                if (rangerAnimation == null) rangerAnimation = GetComponentInChildren<ElfRangerSpriteAnimation>();
                return rangerAnimation;
            }
        }
    }
}
