using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;
using UnityEngine;

namespace Ring.Flow
{
    /// <summary>
    /// 开局生成六个角色：一个玩家、五个人机。
    ///
    /// 顺序固定按角色总表来（总表里第几个人设就是第几个 id），玩家由 <see cref="playerPersona"/> 指定；
    /// 这样人机与玩家的 id 稳定，调试与存档都好对。
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

        [SerializeField]
        [Tooltip("六个角色的圆形站位半径（世界单位）。")]
        private float spawnRadius = 3.5f;

        [SerializeField]
        [Tooltip("是否给玩家角色挂上 PlayerMotor（用 GameInput 的移动轴）。")]
        private bool attachPlayerMotor = true;

        [SerializeField]
        [Tooltip("是否把生成结果打印到 Console。")]
        private bool logSpawn = true;

        private readonly List<CharacterInstance> instances = new List<CharacterInstance>(PersonaIds.Count);

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
                }
                Debug.LogWarning("[生成] 总表里没有 " + playerPersona.ToChineseName() + "，已把 "
                    + Player.DisplayName + " 设为玩家。", this);
            }

            if (instances.Count == 0)
            {
                Debug.LogError("[生成] 一个角色都没生成出来。", this);
                return false;
            }

            // 文档 [005]：开局随机分配特质、技能与位置。
            bell.Runtime.DealInitialCards();

            if (logSpawn)
            {
                Debug.Log("[生成] 已生成 " + instances.Count + " 个角色，玩家是 " + Player.DisplayName
                    + "（id " + playerId + "）。", this);
            }

            return true;
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
        }

        private CharacterInstance CreateInstance(int index, CharacterBaseStats stats, int total, bool isPlayer)
        {
            var go = new GameObject("角色_" + stats.DisplayName);
            go.transform.SetParent(transform, false);

            // 六边形站位：从正上方开始顺时针。
            float angle = 90f - 360f * index / Mathf.Max(1, total);
            float radians = angle * Mathf.Deg2Rad;
            go.transform.localPosition = new Vector3(
                Mathf.Cos(radians) * spawnRadius,
                Mathf.Sin(radians) * spawnRadius,
                0f);

            CharacterInstance instance = go.AddComponent<CharacterInstance>();
            instance.Initialize(index, stats, isPlayer);

            if (isPlayer && attachPlayerMotor)
            {
                go.AddComponent<PlayerMotor>();
            }

            return instance;
        }

        private void OnDestroy()
        {
            instances.Clear();
            Player = null;
        }
    }
}
