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
    /// 后续做地图实体时的扩展点（重写即可，不用改 presenter）：
    /// - Enter()：初始化地形/碰撞、摆出生点、切相机边界、播放入场演出、SceneManager.LoadSceneAsync(Additive)
    /// - Exit()：回收动态生成物、卸载叠加场景、停演出
    /// - RegionAnchors：B8 六区域与位置钟声 BellResult.Swaps 的空间落点
    ///
    /// 非 abstract 是刻意的：地图实体还没做时，也能先挂一个空根物体占位、验证随机链路。
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

        public BattleMapId MapId
        {
            get { return mapId; }
        }

        /// <summary>区域锚点（可能全部为 null，调用方必须判空）。</summary>
        public IReadOnlyList<Transform> RegionAnchors
        {
            get { return regionAnchors; }
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

        /// <summary>进入这张地图。默认只是把整组物体激活；子类可重写做地形/演出/叠加场景。</summary>
        public virtual void Enter()
        {
            gameObject.SetActive(true);
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
        }
    }
}
