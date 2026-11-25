using System;

namespace Ring.Flow
{
    /// <summary>
    /// 一局游戏的流程状态机。纯 C#，不引用 UnityEngine，所以可以离线跑测试。
    ///
    /// 它只回答三件事：现在处于哪个状态、这一步能不能走、走完了该通知谁。
    /// 生成角色、切面板、敲钟这些副作用一律由 Unity 侧的 GameFlow 与 UI 负责，
    /// 因此流程规则改起来不会牵动场景。
    /// </summary>
    public sealed class MatchFlow
    {
        private MatchState state = MatchState.MainMenu;

        /// <summary>当前状态。</summary>
        public MatchState State
        {
            get { return state; }
        }

        /// <summary>上一个状态（主菜单时等于自己）。</summary>
        public MatchState PreviousState { get; private set; } = MatchState.MainMenu;

        /// <summary>玩家角色的 id，未开局或不在局中时为 -1。</summary>
        public int PlayerId { get; private set; } = -1;

        /// <summary>本局参与者数量（含玩家），未开局为 0。</summary>
        public int ParticipantCount { get; private set; }

        /// <summary>B14 的「必须集齐完整人设才判胜」。默认关闭，按需求「剩自己即胜利」。</summary>
        public bool RequireCompleteSetForVictory { get; set; }

        /// <summary>状态变化：(from, to)。</summary>
        public event Action<MatchState, MatchState> StateChanged;

        /// <summary>六个角色已生成完毕：(玩家 id, 参与者数量)。</summary>
        public event Action<int, int> ParticipantsSpawned;

        /// <summary>本局有了结果：胜利 / 失败。</summary>
        public event Action<MatchState> MatchFinished;

        /// <summary>被拒绝的流转，附带原因（接日志用）。</summary>
        public event Action<string> TransitionRejected;

        /// <summary>
        /// 显式初始化完成。与 Unity 侧 <c>GameFlow.InitializeGame()</c> 配套：
        /// 壳层销毁角色、清钟声名册，这里清状态机自身的本局数据；状态不变。
        /// </summary>
        public event Action GameInitialized;

        /// <summary>最近一次被拒绝的原因。</summary>
        public string LastRejection { get; private set; }

        /// <summary>
        /// 显式初始化：清空本局的玩家 id、参与人数与拒绝原因，**状态不变**。
        /// 幂等，可反复调用；「返回主菜单」「再来一局」都会先走一次，保证重开不带残留数据。
        /// 状态流转仍由 <see cref="ReturnToMenu"/> / <see cref="StartMatch"/> 负责。
        /// </summary>
        public void InitializeGame()
        {
            PlayerId = -1;
            ParticipantCount = 0;
            LastRejection = null;

            if (GameInitialized != null)
            {
                GameInitialized();
            }
        }

        /// <summary>主菜单 / 胜利 / 失败 → 准备中。</summary>
        public bool StartMatch()
        {
            if (!state.CanStartMatch())
            {
                return Reject("当前状态（" + state.ToDisplayName() + "）不能开始新的一局。");
            }

            PlayerId = -1;
            ParticipantCount = 0;
            return Transition(MatchState.Loading);
        }

        /// <summary>准备中 → 收集人设。六个实例都已注册进钟声系统后调用。</summary>
        public bool NotifyParticipantsSpawned(int playerId, int count)
        {
            if (state != MatchState.Loading)
            {
                return Reject("只有准备中才接受角色生成完成，当前是 " + state.ToDisplayName() + "。");
            }

            if (playerId < 0)
            {
                return Reject("玩家角色 id 非法：" + playerId + "。");
            }

            if (count < 1)
            {
                return Reject("参与者数量非法：" + count + "。");
            }

            PlayerId = playerId;
            ParticipantCount = count;

            if (!Transition(MatchState.Collect))
            {
                return false;
            }

            if (ParticipantsSpawned != null)
            {
                ParticipantsSpawned(playerId, count);
            }

            return true;
        }

        /// <summary>收集人设 → 淘汰赛（B9：有人集齐完整人设）。</summary>
        public bool NotifyEliminationStarted()
        {
            if (state != MatchState.Collect)
            {
                return Reject("只有收集人设阶段能进入淘汰赛，当前是 " + state.ToDisplayName() + "。");
            }

            return Transition(MatchState.Elimination);
        }

        /// <summary>玩家阵亡 → 失败（收集阶段或淘汰赛都算）。</summary>
        public bool NotifyPlayerDefeated(string reason = null)
        {
            if (!state.IsRunning() || state == MatchState.Loading)
            {
                return Reject("当前状态（" + state.ToDisplayName() + "）不结算失败。");
            }

            Finish(MatchState.Defeat, reason);
            return true;
        }

        /// <summary>
        /// 只剩玩家一人 → 胜利。若 <see cref="RequireCompleteSetForVictory"/> 打开，
        /// 玩家没集齐完整人设时不判胜（B14），返回 false 继续打。
        /// </summary>
        public bool NotifyPlayerLastStanding(bool playerHasCompleteSet)
        {
            if (!state.IsRunning() || state == MatchState.Loading)
            {
                return Reject("当前状态（" + state.ToDisplayName() + "）不结算胜利。");
            }

            if (RequireCompleteSetForVictory && !playerHasCompleteSet)
            {
                return Reject("B14：玩家还没集齐完整人设，不能判胜。");
            }

            Finish(MatchState.Victory, null);
            return true;
        }

        /// <summary>人机获胜（只剩一个非玩家存活者且已集齐）→ 玩家失败。</summary>
        public bool NotifyOpponentWon(int winnerId)
        {
            if (!state.IsRunning() || state == MatchState.Loading)
            {
                return Reject("当前状态（" + state.ToDisplayName() + "）不结算失败。");
            }

            Finish(MatchState.Defeat, "角色 " + winnerId + " 赢下了这一局。");
            return true;
        }

        /// <summary>任何状态 → 主菜单（清空本局数据）。</summary>
        public bool ReturnToMenu()
        {
            if (state == MatchState.MainMenu)
            {
                return Reject("已经在主菜单了。");
            }

            PlayerId = -1;
            ParticipantCount = 0;
            LastRejection = null;
            return Transition(MatchState.MainMenu);
        }

        /// <summary>外部只读判断：玩家是否还在局中。</summary>
        public bool IsPlayerInMatch
        {
            get { return state.IsRunning() && PlayerId >= 0; }
        }

        private void Finish(MatchState result, string reason)
        {
            if (!Transition(result))
            {
                return;
            }

            if (reason != null)
            {
                LastRejection = reason;
            }

            if (MatchFinished != null)
            {
                MatchFinished(result);
            }
        }

        private bool Transition(MatchState next)
        {
            if (!next.IsDefined())
            {
                return Reject("目标状态非法：" + next + "。");
            }

            if (next == state)
            {
                return Reject("已经在 " + state.ToDisplayName() + " 状态。");
            }

            MatchState from = state;
            PreviousState = from;
            state = next;

            if (StateChanged != null)
            {
                StateChanged(from, next);
            }

            return true;
        }

        private bool Reject(string reason)
        {
            LastRejection = reason;
            if (TransitionRejected != null)
            {
                TransitionRejected(reason);
            }

            return false;
        }
    }
}
