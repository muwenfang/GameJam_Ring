using System;
using System.Collections.Generic;
using Ring.Core;
using UnityEngine;

namespace Ring.Inputs
{
    /// <summary>
    /// <see cref="InputKey"/> 与 Unity <see cref="KeyCode"/> 的桥接。
    ///
    /// 这是 InputKeyCatalog 的 Unity 半边：单独成文件，是为了让纯 C# 的那一半
    /// （枚举、显示名、IsMouseButton）能脱离 UnityEngine 编译，供离线测试与存档读写使用。
    /// </summary>
    public static partial class InputKeyCatalog
    {
        private static Dictionary<KeyCode, InputKey> reverseMap;

        /// <summary>转换到 Unity 的 KeyCode；None 或无法识别时返回 <see cref="KeyCode.None"/>。</summary>
        public static KeyCode ToKeyCode(InputKey key)
        {
            if (key == InputKey.None)
            {
                return KeyCode.None;
            }

            switch (key)
            {
                case InputKey.MouseLeft: return KeyCode.Mouse0;
                case InputKey.MouseRight: return KeyCode.Mouse1;
                case InputKey.MouseMiddle: return KeyCode.Mouse2;
            }

            // 其余成员与 KeyCode 同名，直接按名字解析，避免维护对照表。
            if (Enum.TryParse(key.ToString(), out KeyCode code))
            {
                return code;
            }

            return KeyCode.None;
        }

        /// <summary>从 KeyCode 反查；不在白名单里时返回 None。</summary>
        public static InputKey FromKeyCode(KeyCode code)
        {
            if (reverseMap == null)
            {
                reverseMap = BuildReverseMap();
            }

            return reverseMap.TryGetValue(code, out InputKey key) ? key : InputKey.None;
        }

        /// <summary>校验：每个 InputKey 都必须能映射到真实的 KeyCode。</summary>
        public static void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            IReadOnlyList<InputKey> keys = All;
            for (int i = 0; i < keys.Count; i++)
            {
                if (ToKeyCode(keys[i]) == KeyCode.None)
                {
                    report.Error("按键表", keys[i] + " 无法映射到 Unity KeyCode，可能是 Unity 版本改名了。");
                }
            }
        }

        private static Dictionary<KeyCode, InputKey> BuildReverseMap()
        {
            var map = new Dictionary<KeyCode, InputKey>();
            IReadOnlyList<InputKey> keys = All;
            for (int i = 0; i < keys.Count; i++)
            {
                KeyCode code = ToKeyCode(keys[i]);
                if (code != KeyCode.None && !map.ContainsKey(code))
                {
                    map.Add(code, keys[i]);
                }
            }

            return map;
        }
    }
}
