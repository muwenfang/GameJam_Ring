using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;
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

        [SerializeField]
        [Tooltip("是否把生成结果打印到 Console。")]
        private bool logSpawn = true;

        private readonly List<CharacterInstance> instances = new List<CharacterInstance>(PersonaIds.Count);
        private ArenaPlan currentPlan;

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
        /// 把一轮 <see cref="ArenaPlan"/> 落到世界坐标：每个角色站到本场区域中心的左右两侧。
        /// 复制体对决（B12，SecondId &lt; 0）暂时只摆本体，复制体由后续战斗层生成。
        /// 同时把玩家所在的擂台中心交给 <see cref="PlayerMotor"/>，避免它被圆形边界拉回原点。
        /// </summary>
        public void ApplyPlan(ArenaPlan plan)
        {
            currentPlan = plan;
            if (plan == null)
            {
                return;
            }

            for (int d = 0; d < plan.Duels.Count; d++)
            {
                ArenaDuel duel = plan.Duels[d];
                Vector3 center = GetRegionCenter(duel.Map);

                if (duel.SecondId >= 0)
                {
                    PlaceAt(duel.FirstId, center + Vector3.left * duelOffset);
                    PlaceAt(duel.SecondId, center + Vector3.right * duelOffset);
                }
                else
                {
                    // 落单者（复制体对决 / 轮空）：站在区域中心。
                    PlaceAt(duel.FirstId, center);
                }

                SetBodyArena(duel.FirstId, center);
                if (duel.SecondId >= 0)
                {
                    SetBodyArena(duel.SecondId, center);
                }
            }

            if (Player != null && plan.Contains(Player.Id))
            {
                Vector3 playerCenter = GetRegionCenter(plan.MapOf(Player.Id));
                PlayerMotor motor = Player.GetComponent<PlayerMotor>();
                if (motor != null)
                {
                    motor.SetArenaCenter(playerCenter);
                }
            }
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
                character.Body.ConfigureArena(new Vector2(center.x, center.y), true, 6.5f, 1f);
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
