using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 相机跟随玩家：需求「相机一直跟随玩家，并显示 HUD」中的相机部分。
    ///
    /// 挂在 Main Camera 上（用户手工挂组件、把 Target 拖进 Inspector 即可；留空时自动绑
    /// <see cref="GameFlow.Player"/>）。本组件只改相机自身的 transform，不创建任何 UI / 场景物体，
    /// 也不改正交尺寸、Culling Mask 等相机设置。
    ///
    /// 三场 1v1 分布在不同区域，B8 / 位置钟声会把玩家瞬移到另一个擂台，所以订阅
    /// <see cref="GameFlow.ArenasChanged"/> 做一次 Snap()，避免相机横穿整张地图慢慢滑过去。
    /// 重开（<see cref="GameFlow.GameInitialized"/>）与返回主菜单时清掉旧目标，防止跟到已销毁的角色。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/相机跟随 CameraFollow")]
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("跟随目标；留空时自动取 GameFlow.Player（推荐留空）。")]
        private Transform target;

        [SerializeField]
        [Tooltip("是否自动从 GameFlow 解析玩家；关掉后只跟随手动拖进来的 Target。")]
        private bool autoBindPlayer = true;

        [SerializeField]
        [Tooltip("相机相对目标的 XY 偏移；Z 始终保持相机自身深度不变。")]
        private Vector3 offset = Vector3.zero;

        [SerializeField]
        [Tooltip("平滑跟随时间（秒）；0 表示立即跟随。")]
        [Min(0f)]
        private float smoothTime = 0.15f;

        [SerializeField]
        [Tooltip("是否跟随目标 X。")]
        private bool followX = true;

        [SerializeField]
        [Tooltip("是否跟随目标 Y。")]
        private bool followY = true;

        [SerializeField]
        [Tooltip("场地 / 对手重排（位置钟声、B8 重分配）时立即对齐相机，避免长距离平滑滑动。")]
        private bool snapOnArenasChanged = true;

        [Header("场地锁定（地图屏幕对齐）")]
        [SerializeField]
        [Tooltip("把相机限制在当前地图的场地矩形内。地图按视口等比铺满时，Y 会被钉住、X 只能小幅平移，背景不会被跟出屏幕。")]
        private bool clampToStage = true;

        [SerializeField]
        [Tooltip("战斗地图展示器；留空自动在场景里找。")]
        private BattleMapPresenter mapPresenter;

        [SerializeField]
        [Tooltip("是否把目标切换打印到 Console。")]
        private bool logFollow;

        private Vector3 velocity;
        private GameFlow gameFlow;
        private bool presenterSearched;

        private void Start()
        {
            gameFlow = GameFlow.Instance;
            if (gameFlow != null)
            {
                gameFlow.StateChanged += HandleStateChanged;
                gameFlow.GameInitialized += HandleGameInitialized;
                gameFlow.ArenasChanged += HandleArenasChanged;
            }

            TryBindTarget();
            Snap();
        }

        private void OnDestroy()
        {
            if (gameFlow == null)
            {
                return;
            }

            gameFlow.StateChanged -= HandleStateChanged;
            gameFlow.GameInitialized -= HandleGameInitialized;
            gameFlow.ArenasChanged -= HandleArenasChanged;
        }

        private void LateUpdate()
        {
            if (autoBindPlayer && (target == null || !target.gameObject.activeInHierarchy))
            {
                TryBindTarget();
            }

            if (target == null)
            {
                return;
            }

            Vector3 desired = transform.position;
            if (followX)
            {
                desired.x = target.position.x + offset.x;
            }

            if (followY)
            {
                desired.y = target.position.y + offset.y;
            }

            desired = ClampToStage(desired);

            // 刻意不改 Z：Main Camera 在场景里的深度（通常 -10）由用户自己设定。
            if (smoothTime <= 0f)
            {
                velocity = Vector3.zero;
                transform.position = desired;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            }
        }

        /// <summary>手动换跟随目标（B12 复制体视角、过场演出等可用）。</summary>
        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            velocity = Vector3.zero;
        }

        public void ClearTarget()
        {
            target = null;
            velocity = Vector3.zero;
        }

        /// <summary>立刻把相机对齐到目标（不做平滑）。</summary>
        public void Snap()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = transform.position;
            if (followX)
            {
                desired.x = target.position.x + offset.x;
            }

            if (followY)
            {
                desired.y = target.position.y + offset.y;
            }

            desired = ClampToStage(desired);

            velocity = Vector3.zero;
            transform.position = desired;
        }

        /// <summary>
        /// 把相机中心限制在场地矩形内：视口比场地大时直接居中，比场地小时只允许在余量内平移。
        /// 这一步保证「地图按视口对齐」在相机跟随时依然成立，而不是跟两步就露出黑边。
        /// </summary>
        private Vector3 ClampToStage(Vector3 desired)
        {
            if (!clampToStage)
            {
                return desired;
            }

            Camera cam = GetComponent<Camera>();
            if (cam == null || !cam.orthographic)
            {
                return desired;
            }

            if (mapPresenter == null && !presenterSearched)
            {
                mapPresenter = FindObjectOfType<BattleMapPresenter>();
                presenterSearched = true;
            }

            MapStage stage;
            if (mapPresenter == null || !mapPresenter.TryGetStage(out stage))
            {
                return desired;
            }

            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Rect rect = stage.WorldRect;

            desired.x = rect.width <= halfW * 2f
                ? rect.center.x
                : Mathf.Clamp(desired.x, rect.xMin + halfW, rect.xMax - halfW);

            desired.y = rect.height <= halfH * 2f
                ? rect.center.y
                : Mathf.Clamp(desired.y, rect.yMin + halfH, rect.yMax - halfH);

            return desired;
        }

        private void HandleStateChanged(MatchState from, MatchState to)
        {
            if (to == MatchState.MainMenu)
            {
                ClearTarget();
                return;
            }

            TryBindTarget();
            Snap();
        }

        private void HandleGameInitialized()
        {
            // 重开时旧角色已被销毁，必须丢掉引用，等新一局再自动绑。
            ClearTarget();
        }

        private void HandleArenasChanged(ArenaPlan plan)
        {
            if (!snapOnArenasChanged)
            {
                return;
            }

            TryBindTarget();
            Snap();
        }

        private void TryBindTarget()
        {
            if (!autoBindPlayer)
            {
                return;
            }

            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            if (gameFlow == null || gameFlow.Player == null)
            {
                return;
            }

            Transform playerTransform = gameFlow.Player.transform;
            if (target == playerTransform)
            {
                return;
            }

            SetTarget(playerTransform);
            if (logFollow)
            {
                Debug.Log("[相机] 跟随目标 → " + gameFlow.Player.DisplayName, this);
            }
        }
    }
}
