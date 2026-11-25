using Ring.Abilities;
using Ring.Characters;
using Ring.Inputs;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 玩家角色的移动与位移类输入：读 GameInput 的移动轴按资产移速在 XY 平面上走，
    /// 并把右键闪避 / 空格跳跃接到显式入口（此前这两个动作没有任何消费者）。
    /// 手感调参改 unitsPerCell 与 arenaRadius；闪避数值来自 CharacterBaseStats 资产。
    ///
    /// 表现层只做两件事：把「是否在移动」告诉 SwordsmanSpriteAnimation，
    /// 以及用 SpriteRenderer.flipX 翻转朝向（不用负缩放，避免和序列帧的缩放/脚底对齐冲突）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterInstance))]
    [AddComponentMenu("钟声/玩家移动 PlayerMotor")]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("一格对应多少世界单位。数值表里的移速单位是「格/秒」。")]
        private float unitsPerCell = 1f;

        [SerializeField]
        [Tooltip("是否把玩家限制在圆形场地内（防止一开局跑出画面）。")]
        private bool clampToArena = true;

        [SerializeField]
        [Tooltip("场地半径（世界单位）。")]
        private float arenaRadius = 6.5f;

        [SerializeField]
        [Tooltip("当前擂台中心（世界 XY）。由 MatchSpawner.ApplyPlan 按玩家所在区域写入；三场 1v1 分布在不同区域，不能一律以原点为圆心。")]
        private Vector2 arenaCenter;

        [Header("输入接线（闪避 / 跳跃）")]
        [SerializeField]
        [Tooltip("是否把鼠标右键的闪避输入接到位移逻辑。数值取角色资产的闪避参数。")]
        private bool enableDodge = true;

        [SerializeField]
        [Tooltip("是否接收空格跳跃输入。俯视 2D 下跳跃表现未定义，这里只做显式接线与反馈。")]
        private bool enableJump = true;

        [SerializeField]
        [Tooltip("闪避 / 跳跃输入被接住时打印一条日志，方便在 Console 确认接线生效。")]
        private bool logActionInputs = true;

        private float dodgeReadyTime;

        private CharacterInstance instance;
        private SpriteRenderer visualRenderer;
        private SwordsmanSpriteAnimation spriteAnimation;
        private bool animationSearched;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
        }

        /// <summary>
        /// 由 <see cref="MatchSpawner.ApplyPlan"/> 写入玩家当前所在擂台的中心。
        /// 开局、位置钟声、B8 死亡重分配都会重新调用，所以圆形边界永远跟着玩家走。
        /// </summary>
        public void SetArenaCenter(Vector3 worldCenter)
        {
            arenaCenter = new Vector2(worldCenter.x, worldCenter.y);
        }

        public void SetArenaCenter(Vector2 center)
        {
            arenaCenter = center;
        }

        private void Update()
        {
            if (instance == null || instance.IsDead)
            {
                return;
            }

            GameInput input = GameInput.Instance;
            if (input == null)
            {
                return;
            }

            Vector2 axis = input.MoveAxis;

            // 右键闪避 / 空格跳跃此前没有任何消费者，输入被静默吞掉。这里显式接线。
            if (input.DodgePressed)
            {
                TryDodge(axis);
            }

            if (input.JumpPressed)
            {
                TryJump();
            }

            bool hasInput = axis.sqrMagnitude >= 0.0001f;
            bool immobile = hasInput && instance.Derived.Immobile;

            // 有输入但被定身（紧急排气、凝神定身等）：不位移，也不播跑步。
            bool moving = hasInput && !immobile;

            SwordsmanSpriteAnimation animation = SpriteAnimation;
            if (animation != null)
            {
                animation.SetMoving(moving);
            }

            if (!moving)
            {
                return;
            }

            // 移速已经含「过载损耗」的按生命比例、风之优雅的叠层加成、高性能模式等限时增益。
            float speed = instance.MoveSpeedCellsPerSecond * unitsPerCell;
            if (speed <= 0f)
            {
                speed = 3f;
            }

            Vector3 next = transform.position + (Vector3)(axis * (speed * Time.deltaTime));

            if (clampToArena)
            {
                next = ClampToArena(next);
            }

            transform.position = next;
            FaceDirection(axis.x);
        }

        /// <summary>把位置限制在以 <see cref="arenaCenter"/> 为圆心的擂台内。</summary>
        private Vector3 ClampToArena(Vector3 position)
        {
            var flat = new Vector2(position.x - arenaCenter.x, position.y - arenaCenter.y);
            if (flat.magnitude <= arenaRadius)
            {
                return position;
            }

            flat = flat.normalized * arenaRadius;
            return new Vector3(arenaCenter.x + flat.x, arenaCenter.y + flat.y, position.z);
        }

        /// <summary>
        /// 闪避：按角色资产的距离做一次短距冲刺，冷却期间忽略输入。
        /// 沿用项目既有的一次性位移风格（与 AbilityCaster 的突进斩一致），
        /// 因此 DodgeDurationSeconds 暂未消费；平滑冲刺与无敌帧需要 AbilityRuntime
        /// 先提供「外部设定无敌」的接口，本次不擅自改动纯 C# 规则核心。
        /// </summary>
        private void TryDodge(Vector2 axis)
        {
            if (!enableDodge || instance == null || instance.Stats == null || Time.time < dodgeReadyTime)
            {
                return;
            }

            CharacterBaseStats stats = instance.Stats;
            Vector2 direction = axis.sqrMagnitude > 0.0001f ? axis.normalized : CurrentFacing;
            float distance = Mathf.Max(0f, stats.DodgeDistanceCells) * unitsPerCell;
            Vector3 next = transform.position + (Vector3)(direction * distance);

            if (clampToArena)
            {
                next = ClampToArena(next);
            }

            transform.position = next;
            dodgeReadyTime = Time.time + Mathf.Max(0f, stats.DodgeCooldownSeconds);

            if (logActionInputs)
            {
                Debug.Log("[玩家] 闪避触发：位移 " + distance + " 世界单位，冷却 "
                          + stats.DodgeCooldownSeconds + "s（无敌帧尚未接入）。", this);
            }
        }

        /// <summary>
        /// 跳跃：项目是 XY 平面的俯视 2D，Z 轴位移不改变显示，也没有跳跃序列帧。
        /// 这里把空格接到显式入口并给出反馈，避免输入被静默吞掉；
        /// 真正的跳跃表现需先定义「跳起」在俯视视角下的视觉语义（缩放 / 投影 / 排序）。
        /// </summary>
        private void TryJump()
        {
            if (!enableJump || instance == null || instance.Stats == null)
            {
                return;
            }

            if (logActionInputs)
            {
                Debug.Log("[玩家] 跳跃输入已接收：JumpHeightCells = " + instance.Stats.JumpHeightCells
                          + "，但俯视 2D 下跳跃表现尚未定义（无 Z 轴观感 / 无跳跃序列帧）。", this);
            }
        }

        /// <summary>没有移动输入时的兜底朝向（与 FaceDirection 用的 flipX 同源）。</summary>
        private Vector2 CurrentFacing
        {
            get
            {
                SpriteRenderer renderer = VisualRenderer;
                return renderer != null && renderer.flipX ? Vector2.left : Vector2.right;
            }
        }

        private void FaceDirection(float horizontal)
        {
            if (Mathf.Abs(horizontal) < 0.01f)
            {
                return;
            }

            SpriteRenderer renderer = VisualRenderer;
            if (renderer == null)
            {
                return;
            }

            renderer.flipX = horizontal < 0f;
        }

        private SwordsmanSpriteAnimation SpriteAnimation
        {
            get
            {
                if (!animationSearched)
                {
                    animationSearched = true;
                    spriteAnimation = GetComponentInChildren<SwordsmanSpriteAnimation>();
                }

                return spriteAnimation;
            }
        }

        private SpriteRenderer VisualRenderer
        {
            get
            {
                if (visualRenderer == null)
                {
                    visualRenderer = GetComponentInChildren<SpriteRenderer>();
                }

                return visualRenderer;
            }
        }
    }
}
