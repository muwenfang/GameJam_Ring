using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ring.Maps
{
    /// <summary>
    /// 一张地图场景的根组件，挂在每张地图的根节点上，由 <see cref="BattleMapPresenter"/> 按 BattleMapId 选中。
    ///
    /// 刻意不是 Image / Sprite：地图不是一张图，而是一整棵可带脚本的 GameObject 层级
    /// （背景分层、地面与碰撞、出生点、灯光、粒子、动画，甚至叠加加载的独立 Scene）。
    /// 切换的是「整组场景物体」，不是换贴图。所以本类的最小约定只有：一个根节点、一个编号、
    /// 一对进入/退出钩子；地图内部怎么搭完全自由。
    ///
    /// 接入 MapKit 后的三条约定（都不需要代码创建物体）：
    /// - 六张地图的美术资源在 Assets/MapKit/Maps/01..06，机制脚本在 MapKit/Scripts/Mechanics；
    /// - 机制组件挂在本根节点或其子物体上，会随 Enter()/Exit() 的 SetActive 自动启停；
    ///   若想在 HUD 显示机制状态，把主机制组件拖进 Zone Mechanic 槽位，StatusText() 会读它；
    /// - 碰撞地面（Ground Root + Collider2D）同样挂成子物体，随地图一起显隐；本类只提供
    ///   <see cref="GroundSurfaceY"/> 只读查询，不移动角色、不创建碰撞体。
    ///
    /// 后续扩展点（重写即可，不用改 presenter）：
    /// - Enter()：初始化地形/碰撞、摆出生点、切相机边界、播放入场演出、SceneManager.LoadSceneAsync(Additive)
    /// - Exit()：回收动态生成物、卸载叠加场景、停演出
    /// - RegionAnchors：B8 六区域与位置钟声 BellResult.Swaps 的空间落点
    ///
    /// 非 abstract 是刻意的：地图根节点还没搭时，也能先挂一个空根物体占位、验证随机链路。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/地图/地图场景 MapScene")]
    public class MapScene : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("这张地图对应的编号；必须与 BattleMapPresenter 六个槽位的顺序一致。")]
        private BattleMapId mapId;

        [SerializeField]
        [Tooltip("可选：地图内 6 个区域锚点，供出生点与位置钟声消费。现在全留空即可。")]
        private Transform[] regionAnchors = new Transform[BattleMaps.Count];

        [SerializeField]
        [Tooltip("可选：这张地图的主机制组件（MapKit 的 XXXMechanic）。只用于 HUD 读取 StatusText()，不拖不影响地图显隐与机制运行。")]
        private MonoBehaviour zoneMechanic;

        [Header("碰撞地面（可选）")]
        [SerializeField]
        [Tooltip("本图地面根节点（含碰撞体与地面贴图）。作为本根节点的子物体，会随 Enter/Exit 的 SetActive 自动启停；代码不创建它。")]
        private Transform groundRoot;

        [SerializeField]
        [Tooltip("本图地面碰撞体（BoxCollider2D 等）。留空时自动从 Ground Root 上查找；没有地面时保持空。")]
        private Collider2D groundCollider;

        public BattleMapId MapId
        {
            get { return mapId; }
        }

        /// <summary>区域锚点（可能全部为 null，调用方必须判空）。</summary>
        public IReadOnlyList<Transform> RegionAnchors
        {
            get { return regionAnchors; }
        }

        /// <summary>本图在 MapKit 映射表里的中文名（如「Z1 雨夜竹林」）。</summary>
        public string DisplayName
        {
            get { return BattleMaps.ClampToValid(mapId).ToDisplayName(); }
        }

        /// <summary>本图资源目录名（如「01_BambooNight」）。</summary>
        public string ZoneFolder
        {
            get { return BattleMaps.ClampToValid(mapId).ToZoneFolder(); }
        }

        /// <summary>本图在设计场景里的区域根节点名（如「Z1_雨夜竹林」）。</summary>
        public string ZoneName
        {
            get { return BattleMaps.ClampToValid(mapId).ToZoneName(); }
        }

        /// <summary>按 B8 的区域下标 0..5 取锚点；越界或未配置返回 null。</summary>
        public Transform GetRegionAnchor(int index)
        {
            if (regionAnchors == null || index < 0 || index >= regionAnchors.Length)
            {
                return null;
            }

            return regionAnchors[index];
        }

        /// <summary>本图地面根节点；未配置返回 null。地面是本根节点的子物体，随 SetActive 自动启停。</summary>
        public Transform GroundRoot
        {
            get { return groundRoot; }
        }

        /// <summary>
        /// 本图地面碰撞体。Inspector 里留空时自动从 <see cref="GroundRoot"/> 上查找一次；
        /// 找不到返回 null。只读查询，不创建任何组件。
        /// </summary>
        public Collider2D GroundCollider
        {
            get
            {
                if (groundCollider == null && groundRoot != null)
                {
                    groundCollider = groundRoot.GetComponent<Collider2D>();
                }

                return groundCollider;
            }
        }

        /// <summary>本图是否已经接了碰撞地面。</summary>
        public bool HasGround
        {
            get { return GroundCollider != null; }
        }

        /// <summary>
        /// 地面顶面的世界 Y（角色脚底应该落在这里）。没有碰撞地面时返回 0，
        /// 调用方先用 <see cref="HasGround"/> 判空。
        ///
        /// 刻意不直接读 Collider2D.bounds：根节点在 Enter() 之前是隐藏的，非激活碰撞体的
        /// bounds 可能不是最新的；BoxCollider2D 走 offset + size + lossyScale 手算，
        /// 其余类型退回 bounds。
        /// </summary>
        public float GroundSurfaceY
        {
            get
            {
                Collider2D col = GroundCollider;
                if (col == null)
                {
                    return 0f;
                }

                var box = col as BoxCollider2D;
                if (box == null)
                {
                    return col.bounds.max.y;
                }

                Vector3 center = box.transform.TransformPoint(box.offset);
                float halfHeight = box.size.y * 0.5f * Mathf.Abs(box.transform.lossyScale.y);
                return center.y + halfHeight;
            }
        }

        /// <summary>
        /// 读取 MapKit 机制组件的运行状态摘要供 HUD 显示。
        /// 用反射调用 public string StatusText()：Ring.Maps 不需要引用 MapKitTest 程序集，
        /// MapKit 机制脚本也不必实现任何 Ring 接口，双向零耦合。没拖组件 / 方法不存在 / 调用抛错
        /// 一律返回空串，绝不影响地图显隐。
        /// </summary>
        public string StatusText()
        {
            if (zoneMechanic == null)
            {
                return string.Empty;
            }

            try
            {
                System.Reflection.MethodInfo method = zoneMechanic.GetType().GetMethod("StatusText", Type.EmptyTypes);
                if (method == null || method.ReturnType != typeof(string))
                {
                    return string.Empty;
                }

                return method.Invoke(zoneMechanic, null) as string ?? string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>进入这张地图。默认只是把整组物体激活；子类可重写做地形/演出/叠加场景。</summary>
        public virtual void Enter()
        {
            gameObject.SetActive(true);

            // 地面碰撞体被手工勾掉时在这里恢复，保证「按地图放角色」时地面一定存在。
            // 不创建组件，只改 enabled。
            Collider2D col = GroundCollider;
            if (col != null && !col.enabled)
            {
                col.enabled = true;
            }
        }

        /// <summary>退出这张地图。默认只是把整组物体收起；子类可重写做回收/卸载。</summary>
        public virtual void Exit()
        {
            gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            if (!BattleMaps.IsDefined(mapId))
            {
                mapId = BattleMapId.Map01;
            }

            if (regionAnchors == null || regionAnchors.Length != BattleMaps.Count)
            {
                var resized = new Transform[BattleMaps.Count];
                if (regionAnchors != null)
                {
                    int copy = Mathf.Min(regionAnchors.Length, resized.Length);
                    for (int i = 0; i < copy; i++)
                    {
                        resized[i] = regionAnchors[i];
                    }
                }

                regionAnchors = resized;
            }

            // 手工接线时只拖 Ground Root 也够用：碰撞体引用在这里自动补上（不创建任何物体/组件）。
            if (groundCollider == null && groundRoot != null)
            {
                groundCollider = groundRoot.GetComponent<Collider2D>();
            }
        }
    }
}
