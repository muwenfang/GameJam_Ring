using System.Collections.Generic;
using Ring.Core;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 六人设基础数值的总表。运行时用它按 <see cref="PersonaId"/> 反查资产，
    /// 避免各处 Resources.Load / 硬编码引用。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterRoster", menuName = "钟声/角色总表", order = 1)]
    public sealed class CharacterRoster : ScriptableObject
    {
        [Tooltip("六个人设各一份，顺序不影响逻辑（查询按 PersonaId 走）。")]
        [SerializeField] private List<CharacterBaseStats> characters = new List<CharacterBaseStats>(PersonaIds.Count);

        /// <summary>全部已登记的角色数值（可能含空槽，读的时候要判空）。</summary>
        public IReadOnlyList<CharacterBaseStats> Characters => characters;

        /// <summary>登记数量（含空槽）。</summary>
        public int Count => characters.Count;

        /// <summary>按人设查数值，找不到返回 null。</summary>
        public CharacterBaseStats Get(PersonaId persona)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                CharacterBaseStats stats = characters[i];
                if (stats != null && stats.Persona == persona)
                {
                    return stats;
                }
            }

            return null;
        }

        /// <summary>按人设查数值。</summary>
        public bool TryGet(PersonaId persona, out CharacterBaseStats stats)
        {
            stats = Get(persona);
            return stats != null;
        }

        /// <summary>还没有登记的人设，编辑器工具用来提示补齐。</summary>
        public List<PersonaId> GetMissingPersonas()
        {
            var missing = new List<PersonaId>();
            IReadOnlyList<PersonaId> all = PersonaIds.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (Get(all[i]) == null)
                {
                    missing.Add(all[i]);
                }
            }

            return missing;
        }

        /// <summary>收集总表与其下所有资产的问题。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "角色总表";

            if (characters.Count != PersonaIds.Count)
            {
                report.Warn(context, "登记了 " + characters.Count + " 个角色，文档要求 6 个。");
            }

            var seen = new HashSet<PersonaId>();
            int nullSlots = 0;
            for (int i = 0; i < characters.Count; i++)
            {
                CharacterBaseStats stats = characters[i];
                if (stats == null)
                {
                    nullSlots++;
                    continue;
                }

                if (!seen.Add(stats.Persona))
                {
                    report.Error(context, "人设 " + stats.Persona.ToChineseName() + " 重复登记（" + stats.name + "）。");
                }

                stats.CollectIssues(report);
            }

            if (nullSlots > 0)
            {
                report.Error(context, "有 " + nullSlots + " 个空槽没有指派角色资产。");
            }

            List<PersonaId> missing = GetMissingPersonas();
            for (int i = 0; i < missing.Count; i++)
            {
                report.Error(context, "缺少 " + missing[i].ToChineseName() + " 的数值资产。");
            }
        }
    }
}
