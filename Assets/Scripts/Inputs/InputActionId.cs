using System.Collections.Generic;

namespace Ring.Inputs
{
    /// <summary>
    /// 游戏里可以被绑定的「动作」。
    /// 与《数据和结构.txt》的键位表一一对应，不包含 UI 导航（暂停菜单内部交互另算）。
    /// 顺序即序列化顺序，只在末尾追加。
    /// </summary>
    public enum InputActionId
    {
        /// <summary>向上移动。</summary>
        MoveUp = 0,

        /// <summary>向下移动。</summary>
        MoveDown = 1,

        /// <summary>向左移动。</summary>
        MoveLeft = 2,

        /// <summary>向右移动。</summary>
        MoveRight = 3,

        /// <summary>平A。</summary>
        Attack = 4,

        /// <summary>闪避。</summary>
        Dodge = 5,

        /// <summary>跳跃。</summary>
        Jump = 6,

        /// <summary>1 号技能（本我）。</summary>
        Skill1 = 7,

        /// <summary>2 号技能（自我）。</summary>
        Skill2 = 8,

        /// <summary>3 号技能（超我）。</summary>
        Skill3 = 9,

        /// <summary>暂停 / 菜单。</summary>
        Pause = 10,
    }

    /// <summary>动作的遍历、默认键位与显示名。</summary>
    public static class InputActions
    {
        /// <summary>动作总数。</summary>
        public const int Count = 11;

        private static readonly InputActionId[] AllActions =
        {
            InputActionId.MoveUp,
            InputActionId.MoveDown,
            InputActionId.MoveLeft,
            InputActionId.MoveRight,
            InputActionId.Attack,
            InputActionId.Dodge,
            InputActionId.Jump,
            InputActionId.Skill1,
            InputActionId.Skill2,
            InputActionId.Skill3,
            InputActionId.Pause,
        };

        private static readonly string[] DisplayNames =
        {
            "向上移动",
            "向下移动",
            "向左移动",
            "向右移动",
            "平A",
            "闪避",
            "跳跃",
            "技能 1（本我）",
            "技能 2（自我）",
            "技能 3（超我）",
            "暂停 / 菜单",
        };

        private static readonly InputKey[] DefaultPrimary =
        {
            InputKey.W,
            InputKey.S,
            InputKey.A,
            InputKey.D,
            InputKey.MouseLeft,   // 文档「鼠标左键/：平A/闪避」缺字，这里取平A=左键
            InputKey.MouseRight,  // 闪避=右键（见 designNotes）
            InputKey.Space,
            InputKey.Alpha1,
            InputKey.Alpha2,
            InputKey.Alpha3,
            InputKey.Escape,
        };

        private static readonly InputKey[] DefaultAlternate =
        {
            InputKey.UpArrow,
            InputKey.DownArrow,
            InputKey.LeftArrow,
            InputKey.RightArrow,
            InputKey.None,
            InputKey.None,
            InputKey.None,
            InputKey.None,
            InputKey.None,
            InputKey.None,
            InputKey.None,
        };

        /// <summary>按文档顺序排列的全部动作。</summary>
        public static IReadOnlyList<InputActionId> All => AllActions;

        /// <summary>枚举值是否为已定义的动作（挡掉强转进来的野值）。</summary>
        public static bool IsDefined(InputActionId action)
        {
            int index = (int)action;
            return index >= 0 && index < Count;
        }

        /// <summary>中文显示名。</summary>
        public static string ToDisplayName(this InputActionId action)
        {
            int index = (int)action;
            return index >= 0 && index < DisplayNames.Length ? DisplayNames[index] : action.ToString();
        }

        /// <summary>文档给出的默认主键位。</summary>
        public static InputKey GetDefaultPrimary(this InputActionId action)
        {
            int index = (int)action;
            return index >= 0 && index < DefaultPrimary.Length ? DefaultPrimary[index] : InputKey.None;
        }

        /// <summary>文档给出的默认备用键位（多为空，方向键作为 WASD 的备用）。</summary>
        public static InputKey GetDefaultAlternate(this InputActionId action)
        {
            int index = (int)action;
            return index >= 0 && index < DefaultAlternate.Length ? DefaultAlternate[index] : InputKey.None;
        }

        /// <summary>是否为移动类动作。</summary>
        public static bool IsMove(this InputActionId action)
        {
            return action == InputActionId.MoveUp || action == InputActionId.MoveDown
                   || action == InputActionId.MoveLeft || action == InputActionId.MoveRight;
        }

        /// <summary>是否为技能动作。</summary>
        public static bool IsSkill(this InputActionId action)
        {
            return SkillTierOf(action) > 0;
        }

        /// <summary>技能等级 1~3（对应本我/自我/超我）；非技能返回 0。</summary>
        public static int SkillTierOf(this InputActionId action)
        {
            switch (action)
            {
                case InputActionId.Skill1: return 1;
                case InputActionId.Skill2: return 2;
                case InputActionId.Skill3: return 3;
                default: return 0;
            }
        }

        /// <summary>技能等级（1~3）转动作，越界返回 null。</summary>
        public static InputActionId? SkillActionOf(int tier)
        {
            switch (tier)
            {
                case 1: return InputActionId.Skill1;
                case 2: return InputActionId.Skill2;
                case 3: return InputActionId.Skill3;
                default: return null;
            }
        }
    }
}
