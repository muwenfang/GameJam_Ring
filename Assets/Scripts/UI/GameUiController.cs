using System.Text;
using Ring.Bells;
using Ring.Flow;
using UnityEngine;
using UnityEngine.UI;

namespace Ring.UI
{
    /// <summary>
    /// 界面层：只订阅 <see cref="GameFlow"/> 的状态事件，不碰钟声规则、不碰角色数值。
    ///
    /// 四个界面：
    /// - 主菜单（开始游戏）
    /// - 收集人设 HUD（阶段、下一次钟声倒计时、自己的牌与人）
    /// - 淘汰赛 HUD（同一个 HUD，只换提示文案）
    /// - 结算（胜利 / 失败 + 返回主菜单）
    ///
    /// 面板全部由 UiBuilder 在运行时搭建，场景里挂上本组件就能看到界面。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/界面 GameUiController")]
    public sealed class GameUiController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("游戏流程控制器；留空时自动取 GameFlow.Instance。")]
        private GameFlow gameFlow;

        [SerializeField]
        [Tooltip("是否在运行时自动搭建 Canvas（关掉表示由你自己准备界面）。")]
        private bool autoBuildUi = true;

        [SerializeField]
        [Tooltip("是否打印界面切换日志。")]
        private bool logUi;

        private GameObject mainMenuRoot;
        private GameObject hudRoot;
        private GameObject resultRoot;

        private Text stateLabel;
        private Text timerLabel;
        private Text playerLabel;
        private Text rosterLabel;
        private Text resultTitle;
        private Text resultReason;

        private float refreshTimer;

        private void Start()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            if (gameFlow == null)
            {
                Debug.LogWarning("[界面] 场景里没有 GameFlow，界面不会响应任何操作。", this);
            }

            if (autoBuildUi)
            {
                BuildUi();
            }

            if (gameFlow != null)
            {
                gameFlow.StateChanged += HandleStateChanged;
                gameFlow.MatchFinished += HandleMatchFinished;
                ApplyState(gameFlow.State);
            }
        }

        private void OnDestroy()
        {
            if (gameFlow != null)
            {
                gameFlow.StateChanged -= HandleStateChanged;
                gameFlow.MatchFinished -= HandleMatchFinished;
            }
        }

        private void Update()
        {
            if (gameFlow == null)
            {
                return;
            }

            MatchState state = gameFlow.State;
            if (state != MatchState.Collect && state != MatchState.Elimination)
            {
                return;
            }

            refreshTimer -= Time.deltaTime;
            if (refreshTimer <= 0f)
            {
                refreshTimer = 0.1f;
                RefreshHud();
            }
        }

        // ---------- 搭界面 ----------

        private void BuildUi()
        {
            Canvas canvas = UiBuilder.CreateCanvas("RingCanvas");
            mainMenuRoot = BuildMainMenu(canvas.transform);
            hudRoot = BuildHud(canvas.transform);
            resultRoot = BuildResult(canvas.transform);

            mainMenuRoot.SetActive(true);
            hudRoot.SetActive(false);
            resultRoot.SetActive(false);
        }

        private GameObject BuildMainMenu(Transform parent)
        {
            RectTransform panel = UiBuilder.CreateFullScreenPanel(parent, "MainMenu", UiBuilder.PanelColor);

            UiBuilder.CreateText(
                panel, "Title", "钟  声", 120, UiBuilder.AccentColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 260f), new Vector2(1400f, 180f));

            UiBuilder.CreateText(
                panel, "Subtitle", "六人一局：收集人设 → 淘汰赛 → 仅剩一人", 40, new Color(0.85f, 0.86f, 0.9f),
                TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 80f));

            UiBuilder.CreateButton(
                panel, "开始游戏", new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(480f, 110f),
                HandleStartClicked, 44);

            UiBuilder.CreateText(
                panel, "Hint",
                "移动：W A S D / 方向键\n敲钟：B（调试）　处决自己：K　处决人机：L（演示用）",
                28, new Color(0.72f, 0.74f, 0.8f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(1400f, 140f));

            return panel.gameObject;
        }

        private GameObject BuildHud(Transform parent)
        {
            RectTransform panel = UiBuilder.CreateFullScreenPanel(parent, "Hud", new Color(0f, 0f, 0f, 0.18f));
            panel.GetComponent<Image>().raycastTarget = false;

            stateLabel = UiBuilder.CreateText(
                panel, "State", string.Empty, 34, Color.white, TextAnchor.UpperLeft,
                new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(900f, 160f));
            UiBuilder.Place((RectTransform)stateLabel.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(32f, -24f), new Vector2(900f, 160f));
            stateLabel.alignment = TextAnchor.UpperLeft;

            timerLabel = UiBuilder.CreateText(
                panel, "Timer", string.Empty, 44, UiBuilder.AccentColor, TextAnchor.UpperCenter,
                new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(900f, 80f));
            UiBuilder.Place((RectTransform)timerLabel.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(900f, 80f));

            playerLabel = UiBuilder.CreateText(
                panel, "Player", string.Empty, 30, Color.white, TextAnchor.LowerLeft,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1100f, 220f));
            UiBuilder.Place((RectTransform)playerLabel.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(32f, 28f), new Vector2(1100f, 220f));

            rosterLabel = UiBuilder.CreateText(
                panel, "Roster", string.Empty, 26, new Color(0.86f, 0.88f, 0.92f), TextAnchor.LowerRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(720f, 320f));
            UiBuilder.Place((RectTransform)rosterLabel.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-32f, 28f), new Vector2(720f, 320f));

            return panel.gameObject;
        }

        private GameObject BuildResult(Transform parent)
        {
            RectTransform panel = UiBuilder.CreateFullScreenPanel(parent, "Result", new Color(0f, 0f, 0f, 0.78f));

            resultTitle = UiBuilder.CreateText(
                panel, "ResultTitle", string.Empty, 120, UiBuilder.AccentColor, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 200f), new Vector2(1400f, 180f));

            resultReason = UiBuilder.CreateText(
                panel, "ResultReason", string.Empty, 34, new Color(0.85f, 0.86f, 0.9f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(1400f, 140f));

            UiBuilder.CreateButton(
                panel, "再来一局", new Vector2(0.5f, 0.5f), new Vector2(-260f, -80f), new Vector2(420f, 100f),
                HandleRestartClicked, 40);

            UiBuilder.CreateButton(
                panel, "返回主菜单", new Vector2(0.5f, 0.5f), new Vector2(260f, -80f), new Vector2(420f, 100f),
                HandleReturnToMenuClicked, 40);

            return panel.gameObject;
        }

        // ---------- 按钮 ----------

        private void HandleStartClicked()
        {
            if (gameFlow == null)
            {
                return;
            }

            if (logUi)
            {
                Debug.Log("[界面] 点击开始游戏。", this);
            }

            gameFlow.StartMatch();
        }

        private void HandleRestartClicked()
        {
            if (gameFlow == null)
            {
                return;
            }

            gameFlow.ReturnToMenu();
            gameFlow.StartMatch();
        }

        private void HandleReturnToMenuClicked()
        {
            if (gameFlow == null)
            {
                return;
            }

            if (logUi)
            {
                Debug.Log("[界面] 返回主菜单。", this);
            }

            gameFlow.ReturnToMenu();
        }

        // ---------- 状态切换 ----------

        private void HandleStateChanged(MatchState from, MatchState to)
        {
            ApplyState(to);
        }

        private void HandleMatchFinished(MatchState result)
        {
            ApplyState(result);
        }

        private void ApplyState(MatchState state)
        {
            bool menu = state == MatchState.MainMenu;
            bool running = state == MatchState.Loading || state == MatchState.Collect || state == MatchState.Elimination;
            bool finished = state == MatchState.Victory || state == MatchState.Defeat;

            if (mainMenuRoot != null)
            {
                mainMenuRoot.SetActive(menu);
            }

            if (hudRoot != null)
            {
                hudRoot.SetActive(running);
            }

            if (resultRoot != null)
            {
                resultRoot.SetActive(finished);
            }

            if (finished)
            {
                bool victory = state == MatchState.Victory;
                if (resultTitle != null)
                {
                    resultTitle.text = victory ? "胜  利" : "失  败";
                    resultTitle.color = victory ? UiBuilder.AccentColor : new Color(0.9f, 0.35f, 0.32f);
                }

                if (resultReason != null)
                {
                    string reason = gameFlow != null && gameFlow.Flow != null ? gameFlow.Flow.LastRejection : null;
                    resultReason.text = victory
                        ? "场上只剩你一个人。"
                        : ("你被淘汰了。" + (string.IsNullOrEmpty(reason) ? string.Empty : "\n" + reason));
                }
            }
            else if (running)
            {
                RefreshHud();
            }
        }

        // ---------- HUD 刷新 ----------

        private void RefreshHud()
        {
            if (gameFlow == null || stateLabel == null)
            {
                return;
            }

            MatchState state = gameFlow.State;

            stateLabel.text = "阶段：" + state.ToDisplayName() + "\n" + state.ToHint();

            BellSystem bells = gameFlow.Bells;
            if (state == MatchState.Loading)
            {
                timerLabel.text = "准备中…";
            }
            else if (state == MatchState.Elimination || (bells != null && bells.Phase == BellPhase.Elimination))
            {
                timerLabel.text = "淘汰赛";
            }
            else if (bells != null)
            {
                timerLabel.text = "下一次钟声：" + Mathf.CeilToInt(Mathf.Max(0f, bells.SecondsUntilNextBell)) + " 秒";
            }
            else
            {
                timerLabel.text = "（没有钟声系统）";
            }

            CharacterInstance player = gameFlow.Player;
            if (player != null)
            {
                var builder = new StringBuilder();
                builder.Append("你：").Append(player.DisplayName)
                    .Append("　生命 ").Append(Mathf.CeilToInt(player.CurrentHealth)).Append('/')
                    .Append(Mathf.CeilToInt(player.MaxHealth))
                    .Append("　防御 ").Append(Mathf.RoundToInt(player.Stats != null ? player.Stats.Defense : 0f));
                builder.Append('\n').Append(DescribeCards(player.Participant));
                playerLabel.text = builder.ToString();
            }
            else
            {
                playerLabel.text = string.Empty;
            }

            rosterLabel.text = DescribeRoster();
        }

        private string DescribeRoster()
        {
            MatchSpawner spawner = gameFlow != null ? gameFlow.Spawner : null;
            if (spawner == null || spawner.Instances.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder("场上：");
            int alive = 0;
            for (int i = 0; i < spawner.Instances.Count; i++)
            {
                CharacterInstance character = spawner.Instances[i];
                if (character == null)
                {
                    continue;
                }

                bool inPlay = character.Participant == null || character.Participant.IsInPlay;
                if (inPlay && !character.IsDead)
                {
                    alive++;
                }

                builder.Append('\n').Append(inPlay ? "· " : "× ").Append(character.DisplayName);
                if (character.HasCompleteSet)
                {
                    builder.Append(" ★");
                }
            }

            builder.Append("\n在场 ").Append(alive).Append(" 人");
            return builder.ToString();
        }

        private static string DescribeCards(IBellParticipant participant)
        {
            if (participant == null)
            {
                return "牌：—";
            }

            var builder = new StringBuilder("牌：");
            if (participant.Traits.Count == 0)
            {
                builder.Append("（无特质）");
            }
            else
            {
                for (int i = 0; i < participant.Traits.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("、");
                    }

                    builder.Append(participant.Traits[i].ToDisplayName());
                }
            }

            builder.Append("　技能 ").Append(participant.Skills.Count).Append("/3");
            if (BellCards.HasCompleteSet(participant.Traits, participant.Skills))
            {
                builder.Append("　★已集齐");
            }

            return builder.ToString();
        }
    }
}
