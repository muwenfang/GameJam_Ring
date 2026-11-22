using System;
using System.Collections.Generic;

namespace Ring.Inputs
{
    /// <summary>
    /// 可绑定的按键集合。
    ///
    /// 为什么不直接用 <c>KeyCode</c>：
    /// 1. 序列化必须稳定，KeyCode 的数值是 Unity 内部编号，不保证长期不变；
    /// 2. 输入映射的读写逻辑想做成纯 C#（可离线跑测试，不依赖 UnityEngine）；
    /// 3. 只暴露游戏真用得上的键，Inspector 下拉框不会长到找不着。
    ///
    /// 成员名刻意与 KeyCode 的成员名一致（鼠标键除外，见 <see cref="InputKeyCatalog"/>），
    /// 这样映射表只需要三条例外，不必维护一张 70 行的手写对照表。
    /// 新增按键只能在末尾追加，不要插入或重排，否则已有资产里的整数值会错位。
    /// </summary>
    public enum InputKey
    {
        None = 0,

        // 字母
        A = 1, B, C, D, E, F, G, H, I, J, K, L, M,
        N, O, P, Q, R, S, T, U, V, W, X, Y, Z,

        // 数字（主键盘）
        Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,

        // 功能键
        Space, Escape, Return, Tab, Backspace, Delete, Insert,
        LeftShift, RightShift, LeftControl, RightControl, LeftAlt, RightAlt,
        UpArrow, DownArrow, LeftArrow, RightArrow,
        Home, End, PageUp, PageDown, CapsLock,

        // 鼠标
        MouseLeft, MouseRight, MouseMiddle,

        // F1~F12
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    }

    /// <summary>
    /// <see cref="InputKey"/> 的查询与显示名。
    ///
    /// 这一半是纯 C#：不引用 UnityEngine，离线测试与存档读写都能直接编译。
    /// 需要转换到 Unity <c>KeyCode</c> 的部分在 InputKeyCatalog.cs（另一半 partial）。
    /// </summary>
    public static partial class InputKeyCatalog
    {
        private static readonly InputKey[] AllKeys = BuildAllKeys();

        /// <summary>全部可绑定按键（不含 None）。</summary>
        public static IReadOnlyList<InputKey> All => AllKeys;

        /// <summary>是否鼠标键（决定走 GetMouseButton 还是 GetKey，日志里也会区分）。</summary>
        public static bool IsMouseButton(InputKey key)
        {
            return key == InputKey.MouseLeft || key == InputKey.MouseRight || key == InputKey.MouseMiddle;
        }

        /// <summary>显示名。鼠标键与几个符号键给中文/符号，其余直接用字母。</summary>
        public static string ToDisplayName(InputKey key)
        {
            switch (key)
            {
                case InputKey.None: return "未绑定";
                case InputKey.MouseLeft: return "鼠标左键";
                case InputKey.MouseRight: return "鼠标右键";
                case InputKey.MouseMiddle: return "鼠标中键";
                case InputKey.Space: return "空格";
                case InputKey.Escape: return "Esc";
                case InputKey.Return: return "回车";
                case InputKey.Tab: return "Tab";
                case InputKey.Backspace: return "退格";
                case InputKey.Delete: return "Delete";
                case InputKey.Insert: return "Insert";
                case InputKey.LeftShift: return "左 Shift";
                case InputKey.RightShift: return "右 Shift";
                case InputKey.LeftControl: return "左 Ctrl";
                case InputKey.RightControl: return "右 Ctrl";
                case InputKey.LeftAlt: return "左 Alt";
                case InputKey.RightAlt: return "右 Alt";
                case InputKey.UpArrow: return "↑";
                case InputKey.DownArrow: return "↓";
                case InputKey.LeftArrow: return "←";
                case InputKey.RightArrow: return "→";
                case InputKey.Home: return "Home";
                case InputKey.End: return "End";
                case InputKey.PageUp: return "PageUp";
                case InputKey.PageDown: return "PageDown";
                case InputKey.CapsLock: return "CapsLock";
            }

            string name = key.ToString();
            if (name.Length == 6 && name.StartsWith("Alpha", StringComparison.Ordinal))
            {
                return name.Substring(5);
            }

            return name;
        }

        private static InputKey[] BuildAllKeys()
        {
            var keys = new List<InputKey>();
            foreach (InputKey key in Enum.GetValues(typeof(InputKey)))
            {
                if (key != InputKey.None)
                {
                    keys.Add(key);
                }
            }

            return keys.ToArray();
        }
    }
}
