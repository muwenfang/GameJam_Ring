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

        private CharacterInstance instance;
        private CombatBody body;
        private SwordsmanSpriteAnimation spriteAnimation;
        private StrongmanSpriteAnimation strongmanAnimation;
        private bool wasMoving;

        public bool HasDodgeInvulnerability => body != null && body.HasDodgeInvulnerability;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
            body = GetComponent<CombatBody>();
            body.ConfigureArena(arenaCenter, clampToArena, arenaRadius, unitsPerCell);
            body.SetActionEnabled(enableDodge, enableJump);
        }

        private void Update()
        {
            if (instance == null || body == null || instance.IsDead)
            {
                return;
            }

            GameInput input = GameInput.Instance;
            if (input == null)
            {
                return;
            }

            Vector2 axis = input.MoveAxis;
            Vec2 move = new Vec2(axis.x, axis.y);
            body.SetMoveInput(move);

            bool moving = move.SqrMagnitude > CombatMath.Epsilon &&
                          !instance.Combat.Immobile && !instance.Derived.Immobile;
            SetAnimationsMoving(moving);

            if (input.DodgePressed)
            {
                body.RequestDodge(move);
            }

            if (input.JumpPressed)
            {
                body.RequestJump();
            }

            if (logActionInputs && (input.DodgePressed || input.JumpPressed))
            {
                Debug.Log("[" + instance.DisplayName + "] 输入 闪避=" + input.DodgePressed
                          + " 跳跃=" + input.JumpPressed, this);
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
    }
}
