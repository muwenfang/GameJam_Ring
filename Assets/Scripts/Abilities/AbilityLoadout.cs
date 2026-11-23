using System;
using System.Collections.Generic;
using Ring.Bells;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 一个角色身上挂着的「五个槽」：特质 / 普攻 / 本我 / 自我 / 超我。
    ///
    /// 关键点（对应需求「五个能力可能分散到不同角色身上」）：
    ///   · 每个槽里放的是一张 <see cref="AbilityCard"/>，卡自带你拿的是**谁**的哪个能力，
    ///     所以「剑客的特质 + 力士的普攻 + 钟表师的本我」这种混装是合法的；
    ///   · 一个角色通常只有一张特质，但钟声规则允许靠夺取（B6）囤到多张，
    ///     因此特质槽是「最多 6 张」的列表；四个技能槽各自最多 1 张；
    ///   · 同一张卡（同人设同槽）不会重复出现在同一个人身上。
    /// </summary>
    public sealed class AbilityLoadout
    {
        /// <summary>特质最多能囤几张（六个人设各一张，实际对局里远到不了）。</summary>
        public const int MaxTraits = 6;

        private readonly List<AbilityCard> cards = new List<AbilityCard>(8);

        /// <summary>当前持有的全部能力卡（特质在前，其余按加入顺序）。</summary>
        public IReadOnlyList<AbilityCard> Cards
        {
            get { return cards; }
        }

        public int Count
        {
            get { return cards.Count; }
        }

        public bool IsEmpty
        {
            get { return cards.Count == 0; }
        }

        public bool Has(AbilityCard card)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == card)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>该槽是否至少有一张卡。</summary>
        public bool Has(AbilitySlot slot)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Slot == slot)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>取某槽的卡；特质槽返回第一张。</summary>
        public bool TryGet(AbilitySlot slot, out AbilityCard card)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Slot == slot)
                {
                    card = cards[i];
                    return true;
                }
            }

            card = default;
            return false;
        }

        /// <summary>某槽的数值定义；没有该卡时返回 null。</summary>
        public AbilityDefinition GetDefinition(AbilitySlot slot)
        {
            AbilityCard card;
            return TryGet(slot, out card) ? card.Definition : null;
        }

        /// <summary>持有中的全部特质卡（按加入顺序）。</summary>
        public IReadOnlyList<AbilityCard> GetTraitCards()
        {
            var traits = new List<AbilityCard>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Slot == AbilitySlot.Trait)
                {
                    traits.Add(cards[i]);
                }
            }

            return traits;
        }

        /// <summary>
        /// 放入一张卡。非特质槽如果已被别的卡占着，会把它顶出来放进 <paramref name="displaced"/>，
        /// 让调用方决定是替换、退回牌池（B11）还是直接丢弃。
        /// </summary>
        public bool TryAdd(AbilityCard card, out AbilityCard displaced)
        {
            displaced = default;

            if (!card.IsValid)
            {
                return false;
            }

            if (Has(card))
            {
                return false;
            }

            if (card.Slot == AbilitySlot.Trait)
            {
                if (CountTraits() >= MaxTraits)
                {
                    return false;
                }

                cards.Add(card);
                return true;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Slot == card.Slot)
                {
                    displaced = cards[i];
                    cards[i] = card;
                    return true;
                }
            }

            cards.Add(card);
            return true;
        }

        /// <summary>移除指定卡。</summary>
        public bool Remove(AbilityCard card)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == card)
                {
                    cards.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>清空某个槽（特质槽会清掉全部特质）。返回移除的张数。</summary>
        public int RemoveSlot(AbilitySlot slot)
        {
            int removed = 0;
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                if (cards[i].Slot == slot)
                {
                    cards.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }

        public void Clear()
        {
            cards.Clear();
        }

        public void CopyFrom(AbilityLoadout other)
        {
            cards.Clear();
            if (other == null)
            {
                return;
            }

            cards.AddRange(other.cards);
        }

        /// <summary>
        /// 把钟声系统的持有卡同步进装备表：特质卡 → 特质槽，技能卡 → 本我/自我/超我，
        /// 普攻槽不受影响（普攻是生成角色时发的，不参与特质/技能钟声）。
        /// </summary>
        /// <returns>成功放入的张数。</returns>
        public int SyncFromBellCards(
            IReadOnlyList<TraitCard> traits,
            IReadOnlyList<SkillCard> skills,
            out List<AbilityCard> rejected)
        {
            rejected = new List<AbilityCard>();

            // 先摘掉旧的特质/技能，保留普攻。
            for (int i = cards.Count - 1; i >= 0; i--)
            {
                AbilitySlot slot = cards[i].Slot;
                if (slot == AbilitySlot.Trait || slot.IsSkill())
                {
                    cards.RemoveAt(i);
                }
            }

            int applied = 0;
            if (traits != null)
            {
                for (int i = 0; i < traits.Count; i++)
                {
                    AbilityCard card = AbilityCards.FromTrait(traits[i]);
                    if (TryAdd(card, out AbilityCard ignored))
                    {
                        applied++;
                    }
                    else
                    {
                        rejected.Add(card);
                    }
                }
            }

            if (skills != null)
            {
                for (int i = 0; i < skills.Count; i++)
                {
                    AbilityCard card = AbilityCards.FromSkill(skills[i]);
                    if (TryAdd(card, out AbilityCard displaced))
                    {
                        applied++;

                        // 只有「同一个技能槽里被顶出来的那张」才算被拒绝。
                        // TryAdd 在空槽时回填 default，而 default 的 (剑客, 特质) 在总表里是合法组合，
                        // 单看 IsValid 会把「没顶出任何东西」误判成有卡被顶出。
                        if (displaced.IsValid && displaced.Slot == card.Slot && displaced != card)
                        {
                            rejected.Add(displaced);
                        }
                    }
                    else
                    {
                        rejected.Add(card);
                    }
                }
            }

            return applied;
        }

        /// <summary>是否集齐某人设（默认只看特质 + 三个技能，与钟声 B13/B14 一致）。</summary>
        public bool HasCompleteSet(PersonaId persona, bool includeBasicAttack = false)
        {
            return AbilityCards.IsCompleteSet(cards, persona, includeBasicAttack);
        }

        public bool TryGetCompletePersona(out PersonaId persona, bool includeBasicAttack = false)
        {
            return AbilityCards.TryGetCompletePersona(cards, out persona, includeBasicAttack);
        }

        public string DescribeProgress(PersonaId persona, bool includeBasicAttack = false)
        {
            return AbilityCards.DescribeProgress(cards, persona, includeBasicAttack);
        }

        /// <summary>「特质:剑客 ｜ 普攻:力士 ｜ 本我:钟表师」，给 HUD 用。</summary>
        public string Describe()
        {
            if (cards.Count == 0)
            {
                return "（空）";
            }

            var parts = new List<string>(AbilitySlots.Count);
            for (int s = 0; s < AbilitySlots.Count; s++)
            {
                AbilitySlot slot = AbilitySlots.All[s];
                if (slot == AbilitySlot.Trait)
                {
                    IReadOnlyList<AbilityCard> traits = GetTraitCards();
                    if (traits.Count == 0)
                    {
                        continue;
                    }

                    var names = new List<string>(traits.Count);
                    for (int i = 0; i < traits.Count; i++)
                    {
                        names.Add(PersonaIds.ToChineseName(traits[i].Persona));
                    }

                    parts.Add(slot.ToDisplayName() + ":" + string.Join("+", names));
                    continue;
                }

                AbilityCard card;
                if (TryGet(slot, out card))
                {
                    parts.Add(slot.ToDisplayName() + ":" + PersonaIds.ToChineseName(card.Persona));
                }
            }

            return string.Join(" ｜ ", parts);
        }

        public int CountTraits()
        {
            int count = 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Slot == AbilitySlot.Trait)
                {
                    count++;
                }
            }

            return count;
        }

        public void CollectIssues(ValidationReport report, string context = null)
        {
            if (report == null)
            {
                return;
            }

            string where = string.IsNullOrEmpty(context) ? "能力槽" : context;

            if (CountTraits() > MaxTraits)
            {
                report.Error(where, "特质超过上限 " + MaxTraits + " 张");
            }

            var seenTraits = new HashSet<PersonaId>();
            var seenSlots = new Dictionary<AbilitySlot, PersonaId>();

            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCard card = cards[i];
                if (!card.IsValid)
                {
                    report.Error(where, "第 " + i + " 张卡不是合法的人设/槽位组合");
                    continue;
                }

                if (card.Definition == null)
                {
                    report.Error(where, card.ToDisplayName() + " 在技能总表里没有对应数值");
                }

                if (card.Slot == AbilitySlot.Trait)
                {
                    if (!seenTraits.Add(card.Persona))
                    {
                        report.Error(where, "重复持有 " + card.ToDisplayName());
                    }

                    continue;
                }

                PersonaId existing;
                if (seenSlots.TryGetValue(card.Slot, out existing))
                {
                    report.Error(
                        where,
                        card.Slot.ToDisplayName()
                        + " 槽同时被 "
                        + PersonaIds.ToChineseName(existing)
                        + " 和 "
                        + PersonaIds.ToChineseName(card.Persona)
                        + " 占用");
                }
                else
                {
                    seenSlots[card.Slot] = card.Persona;
                }
            }
        }

        public override string ToString()
        {
            return Describe();
        }
    }
}
