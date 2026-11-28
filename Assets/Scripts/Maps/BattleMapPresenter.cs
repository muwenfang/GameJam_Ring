using System;
using UnityEngine;

namespace Ring.Maps
{
    /// <summary>
    /// 把「本局随机到的地图」翻译成「显示哪一组场景物体」。挂在场景里（建议挂在 battleCanvas 上或同级空物体）。
    ///
    /// 六个槽位由 Inspector 拖入，代码不创建、不实例化任何地图物体 —— 槽位留空是合法状态，
    /// 运行时会打一条明确警告并优雅降级。MapKit 的六张地图资源（Assets/MapKit/Maps/01..06）
    /// 由用户在场景里搭成根节点、挂上 MapScene 后拖进这六个槽位。
    ///
    /// 为什么是 MapScene[] 而不是 Sprite[] / Image[]：见 MapScene 的说明，地图是一整棵场景层级。
    /// 本类只做「六选一 + 调用 Enter/Exit」，地形、碰撞、机制演出都在 MapScene 与 MapKit 脚本里，互不知晓。
    ///
    /// 2026-11-25 增补（本次）：
    /// - Awake 统一收起六张地图，避免场景里根节点存成「初始激活」时六张叠在一起；
    /// - 按 MapScene.MapId 校正槽位顺序（仅当六个编号恰好构成 0..5 的排列时），拖错顺序也能切对图；
    /// - 非选中地图在 Exit() 之后再兜底 SetActive(false)，防止自定义 MapScene.Exit() 忘了收起；
    /// - 暴露当前地图的地面顶面 Y，供后续「把角色放到地面上」的消费方查询。
    ///
    /// 边界：本类只读写地图根节点的激活状态与 Enter/Exit 钩子，
    /// 不触碰角色数值、输入、钟声、技能与胜负判定 —— 换地图不改变任何其他属性。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/地图/战斗地图展示器 BattleMapPresenter")]
    public sealed class BattleMapPresenter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("六个地图槽位，顺序对应 BattleMapId.Map01..Map06（即 MapKit 的 Z1..Z6）。根节点未搭时留空即可。")]
        private MapScene[] scenes = new MapScene[BattleMaps.Count];

        [SerializeField]
        [Tooltip("是否打印地图切换日志。")]
        private bool logMaps = true;

        private BattleMapId current;
        private bool shown;
        private MapScene currentScene;
        private bool scenesResolved;
        private BattleMapId missingWarnedFor = (BattleMapId)(-1);

        public BattleMapId Current
        {
            get { return current; }
        }

        /// <summary>当前展示的地图根节点；槽位为空或未开局时为 null。</summary>
        public MapScene CurrentScene
        {
            get { return currentScene; }
        }

        /// <summary>当前地图的中文名（如「Z1 雨夜竹林」），供 HUD / 日志使用。</summary>
        public string CurrentDisplayName
        {
            get { return current.ToDisplayName(); }
        }

        /// <summary>
        /// 当前地图的碰撞地面顶面（世界 Y）。没有接地面时返回 0，配合 <see cref="HasGroundSurface"/> 判空。
        /// 只做查询，不移动、不生成任何物体。
        /// </summary>
        public float CurrentGroundSurfaceY
        {
            get { return currentScene != null ? currentScene.GroundSurfaceY : 0f; }
        }

        /// <summary>当前地图是否已经接了碰撞地面。</summary>
        public bool HasGroundSurface
        {
            get { return currentScene != null && currentScene.HasGround; }
        }

        /// <summary>
        /// 地图切换完成、场地几何（25×12 格 + 地面顶面）已算好时触发。
        /// 与 <see cref="Ring.Flow.GameFlow.MapChanged"/> 的区别：那个是「数据层决定了哪张图」，
        /// 这个是「这张图已经在场景里激活并算完几何」，落位/相机可以直接消费。
        /// </summary>
        public event Action<BattleMapId> MapShown;

        /// <summary>当前地图的场地几何；没展示地图或没算出来时 IsValid = false。</summary>
        public MapStage CurrentStage
        {
            get { return currentScene != null ? currentScene.Stage : default(MapStage); }
        }

        /// <summary>取当前地图的场地几何（背景矩形 + 25×12 格 + 地面顶面）。</summary>
        public bool TryGetStage(out MapStage stage)
        {
            stage = CurrentStage;
            return stage.IsValid;
        }

        /// <summary>
        /// 当前地图机制的状态摘要；委托给 MapScene.StatusText()（反射读 MapKit 机制的 StatusText）。
        /// 没接地图、没拖机制组件时返回空串，调用方自行决定占位文案。
        /// </summary>
        public string StatusText()
        {
            return currentScene != null ? currentScene.StatusText() : string.Empty;
        }

        /// <summary>该编号的地图实体是否已经接进槽位。</summary>
        public bool HasScene(BattleMapId id)
        {
            if (!BattleMaps.IsDefined(id))
            {
                return false;
            }

            int index = BattleMaps.IndexOf(id);
            return scenes != null && index < scenes.Length && scenes[index] != null;
        }

        /// <summary>
        /// 开局前把所有已接地图根节点统一收起。用户不必记得把六个根节点都存成隐藏，
        /// Show() 只会激活本局选中的那一张。
        /// </summary>
        private void Awake()
        {
            ResolveScenesByMapId();
            CollapseAll();
        }

        /// <summary>
        /// 显示指定地图：其余五张退出，选中的进入。幂等 —— 同一局里阶段从 Loading 走到 Collect
        /// 会重复调用，不会反复触发 Enter。
        /// </summary>
        public void Show(BattleMapId id)
        {
            if (!BattleMaps.IsDefined(id))
            {
                Debug.LogWarning("[地图] 收到非法地图编号 " + id + "，忽略本次展示。", this);
                return;
            }

            ResolveScenesByMapId();

            if (shown && current == id)
            {
                return;
            }

            int targetIndex = BattleMaps.IndexOf(id);
            if (scenes != null)
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (i == targetIndex || scenes[i] == null)
                    {
                        continue;
                    }

                    scenes[i].Exit();
                    // 兜底：自定义 MapScene.Exit() 若被重写且忘了收起物体，这里保证它真的隐藏。
                    if (scenes[i].gameObject.activeSelf)
                    {
                        scenes[i].gameObject.SetActive(false);
                    }
                }
            }

            current = id;
            shown = true;

            MapScene target = scenes != null && targetIndex < scenes.Length ? scenes[targetIndex] : null;
            currentScene = target;
            if (target == null)
            {
                // 六个地图根节点还没在场景里搭好 / 拖进槽位：预期状态，每张图只提示一次，不阻断开局。
                if (missingWarnedFor != id)
                {
                    missingWarnedFor = id;
                    Debug.LogWarning(
                        "[地图] 本局选中 " + id.ToDisplayName() + "（槽位 " + targetIndex
                        + "），但该槽位还没有地图实体。请用菜单「钟声 / 地图 / 生成六张地图与碰撞地面」，"
                        + "或手工把 MapKit 对应 Z 区根节点挂上 MapScene 后拖进 BattleMapPresenter 的 Scenes。",
                        this);
                }

                return;
            }

            target.Enter();
            if (logMaps)
            {
                Debug.Log("[地图] 展示 " + id.ToDisplayName() + "：" + target.name
                    + "（资源 " + id.ToZoneFolder() + "）"
                    + (target.HasGround
                        ? "，地面顶面 Y=" + target.GroundSurfaceY.ToString("F2")
                        : "，未接碰撞地面")
                    + (target.HasStage ? "；" + target.Stage.Describe() : "；场地几何未算出"), this);
            }

            // 地图已激活且几何算完：落位 / 相机可以在这里消费场地，不必再猜顺序。
            if (MapShown != null)
            {
                MapShown(id);
            }
        }

        /// <summary>收起所有地图（回主菜单 / 结算隐藏战斗画布时）。</summary>
        public void Hide()
        {
            CollapseAll();

            shown = false;
            currentScene = null;
            if (logMaps)
            {
                Debug.Log("[地图] 已收起所有地图。", this);
            }
        }

        private void CollapseAll()
        {
            if (scenes == null)
            {
                return;
            }

            for (int i = 0; i < scenes.Length; i++)
            {
                MapScene scene = scenes[i];
                if (scene == null)
                {
                    continue;
                }

                scene.Exit();
                if (scene.gameObject.activeSelf)
                {
                    scene.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 槽位顺序防呆：若六个槽位的 MapScene.MapId 恰好是 0..5 的一个排列，就按 MapId 重排，
        /// 这样 Inspector 里拖错顺序也不会切错图。只要出现缺漏、重复或未区分编号（例如全留默认 Map01），
        /// 就保持原有的下标顺序不动，避免把用户手工摆好的顺序打乱。
        /// 只执行一次，不修改序列化数据（仅重排运行时数组）。
        /// </summary>
        private void ResolveScenesByMapId()
        {
            if (scenesResolved)
            {
                return;
            }

            scenesResolved = true;

            if (scenes == null || scenes.Length != BattleMaps.Count)
            {
                return;
            }

            var byId = new MapScene[BattleMaps.Count];
            var seen = new bool[BattleMaps.Count];
            bool valid = true;

            for (int i = 0; i < scenes.Length; i++)
            {
                MapScene scene = scenes[i];
                if (scene == null)
                {
                    valid = false;
                    break;
                }

                BattleMapId id = scene.MapId;
                if (!BattleMaps.IsDefined(id))
                {
                    valid = false;
                    break;
                }

                int index = BattleMaps.IndexOf(id);
                if (seen[index] || byId[index] != null)
                {
                    valid = false;
                    break;
                }

                seen[index] = true;
                byId[index] = scene;
            }

            if (!valid)
            {
                // 有空槽或编号未区分：保持下标顺序。只在日志开关打开时提示，不刷屏。
                if (logMaps)
                {
                    Debug.Log(
                        "[地图] 六个槽位的 Map Id 未构成 0..5 的唯一排列（有空槽、重复或全是默认值），"
                        + "按槽位下标匹配。请在每个 MapScene 上把 Map Id 设为对应的 Map01..Map06。", this);
                }

                return;
            }

            bool reordered = false;
            for (int i = 0; i < byId.Length; i++)
            {
                if (byId[i] != scenes[i])
                {
                    reordered = true;
                    break;
                }
            }

            if (!reordered)
            {
                return;
            }

            scenes = byId;
            if (logMaps)
            {
                Debug.Log("[地图] 已按各 MapScene 的 Map Id 校正六个槽位顺序。", this);
            }
        }

        private void OnValidate()
        {
            if (scenes == null || scenes.Length != BattleMaps.Count)
            {
                var resized = new MapScene[BattleMaps.Count];
                if (scenes != null)
                {
                    int copy = Mathf.Min(scenes.Length, resized.Length);
                    for (int i = 0; i < copy; i++)
                    {
                        resized[i] = scenes[i];
                    }
                }

                scenes = resized;
            }

            // Inspector 里改过槽位后，允许下次 Show 重新判定一次顺序。
            scenesResolved = false;
        }
    }
}
