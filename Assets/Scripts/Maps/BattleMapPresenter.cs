using UnityEngine;

namespace Ring.Maps
{
    /// <summary>
    /// 把「本局随机到的地图」翻译成「显示哪一组场景物体」。挂在场景里（建议挂在 battleCanvas 上或同级空物体）。
    ///
    /// 六个槽位由 Inspector 拖入，代码不创建、不实例化任何地图物体 —— 地图实体还没做，
    /// 槽位留空是合法状态，运行时会打一条明确警告并优雅降级。
    ///
    /// 为什么是 MapScene[] 而不是 Sprite[] / Image[]：见 MapScene 的说明，地图是一整棵场景层级。
    /// 本类只做「六选一 + 调用 Enter/Exit」，地形、碰撞、演出都在 MapScene 子类里，互不知晓。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/地图/战斗地图展示器 BattleMapPresenter")]
    public sealed class BattleMapPresenter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("六个地图槽位，顺序对应 BattleMapId.Map01..Map06。地图实体还没做时留空即可。")]
        private MapScene[] scenes = new MapScene[BattleMaps.Count];

        [SerializeField]
        [Tooltip("是否打印地图切换日志。")]
        private bool logMaps = true;

        private BattleMapId current;
        private bool shown;

        public BattleMapId Current
        {
            get { return current; }
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

            if (shown && current == id)
            {
                return;
            }

            int targetIndex = BattleMaps.IndexOf(id);
            if (scenes != null)
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (i != targetIndex && scenes[i] != null)
                    {
                        scenes[i].Exit();
                    }
                }
            }

            current = id;
            shown = true;

            MapScene target = scenes != null && targetIndex < scenes.Length ? scenes[targetIndex] : null;
            if (target == null)
            {
                // 六个地图实体尚未制作：这是预期状态，只提示一次，不阻断开局。
                Debug.LogWarning(
                    "[地图] 本局选中 " + id.ToDisplayName() + "（槽位 " + targetIndex
                    + "），但该槽位还没有地图实体。请把地图根节点挂上 MapScene 后拖进 BattleMapPresenter 的 Scenes。",
                    this);
                return;
            }

            target.Enter();
            if (logMaps)
            {
                Debug.Log("[地图] 展示 " + id.ToDisplayName() + "：" + target.name, this);
            }
        }

        /// <summary>收起所有地图（回主菜单 / 结算隐藏战斗画布时）。</summary>
        public void Hide()
        {
            if (scenes != null)
            {
                for (int i = 0; i < scenes.Length; i++)
                {
                    if (scenes[i] != null)
                    {
                        scenes[i].Exit();
                    }
                }
            }

            shown = false;
            if (logMaps)
            {
                Debug.Log("[地图] 已收起所有地图。", this);
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
        }
    }
}
