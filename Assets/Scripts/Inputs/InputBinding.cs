using System;
using System.Collections.Generic;
using Ring.Core;

namespace Ring.Inputs
{
    /// <summary>
    /// 一条可序列化的绑定记录：动作 → 主键 + 备用键。
    /// 用 struct 是为了让 ScriptableObject 的列表在 Inspector 里直接可编辑。
    /// </summary>
    [Serializable]
    public struct InputBindingEntry
    {
        public InputActionId Action;
        public InputKey Primary;
        public InputKey Alternate;

        public InputBindingEntry(InputActionId action, InputKey primary, InputKey alternate)
        {
            Action = action;
            Primary = primary;
            Alternate = alternate;
        }

        /// <summary>调试用描述，例如「平A：鼠标左键」。</summary>
        public string Describe()
        {
            string text = Action.ToDisplayName() + "：" + InputKeyCatalog.ToDisplayName(Primary);
            if (Alternate != InputKey.None)
            {
                text += " / " + InputKeyCatalog.ToDisplayName(Alternate);
            }

            return text;
        }
    }

    /// <summary>
    /// 一套完整的按键映射。纯 C#，不引用 UnityEngine，因此可以离线读写与测试。
    /// 运行时由 <see cref="GameInput"/> 持有并逐帧轮询。
    /// </summary>
    public sealed class InputBindingTable
    {
        /// <summary>每个动作的键位槽数：主键 + 备用键。</summary>
        public const int SlotsPerAction = 2;

        private readonly InputKey[] primary;
        private readonly InputKey[] alternate;

        /// <summary>创建一份「文档默认键位」。</summary>
        public InputBindingTable()
        {
            primary = new InputKey[InputActions.Count];
            alternate = new InputKey[InputActions.Count];
            ResetToDefaults();
        }

        /// <summary>动作数量。</summary>
        public int ActionCount => primary.Length;

        /// <summary>主键。</summary>
        public InputKey GetPrimary(InputActionId action)
        {
            return IsValid(action) ? primary[(int)action] : InputKey.None;
        }

        /// <summary>备用键。</summary>
        public InputKey GetAlternate(InputActionId action)
        {
            return IsValid(action) ? alternate[(int)action] : InputKey.None;
        }

        /// <summary>取第 slot 个键（0=主键，1=备用键）。</summary>
        public InputKey GetKey(InputActionId action, int slot)
        {
            if (!IsValid(action))
            {
                return InputKey.None;
            }

            return slot == 1 ? alternate[(int)action] : primary[(int)action];
        }

        /// <summary>该动作是否至少绑了一个键。</summary>
        public bool IsBound(InputActionId action)
        {
            return GetPrimary(action) != InputKey.None || GetAlternate(action) != InputKey.None;
        }

        /// <summary>直接设置主键与备用键（不做冲突检查）。</summary>
        public void Set(InputActionId action, InputKey primaryKey, InputKey alternateKey)
        {
            if (!IsValid(action))
            {
                return;
            }

            primary[(int)action] = primaryKey;
            alternate[(int)action] = alternateKey;
        }

        /// <summary>
        /// 设置某动作的第 slot 个键。若该键已被别的动作占用，返回 false 并给出占用者，
        /// 由调用方决定是覆盖还是让玩家换个键。
        /// </summary>
        public bool TrySetKey(InputActionId action, int slot, InputKey key, out InputActionId conflict)
        {
            conflict = default;
            if (!IsValid(action))
            {
                return false;
            }

            if (key != InputKey.None && TryFindOwner(key, out InputActionId owner, action))
            {
                conflict = owner;
                return false;
            }

            if (slot == 1)
            {
                alternate[(int)action] = key;
            }
            else
            {
                primary[(int)action] = key;
            }

            return true;
        }

        /// <summary>强制设置某动作的第 slot 个键，不做冲突检查。</summary>
        public void SetKey(InputActionId action, int slot, InputKey key)
        {
            if (!IsValid(action))
            {
                return;
            }

            if (slot == 1)
            {
                alternate[(int)action] = key;
            }
            else
            {
                primary[(int)action] = key;
            }
        }

        /// <summary>清空某动作的两个键。</summary>
        public void Clear(InputActionId action)
        {
            Set(action, InputKey.None, InputKey.None);
        }

        /// <summary>查询某个键被哪个动作占用。</summary>
        public bool TryFindOwner(InputKey key, out InputActionId owner, InputActionId except = (InputActionId)(-1))
        {
            owner = default;
            if (key == InputKey.None)
            {
                return false;
            }

            for (int i = 0; i < primary.Length; i++)
            {
                var action = (InputActionId)i;
                if (action == except)
                {
                    continue;
                }

                if (primary[i] == key || alternate[i] == key)
                {
                    owner = action;
                    return true;
                }
            }

            return false;
        }

        /// <summary>某个动作绑定的键（跳过 None）。</summary>
        public IEnumerable<InputKey> EnumerateKeys(InputActionId action)
        {
            InputKey first = GetPrimary(action);
            if (first != InputKey.None)
            {
                yield return first;
            }

            InputKey second = GetAlternate(action);
            if (second != InputKey.None && second != first)
            {
                yield return second;
            }
        }

        /// <summary>恢复成文档默认键位。</summary>
        public void ResetToDefaults()
        {
            IReadOnlyList<InputActionId> all = InputActions.All;
            for (int i = 0; i < all.Count; i++)
            {
                InputActionId action = all[i];
                primary[(int)action] = action.GetDefaultPrimary();
                alternate[(int)action] = action.GetDefaultAlternate();
            }
        }

        /// <summary>深拷贝，便于「编辑后取消」。</summary>
        public InputBindingTable Clone()
        {
            var copy = new InputBindingTable();
            Array.Copy(primary, copy.primary, primary.Length);
            Array.Copy(alternate, copy.alternate, alternate.Length);
            return copy;
        }

        /// <summary>导出为可序列化记录，顺序按文档动作顺序。</summary>
        public InputBindingEntry[] ToEntries()
        {
            IReadOnlyList<InputActionId> all = InputActions.All;
            var entries = new InputBindingEntry[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                InputActionId action = all[i];
                entries[i] = new InputBindingEntry(action, primary[(int)action], alternate[(int)action]);
            }

            return entries;
        }

        /// <summary>从记录导入；未出现的动作保持当前值。</summary>
        public void ApplyEntries(IEnumerable<InputBindingEntry> entries)
        {
            if (entries == null)
            {
                return;
            }

            foreach (InputBindingEntry entry in entries)
            {
                Set(entry.Action, entry.Primary, entry.Alternate);
            }
        }

        /// <summary>收集映射表的问题：未绑定、重复占用、主备键相同。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "输入映射";

            IReadOnlyList<InputActionId> all = InputActions.All;
            var used = new Dictionary<InputKey, InputActionId>();

            for (int i = 0; i < all.Count; i++)
            {
                InputActionId action = all[i];
                if (!IsBound(action))
                {
                    report.Error(context, action.ToDisplayName() + " 没有绑定任何按键。");
                }

                if (primary[(int)action] != InputKey.None && primary[(int)action] == alternate[(int)action])
                {
                    report.Warn(context, action.ToDisplayName() + " 的主键和备用键相同。");
                }

                foreach (InputKey key in EnumerateKeys(action))
                {
                    if (used.TryGetValue(key, out InputActionId other))
                    {
                        report.Error(context, "按键 " + InputKeyCatalog.ToDisplayName(key) + " 同时绑给了 "
                                              + other.ToDisplayName() + " 和 " + action.ToDisplayName() + "。");
                        continue;
                    }

                    used.Add(key, action);
                }
            }
        }

        /// <summary>多行调试文本。</summary>
        public string Describe()
        {
            var builder = new System.Text.StringBuilder();
            IReadOnlyList<InputActionId> all = InputActions.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append('\n');
                }

                var entry = new InputBindingEntry(all[i], GetPrimary(all[i]), GetAlternate(all[i]));
                builder.Append(entry.Describe());
            }

            return builder.ToString();
        }

        private bool IsValid(InputActionId action)
        {
            int index = (int)action;
            return index >= 0 && index < primary.Length;
        }
    }
}
