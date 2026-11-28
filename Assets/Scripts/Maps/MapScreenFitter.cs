using UnityEngine;

namespace Ring.Maps
{
    /// <summary>
    /// 把地图背景按相机视口「等比铺满」，并让地面碰撞体精确覆盖背景图里画出来的那一层地面。
    ///
    /// 依据（Assets/MapKit/Resources/MapKit/map_config.json 与图片导入设置，均已实测）：
    /// - 六张背景图统一 1600×768、textureType=8(Sprite)、PPU=64、pivot=(0.5,0.5)
    ///   → sprite 原始尺寸正好 25 × 12 世界单位；
    /// - grid: cols=25, rows=12, tileUnits=1 → 网格 25 列 × 12 行、每格 1 单位；
    /// - 地面只有最底 1 行：ForestMechanic.groundSurfaceY=1、ForestFireflies.groundHeight=1
    ///   （注释「地面高度（格，从背景底边算起）」）、SteamMechanic 的 25 个井盖格、
    ///   BambooNightMechanic 的 cy=12f 从顶部落下，都指向同一套「左下角原点」坐标。
    ///   所以「看地图图片中的地面」= 图片最底部 1/12 高度的那条带，
    ///   碰撞地面就是这一条，不是整个屏幕。
    ///
    /// 本组件只修改「已有物体」的 transform 与 BoxCollider2D 数值，
    /// 不创建 / 销毁任何物体或组件，也不碰角色、输入、钟声、UI。
    /// 生成工具会把它挂在每张地图根节点上并连好四条引用；想纯手工接线，
    /// 直接把对应物体拖进 Inspector、或取消勾选 Auto Fit 后自己填数值即可。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/地图/地图屏幕对齐 MapScreenFitter")]
    public class MapScreenFitter : MonoBehaviour
    {
        private const string BackgroundChildName = "BG_背景";
        private const string GroundChildName = "Ground_地面25格";

        [Header("引用（生成工具自动连线，也可手工拖）")]
        [SerializeField]
        [Tooltip("留空时用 Camera.main。生成工具会显式拖入场景主相机，避免 tag 依赖。")]
        private Camera targetCamera;

        [SerializeField]
        [Tooltip("本图背景 SpriteRenderer。留空时按子物体名 BG_背景 自动查找。")]
        private SpriteRenderer background;

        [SerializeField]
        [Tooltip("本图地面根节点。留空时按子物体名 Ground_地面25格 自动查找。")]
        private Transform groundRoot;

        [SerializeField]
        [Tooltip("本图地面碰撞体。留空时从 Ground Root 上取 BoxCollider2D。")]
        private BoxCollider2D groundCollider;

        [Header("设计网格（与 map_config.json 的 25 × 12 一致）")]
        [SerializeField]
        [Min(1f)]
        [Tooltip("背景图宽度对应的总列数，用来算「一格多宽」。默认 25（MapKit 口径）。")]
        private float designColumns = MapStageBuilder.DesignColumns;

        [SerializeField]
        [Min(1f)]
        [Tooltip("背景图高度对应的总行数，用来把「一行地面」换算成图片比例。默认 12。")]
        private float designRows = MapStageBuilder.DesignRows;

        [SerializeField]
        [Min(0.01f)]
        [Tooltip("碰撞地面占最底部几行。默认 1 —— 地图图片里只画了这一层，不是全屏。想抬高顶面可增大。")]
        private float groundRows = 1f;

        [SerializeField]
        [Tooltip("背景 Sprite 为空时的兜底尺寸（25 × 12 世界单位）。")]
        private Vector2 fallbackSpriteSize = MapStageBuilder.FallbackSpriteSize;

        [Header("运行方式")]
        [SerializeField]
        [Tooltip("进入这张地图（OnEnable）时按当前视口重算一次。关掉则完全使用 Inspector 里的静态数值。")]
        private bool fitOnEnable = true;

        [SerializeField]
        [Tooltip("Game 视图宽高比变化时（例如拖动 Free Aspect 窗口）自动重算。")]
        private bool refitOnAspectChange = true;

        private float lastAspect = -1f;
        private MapStage stage;

        /// <summary>最近一次计算出的背景世界缩放系数，供校验与调试读取。</summary>
        public float LastFitScale { get; private set; }

        /// <summary>最近一次计算出的地面顶面世界 Y，供校验与调试读取。</summary>
        public float LastGroundSurfaceY { get; private set; }

        /// <summary>最近一次计算出的场地几何（背景矩形 + 25×12 格 + 地面）。</summary>
        public MapStage Stage
        {
            get { return stage; }
        }

        /// <summary>是否已经算出有效场地。</summary>
        public bool HasStage
        {
            get { return stage.IsValid; }
        }

        /// <summary>设计列数（默认 25）。</summary>
        public float DesignColumns
        {
            get { return designColumns; }
        }

        /// <summary>设计行数（默认 12）。</summary>
        public float DesignRows
        {
            get { return designRows; }
        }

        private void OnEnable()
        {
            lastAspect = -1f;
            if (fitOnEnable)
            {
                Fit();
            }
        }

        private void Update()
        {
            if (!refitOnAspectChange)
            {
                return;
            }

            Camera cam = ResolveCamera();
            if (cam != null && !Mathf.Approximately(cam.aspect, lastAspect))
            {
                Fit();
            }
        }

        /// <summary>
        /// 供 Editor 工具调用：显式写入四条引用。
        /// 这样运行时不依赖名字查找，也不依赖 MainCamera tag。
        /// </summary>
        public void Configure(Camera cam, SpriteRenderer bg, Transform ground, BoxCollider2D box)
        {
            targetCamera = cam;
            background = bg;
            groundRoot = ground;
            groundCollider = box;
        }

        /// <summary>
        /// 立即按当前相机视口重算背景与地面几何。可在 Inspector 运行时从代码或按钮调用。
        /// 采用 cover 策略：等比缩放到「能盖住整个视口」的最小倍数，图片不变形、不留黑边，
        /// 超出屏幕的部分被自然裁掉（背景本身是横向延展的场景图，裁两侧不影响构图）。
        /// </summary>
        public void Fit()
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            lastAspect = cam.aspect;

            if (background == null)
            {
                background = FindBackground();
            }

            if (groundRoot == null)
            {
                groundRoot = FindGround();
            }

            if (groundCollider == null && groundRoot != null)
            {
                groundCollider = groundRoot.GetComponent<BoxCollider2D>();
            }

            // 真正的几何只实现一份（MapStageBuilder）：Editor 菜单、运行时自动对齐、本组件共用，
            // 避免「菜单算一套、进 Play 又算一套」。
            MapStage computed;
            if (!MapStageBuilder.TryApply(
                    cam, transform, background, groundRoot, groundCollider,
                    designColumns, designRows, groundRows, fallbackSpriteSize, out computed))
            {
                return;
            }

            stage = computed;
            LastFitScale = stage.WorldRect.width / Mathf.Max(0.0001f, SpriteWidth());
            LastGroundSurfaceY = stage.GroundSurfaceY;
        }

        private float SpriteWidth()
        {
            if (background != null && background.sprite != null)
            {
                return background.sprite.bounds.size.x;
            }

            return Mathf.Max(0.0001f, fallbackSpriteSize.x);
        }

        private Camera ResolveCamera()
        {
            return targetCamera != null ? targetCamera : Camera.main;
        }

        private SpriteRenderer FindBackground()
        {
            Transform child = FindDirectChild(BackgroundChildName);
            return child != null ? child.GetComponent<SpriteRenderer>() : null;
        }

        private Transform FindGround()
        {
            return FindDirectChild(GroundChildName);
        }

        private Transform FindDirectChild(string childName)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private void OnValidate()
        {
            // 纯手工搭的场景也能用：只补齐空引用，不创建任何物体/组件。
            if (background == null)
            {
                background = FindBackground();
            }

            if (groundRoot == null)
            {
                groundRoot = FindGround();
            }

            if (groundCollider == null && groundRoot != null)
            {
                groundCollider = groundRoot.GetComponent<BoxCollider2D>();
            }
        }
    }
}
