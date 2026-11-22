using Ring.Inputs;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 玩家角色的最小移动：读 GameInput 的移动轴，按角色数值资产的移速在 XY 平面上走。
    /// 战斗、闪避、跳跃都不在这里——本轮只保证「玩家能被操控」，手感调参时改 unitsPerCell 与 arenaRadius。
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

        private CharacterInstance instance;
        private Transform visual;

        private void Awake()
        {
            instance = GetComponent<CharacterInstance>();
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
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            float speed = instance.Stats != null ? instance.Stats.MoveSpeedCellsPerSecond * unitsPerCell : 3f;
            Vector3 next = transform.position + (Vector3)(axis * (speed * Time.deltaTime));

            if (clampToArena)
            {
                var flat = new Vector2(next.x, next.y);
                if (flat.magnitude > arenaRadius)
                {
                    flat = flat.normalized * arenaRadius;
                    next = new Vector3(flat.x, flat.y, next.z);
                }
            }

            transform.position = next;
            FaceDirection(axis.x);
        }

        private void FaceDirection(float horizontal)
        {
            if (Mathf.Abs(horizontal) < 0.01f)
            {
                return;
            }

            if (visual == null)
            {
                visual = transform.Find("Visual");
            }

            if (visual == null)
            {
                return;
            }

            Vector3 scale = visual.localScale;
            float magnitude = Mathf.Abs(scale.x);
            visual.localScale = new Vector3(horizontal < 0f ? -magnitude : magnitude, scale.y, scale.z);
        }
    }
}
