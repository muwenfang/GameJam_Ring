using System;
using System.Collections.Generic;

namespace Ring.Flow
{
    /// <summary>
    /// 一局游戏的宏观状态。UI 只认这个枚举，不关心钟声内部细节。
    ///
    /// 流转：主菜单 → 准备中 → 收集人设 → 淘汰赛 → 胜利 / 失败 → 主菜单
    /// 「准备中」是生成六个角色的那几帧，避免 UI 半截刷新。
    /// </summary>
    public enum MatchState
    {
        MainMenu = 0,
        Loading = 1,
        Collect = 2,
        Elimination = 3,
        Victory = 4,
        Defeat = 5,
    }

    /// <summary>状态枚举的文案与判定。纯 C#，UI 与日志共用。</summary>
    public static class MatchStates
    {
        public const int Count = 6;

        public static readonly IReadOnlyList<MatchState> All = new[]
        {
            MatchState.MainMenu,
            MatchState.Loading,
            MatchState.Collect,
            MatchState.Elimination,
            MatchState.Victory,
            MatchState.Defeat,
        };

        /// <summary>正在进行的一局（准备/收集/淘汰）。</summary>
        public static bool IsRunning(this MatchState state)
        {
            return state == MatchState.Loading || state == MatchState.Collect || state == MatchState.Elimination;
        }

        /// <summary>已经有结果了。</summary>
        public static bool IsFinished(this MatchState state)
        {
            return state == MatchState.Victory || state == MatchState.Defeat;
        }

        /// <summary>可以从这个状态点「开始游戏」。</summary>
        public static bool CanStartMatch(this MatchState state)
        {
            return state == MatchState.MainMenu || state.IsFinished();
        }

        public static string ToDisplayName(this MatchState state)
        {
            switch (state)
            {
                case MatchState.MainMenu: return "主菜单";
                case MatchState.Loading: return "准备中";
                case MatchState.Collect: return "收集人设";
                case MatchState.Elimination: return "淘汰赛";
                case MatchState.Victory: return "胜利";
                case MatchState.Defeat: return "失败";
                default: return "未知";
            }
        }

        /// <summary>HUD 副标题：一句话说明当前该干什么。</summary>
        public static string ToHint(this MatchState state)
        {
            switch (state)
            {
                case MatchState.MainMenu: return "点击开始游戏，进入钟声。";
                case MatchState.Loading: return "正在生成六位角色…";
                case MatchState.Collect: return "收集特质与三个等级的技能，凑齐一套人设。";
                case MatchState.Elimination: return "淘汰赛：倒下者离场，活到最后的人获胜。";
                case MatchState.Victory: return "你集齐人设并活了下来。";
                case MatchState.Defeat: return "你倒下了，钟声继续。";
                default: return string.Empty;
            }
        }

        public static bool IsDefined(this MatchState state)
        {
            return (int)state >= 0 && (int)state < Count;
        }

        /// <summary>给校验/日志用：所有能走到「结束」的状态。</summary>
        public static bool IsTerminal(this MatchState state)
        {
            return state.IsFinished() || state == MatchState.MainMenu;
        }
    }
}
