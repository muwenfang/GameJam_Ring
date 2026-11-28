using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;
using Ring.Inputs;
using Ring.Maps;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 开局生成六个角色：一个玩家、五个人机；并按 <see cref="ArenaPlan"/> 把六人摆进三场 1v1。
    ///
    /// 顺序固定按角色总表来（总表里第几个人设就是第几个 id），玩家由 <see cref="playerPersona"/> 指定；
    /// 这样人机与玩家的 id 稳定，钟声配对、调试与存档都好对。
    ///
    /// 三场 1v1 的**规则**（抽哪三个区域、谁跟谁打）全在纯 C# 的 ArenaPlan；本类只负责
    /// 把 <see cref="ArenaPlan"/> 翻译成世界坐标落点，不改写任何映射。
    ///
    /// 落位来源（都不自动生成 UI）：
    /// - 优先读 Inspector 里拖进来的 6 个「区域落点」Transform（对应 BattleMapId.Map01..Map06）；
    /// - 没拖时用计算布局：六个区域按 2 行 3 列排开，每场双方站在区域中心左右各 duelOffset。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/角色生成 MatchSpawner")]
    public sealed class MatchSpawner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("六个人设的数值总表（Assets/Data/Characters/CharacterRoster.asset）。")]
        private CharacterRoster roster;

        [SerializeField]
        [Tooltip("玩家操控的人设；其余五个人设自动成为人机。")]
        private PersonaId playerPersona = PersonaId.Swordsman;

        [Header("生成站位（ApplyPlan 之前的初始站位）")]
        [SerializeField]
        [Tooltip("六个角色的圆形站位半径（世界单位）。")]
        private float spawnRadius = 3.5f;

        [Header("三场 1v1 落位")]
        [SerializeField]
        [Tooltip("可选：六个区域的落点 Transform，顺序对应 BattleMapId.Map01..Map06（= MapKit Z1..Z6）。留空则用计算布局。")]
        private Transform[] regionAnchors = new Transform[BattleMaps.Count];

        [SerializeField]
        [Tooltip("未拖区域落点时，六个计算区域之间的中心距（世界单位）。")]
        private float arenaSpacing = 14f;

        [SerializeField]
        [Tooltip("每场 1v1 中，双方相对区域中心的左右偏移（世界单位）。")]
        private float duelOffset = 2.2f;

        [SerializeField]
        [Tooltip("是否给玩家角色挂上 PlayerMotor（用 GameInput 的移动轴）与 AbilityCaster。")]
        private bool attachPlayerMotor = true;

        [Header("场地落位（单图切镜，25×12 坐标统一）")]
        [SerializeField]
        [Tooltip("战斗地图展示器；留空自动在场景里找。有它才能读到当前地图的场地矩形与 25×12 格换算。")]
        private BattleMapPresenter mapPresenter;

        [SerializeField]
        [Tooltip("开：只把「玩家所在那一场」摆到当前展示的地图里（切镜），其余场次停到场外。关：沿用旧的 2 行 3 列分擂台落位。")]
        private bool stageIntoCurrentMap = true;

        [SerializeField]
        [Tooltip("1v1 双方相距多少格（按 25×12 地图格算）。默认 6 格。")]
        private float duelSeparationCells = 6f;

        [SerializeField]
        [Tooltip("脚底离地面上沿的余量（世界单位），避免视觉上陷进地面。")]
        private float groundClearance = 0.02f;

        [SerializeField]
        [Tooltip("场外待机点相对地图右边界的间距（世界单位）。")]
        private float parkSpacing = 30f;

        [SerializeField]
        [Tooltip("把「一格 = 多少世界单位」同步给角色运动体 / 技能施放器 / 输入选点。关掉则保持各自 Inspector 里的值。")]
        private bool syncCellScale = true;

        [SerializeField]
        [Tooltip("是否把落位结果打印到 Console。")]
        private bool logStage = true;

        [SerializeField]
        [Tooltip("是否把生成结果打印到 Console。")]
        private bool logSpawn = true;

        private readonly List<CharacterInstance> instances = new List<CharacterInstance>(PersonaIds.Count);
        private ArenaPlan currentPlan;
        private bool subscribedToPresenter;

        private void Awake()
        {
            // 尽早订阅：GameFlow 在 Start 里就会开局，地图在 Loading→Collect 时展示。
            ResolvePresenter();
        }

        /// <summary>本局生成的角色（顺序 = 生成顺序）。</summary>
        public IReadOnlyList<CharacterInstance> Instances
        {
            get { return instances; }
        }

        public int SpawnedCount
        {
            get { return instances.Count; }
        }

        /// <summary>玩家角色；未开局时为 null。</summary>
        public CharacterInstance Player { get; private set; }

        /// <summary>当前这一轮的场地与对手安排；未开局 / 清场后为 null。</summary>
        public ArenaPlan CurrentPlan
        {
            get { return currentPlan; }
        }

        public CharacterRoster Roster
        {
            get { return roster; }
            set { roster = value; }
        }

        public PersonaId PlayerPersona
        {
            get { return playerPersona; }
            set { playerPersona = value; }
        }

        /// <summary>按总表生成六个角色并注册到钟声系统；成功时返回 true，playerId 为玩家 id。</summary>
        public bool SpawnMatch(out int playerId)
        {
            playerId = -1;

            if (roster == null)
            {
                Debug.LogError("[生成] 没有指定角色总表，无法开局。", this);
                return false;
            }

            BellSystem bell = BellSystem.Instance;
            if (bell == null || bell.Runtime == null)
            {
                Debug.LogError("[生成] 场景里没有 BellSystem，无法开局。", this);
                return false;
            }

            if (roster.Characters.Count < PersonaIds.Count)
            {
                Debug.LogWarning("[生成] 角色总表只有 " + roster.Characters.Count + " 个角色，不足 6 人。", this);
            }

            DespawnAll();
            bell.Runtime.Registry.Clear();
            bell.Runtime.ResetRound();

            int count = Mathf.Min(roster.Characters.Count, PersonaIds.Count);
            for (int i = 0; i < count; i++)
            {
                CharacterBaseStats stats = roster.Characters[i];
                if (stats == null)
                {
                    Debug.LogWarning("[生成] 角色总表第 " + i + " 个槽为空，跳过。", this);
                    continue;
                }

                CharacterInstance instance = CreateInstance(i, stats, count, stats.Persona == playerPersona);
                if (!bell.Register(instance.Participant))
                {
                    Debug.LogWarning("[生成] 注册 " + stats.DisplayName + " 到钟声系统失败。", this);
                }

                instances.Add(instance);
                if (instance.IsPlayer)
                {
                    playerId = instance.Id;
                    Player = instance;
                }
            }

            if (Player == null && instances.Count > 0)
            {
                // 指定的人设没在总表里：退化成第一个人当玩家，避免整局没人可控。
                Player = instances[0];
                playerId = Player.Id;
                Player.PromoteToPlayer();
                if (attachPlayerMotor && Player.GetComponent<PlayerMotor>() == null)
                {
                    Player.gameObject.AddComponent<PlayerMotor>();
                    Player.gameObject.AddComponent<AbilityCaster>();
                }
                Debug.LogWarning("[生成] 总表里没有 " + playerPersona.ToChineseName() + "，已把 "
                    + Player.DisplayName + " 设为玩家。", this);
            }

            if (instances.Count == 0)
            {
                Debug.LogError("[生成] 一个角色都没生成出来。", this);
                return false;
            }

            // 文档 [005]：开局随机分配特质、技能（位置 / 区域由 GameFlow 随后 ApplyPlan 决定）。
            bell.Runtime.DealInitialCards();
            RefreshLoadouts();

            if (logSpawn)
            {
                Debug.Log("[生成] 已生成 " + instances.Count + " 个角色，玩家是 " + Player.DisplayName
                    + "（id " + playerId + "）。", this);
            }

            return true;
        }

        /// <summary>
        /// 把一轮 <see cref="ArenaPlan"/> 落到世界坐标。
        ///
        /// 两种口径（Inspector 的 <c>stageIntoCurrentMap</c> 切换）：
        /// - **单图切镜（默认）**：只把玩家所在那一场摆到「当前展示的地图」里，
        ///   双方站在 25×12 格的第 25 格中轴左右各 3 格、脚底落在地面上沿；
        ///   另外两场停到地图右边界之外（相机锁在场地内，永远看不到）。
        /// - **旧分擂台**：沿用 2 行 3 列 / 六个区域锚点，每场各自一块场地（需要宽相机或多相机）。
        ///
        /// 地图还没展示时（<see cref="BattleMapPresenter.MapShown"/> 尚未触发）先保持生成时的六边形站位，
        /// 等地图激活、场地几何算完后再自动重摆——所以最终状态一定是「和当前地图对齐」的。
        /// </summary>
        public void ApplyPlan(ArenaPlan plan)
        {
            currentPlan = plan;
            if (plan == null)
            {
                return;
            }

            StagePlan(plan);
        }

        /// <summary>重摆当前这一轮（地图切换完成、视口变化后调用）。</summary>
        public void RestageCurrentPlan()
        {
            if (currentPlan != null)
            {
                StagePlan(currentPlan);
            }
        }

        private void StagePlan(ArenaPlan plan)
        {
            MapStage stage;
            bool hasStage = TryGetStage(out stage);

            int playerId = Player != null ? Player.Id : -1;
            int stagedIndex = -1;
            if (hasStage)
            {
                stagedIndex = playerId >= 0 ? plan.DuelIndexOf(playerId) : 0;
                if (stagedIndex < 0 && plan.Duels.Count > 0)
                {
                    stagedIndex = 0;
                }
            }
            else if (stageIntoCurrentMap && ResolvePresenter() != null)
            {
                // 展示器在、但地图还没激活（MapShown 未触发）：先保持生成时的六边形站位，
                // 等 MapShown 回来再摆。比按旧口径摆到 ±14 的镜头外更安全。
                if (logStage)
                {
                    Debug.Log("[落位] 等待当前地图场地就绪，先保持初始站位；地图展示后会自动重摆。", this);
                }

                return;
            }

            for (int d = 0; d < plan.Duels.Count; d++)
            {
                ArenaDuel duel = plan.Duels[d];
                if (duel == null)
                {
                    continue;
                }

                if (!hasStage)
                {
                    PlaceDuelLegacy(duel);
                }
                else if (d == stagedIndex)
                {
                    StageDuel(duel, stage, playerId);
                }
                else
                {
                    ParkDuel(duel, stage, d);
                }
            }

            if (hasStage && syncCellScale)
            {
                ApplyCellScale(stage);
            }

            if (hasStage && Player != null && plan.Contains(Player.Id))
            {
                PlayerMotor motor = Player.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    // 矩形模式下这个字段不参与约束（SolveMotion 读的是 arenaRectMin/Max），
                    // 这里只保持中心信息与场地一致，供 Inspector 自检与旧逻辑兜底。
                    motor.SetArenaCenter(stage.Center);
                }
            }

            if (logStage)
            {
                Debug.Log("[落位] " + (hasStage
                        ? "切镜：" + (stagedIndex >= 0 && stagedIndex < plan.Duels.Count
                            ? plan.Duels[stagedIndex].Map.ToDisplayName()
                            : "无") + " 摆玩家场次，其余停到场外；" + stage.Describe()
                        : "已关闭切镜落位（或没有 BattleMapPresenter），按旧分擂台落位")
                    + (hasStage && syncCellScale ? "；格距 " + stage.CellSize.ToString("F3") + " 已同步" : string.Empty), this);
            }
        }

        /// <summary>把玩家这一场的双方摆进当前地图：中轴左右各半距、脚底贴地面。</summary>
        private void StageDuel(ArenaDuel duel, MapStage stage, int playerId)
        {
            float centerCol = stage.Grid.SafeColumns * 0.5f;
            float halfCells = Mathf.Max(0.5f, duelSeparationCells) * 0.5f;

            Vector3 left = stage.GroundPoint(centerCol - halfCells);
            Vector3 right = stage.GroundPoint(centerCol + halfCells);
            left.y += groundClearance;
            right.y += groundClearance;

            // 玩家固定站左侧（若玩家是 SecondId 则站右侧），镜头里始终左守右攻。
            bool playerOnLeft = playerId < 0 || duel.FirstId == playerId;

            if (duel.SecondId >= 0)
            {
                PlaceAt(duel.FirstId, playerOnLeft ? left : right);
                PlaceAt(duel.SecondId, playerOnLeft ? right : left);
                ConfigureBody(duel.FirstId, stage);
                ConfigureBody(duel.SecondId, stage);
            }
            else
            {
                // 落单（复制体对决 / 轮空）：站中轴。
                PlaceAt(duel.FirstId, stage.GroundPoint(centerCol) + Vector3.up * groundClearance);
                ConfigureBody(duel.FirstId, stage);
            }
        }

        /// <summary>其余场次停到地图右边界之外：逻辑仍在跑，但相机锁在场地内看不到。</summary>
        private void ParkDuel(ArenaDuel duel, MapStage stage, int index)
        {
            float x = stage.WorldRect.xMax + parkSpacing * (index + 1);
            float y = stage.WorldRect.yMin + stage.WorldRect.height * 0.5f;
            var park = new Vector3(x, y, 0f);

            PlaceAt(duel.FirstId, park);
            if (duel.SecondId >= 0)
            {
                PlaceAt(duel.SecondId, park + Vector3.right * 0.6f);
            }

            // 半径压到 0.5，位置钟声 / 击退也不会把场外角色带回镜头。
            ConfigureBodyAt(duel.FirstId, park, 0.5f, stage.CellSize);
            if (duel.SecondId >= 0)
            {
                ConfigureBodyAt(duel.SecondId, park + Vector3.right * 0.6f, 0.5f, stage.CellSize);
            }
        }

        private void PlaceDuelLegacy(ArenaDuel duel)
        {
            Vector3 center = GetRegionCenter(duel.Map);

            if (duel.SecondId >= 0)
            {
                PlaceAt(duel.FirstId, center + Vector3.left * duelOffset);
                PlaceAt(duel.SecondId, center + Vector3.right * duelOffset);
            }
            else
            {
                PlaceAt(duel.FirstId, center);
            }

            SetBodyArena(duel.FirstId, center);
            if (duel.SecondId >= 0)
            {
                SetBodyArena(duel.SecondId, center);
            }
        }

        private void ConfigureBody(int participantId, MapStage stage)
        {
            CharacterInstance character = FindById(participantId);
            if (character == null || character.Body == null)
            {
                return;
            }

            // 横版地面口径：左右可走区间取地图矩形，竖向锁定在地面顶面（+ 视觉余量）。
            // 角色只能在地面左右移动，跳跃由 CombatBody 的 JumpOffset 视觉偏移表现。
            character.Body.ConfigureArenaRect(stage.PlayableRectMin, stage.PlayableRectMax, stage.CellSize,
                stage.GroundSurfaceY + groundClearance);

            if (syncCellScale)
            {
                AbilityCaster caster = character.GetComponent<AbilityCaster>();
                if (caster != null)
                {
                    caster.SetUnitsPerCell(stage.CellSize);
                }
            }
        }

        private void ConfigureBodyAt(int participantId, Vector3 center, float radius, float cellSize)
        {
            CharacterInstance character = FindById(participantId);
            if (character == null || character.Body == null)
            {
                return;
            }

            character.Body.ConfigureArenaRect(
                new Vector2(center.x - radius, center.y - radius),
                new Vector2(center.x + radius, center.y + radius), cellSize, center.y);
        }

        /// <summary>把格距同步给输入选点与所有角色（运动体 / 技能 / 玩家适配层）。</summary>
        private void ApplyCellScale(MapStage stage)
        {
            GameInput input = GameInput.Instance;
            if (input != null)
            {
                // 选点格与地图格必须同源：否则鼠标点第 12 格、技能却算到第 10 格。
                input.SetGrid(stage.Grid);
            }

            for (int i = 0; i < instances.Count; i++)
            {
                CharacterInstance character = instances[i];
                if (character == null)
                {
                    continue;
                }

                if (character.Body != null)
                {
                    character.Body.SetUnitsPerCell(stage.CellSize);
                }

                PlayerMotor motor = character.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    motor.SetUnitsPerCell(stage.CellSize);
                }

                AbilityCaster caster = character.GetComponent<AbilityCaster>();
                if (caster != null)
                {
                    caster.SetUnitsPerCell(stage.CellSize);
                }
            }
        }

        private bool TryGetStage(out MapStage stage)
        {
            BattleMapPresenter presenter = ResolvePresenter();
            if (stageIntoCurrentMap && presenter != null && presenter.TryGetStage(out stage))
            {
                return true;
            }

            stage = default(MapStage);
            return false;
        }

        private BattleMapPresenter ResolvePresenter()
        {
            if (mapPresenter == null)
            {
                mapPresenter = FindObjectOfType<BattleMapPresenter>();
            }

            if (mapPresenter != null && !subscribedToPresenter)
            {
                mapPresenter.MapShown += HandleMapShown;
                subscribedToPresenter = true;
            }

            return mapPresenter;
        }

        private void HandleMapShown(BattleMapId map)
        {
            // 地图刚激活、场地几何算完：把这一轮重新摆一次，保证角色站在当前这张图的地面上。
            RestageCurrentPlan();
        }

        /// <summary>销毁本局所有角色（回主菜单、重开时调用）。</summary>
        public void DespawnAll()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                {
                    CharacterInstance instance = instances[i];
                    BellSystem bell = BellSystem.Instance;
                    if (bell != null && bell.Runtime != null)
                    {
                        bell.Unregister(instance.Id);
                    }

                    instance.Despawn();
                }
            }

            instances.Clear();
            Player = null;
            currentPlan = null;
        }

        /// <summary>
        /// 每次钟声换完牌之后，把每个人手上新的特质与技能同步进五个能力槽。
        /// 拆卡混装时这样做是唯一正确的同步点：钟声只改 BellParticipant 的持有列表。
        /// </summary>
        public void RefreshLoadouts()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null)
                {
                    instances[i].RefreshLoadout();
                }
            }
        }

        /// <summary>按区域编号取世界落点；Inspector 没拖锚点时退化成 2 行 3 列的计算布局。</summary>
        private Vector3 GetRegionCenter(BattleMapId map)
        {
            int index = BattleMaps.IndexOf(BattleMaps.ClampToValid(map));
            if (regionAnchors != null && index < regionAnchors.Length && regionAnchors[index] != null)
            {
                return regionAnchors[index].position;
            }

            int col = index % 3;
            int row = index / 3;
            return new Vector3(
                (col - 1) * arenaSpacing,
                (0.5f - row) * arenaSpacing * 0.6f,
                0f);
        }

        private void PlaceAt(int participantId, Vector3 worldPosition)
        {
            CharacterInstance character = FindById(participantId);
            if (character != null)
            {
                character.transform.position = worldPosition;
            }
        }

        private void SetBodyArena(int participantId, Vector3 center)
        {
            CharacterInstance character = FindById(participantId);
            if (character != null && character.Body != null)
            {
                character.Body.ConfigureArenaRect(
                    new Vector2(center.x - 6.5f, center.y - 6.5f),
                    new Vector2(center.x + 6.5f, center.y + 6.5f), 1f, center.y);
            }
        }

        private CharacterInstance FindById(int participantId)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (instances[i] != null && instances[i].Id == participantId)
                {
                    return instances[i];
                }
            }

            return null;
        }

        private CharacterInstance CreateInstance(int index, CharacterBaseStats stats, int total, bool isPlayer)
        {
            var go = new GameObject("角色_" + stats.DisplayName);
            go.transform.SetParent(transform, false);

            // 六边形站位：从正上方开始顺时针。ApplyPlan 随后会按三场 1v1 重新落位。
            float angle = 90f - 360f * index / Mathf.Max(1, total);
            float radians = angle * Mathf.Deg2Rad;
            go.transform.localPosition = new Vector3(
                Mathf.Cos(radians) * spawnRadius,
                Mathf.Sin(radians) * spawnRadius,
                0f);

            CharacterInstance instance = go.AddComponent<CharacterInstance>();
            instance.Initialize(index, stats, isPlayer);

            // 玩家和 AI 共用同一个运动体：AI 后续接决策时只需要写移动输入。
            go.AddComponent<CombatBody>();

            if (isPlayer && attachPlayerMotor)
            {
                go.AddComponent<PlayerMotor>();
                go.AddComponent<AbilityCaster>();
            }

            return instance;
        }

        private void OnDestroy()
        {
            if (mapPresenter != null && subscribedToPresenter)
            {
                mapPresenter.MapShown -= HandleMapShown;
            }

            subscribedToPresenter = false;
            instances.Clear();
            Player = null;
            currentPlan = null;
        }

        private void OnValidate()
        {
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
