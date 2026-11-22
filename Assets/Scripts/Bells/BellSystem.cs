using System;
using System.Collections.Generic;
using Ring.Core;
using UnityEngine;

namespace Ring.Bells
{
    /// <summary>
    /// 钟声系统的 Unity 外壳：负责每帧推进计时、把纯逻辑结果转成事件、管理参与者注册。
    /// 真正的规则都在 BellRuntime / 三个 IBellEffect 里，方便离线测试与后续替换表现层。
    ///
    /// 使用方式：
    /// 1）场景里放一个挂了本脚本的物体（或从代码 new GameObject 后 AddComponent）；
    /// 2）角色脚本实现 IBellParticipant 并在开局调用 Register；
    /// 3）订阅 BellRung / PhaseChanged 做播报、转场与 UI；
    /// 4）击杀后调用 NotifyDeath，选择追踪时调用 NotifyTracking（B5/B6 会自动强制位置钟声）。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/钟声系统 BellSystem")]
    public sealed class BellSystem : MonoBehaviour
    {
        private static BellSystem instance;

        [SerializeField]
        [Tooltip("钟声规则资产；留空时使用文档默认值。")]
        private BellSettings settings;

        [SerializeField]
        [Tooltip("是否每帧自动推进 30 秒计时（B4）。关掉后只能手动敲钟。")]
        private bool autoUpdate = true;

        [SerializeField]
        [Tooltip("是否把每次钟声结果打印到 Console（联调期建议打开）。")]
        private bool logBells = true;

        [SerializeField]
        [Tooltip("调试用：按下该键立刻随机敲响一次钟声。正式按键请走 GameInput。")]
        private bool debugRingEnabled;

        [SerializeField]
        private KeyCode debugRingKey = KeyCode.B;

        private BellParticipantRegistry registry;

        /// <summary>当前场景里的钟声系统（没有则返回 null）。</summary>
        public static BellSystem Instance
        {
            get { return instance; }
        }

        /// <summary>纯逻辑核心，测试/结算面板可以直接读。</summary>
        public BellRuntime Runtime { get; private set; }

        public BellSettings Settings
        {
            get { return settings; }
            set
            {
                settings = value;
                if (Runtime != null)
                {
                    Runtime.Rules.CopyFrom(settings != null ? settings.ToRules() : new BellRules());
                }
            }
        }

        public BellPhase Phase
        {
            get { return Runtime != null ? Runtime.Phase : BellPhase.Normal; }
        }

        public float SecondsUntilNextBell
        {
            get { return Runtime != null ? Runtime.SecondsUntilNextBell : 0f; }
        }

        /// <summary>每次钟声执行后触发（B1/B3/B4/B5 的播报入口）。</summary>
        public event Action<BellResult> BellRung;

        /// <summary>B9：切换到淘汰制时触发。</summary>
        public event Action<BellPhase> PhaseChanged;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[钟声] 场景里已存在 BellSystem，销毁重复实例：" + name, this);
                Destroy(this);
                return;
            }

            instance = this;
            registry = new BellParticipantRegistry();
            Runtime = new BellRuntime(registry, settings != null ? settings.ToRules() : new BellRules());
            Runtime.BellRung += HandleRuntimeBellRung;
            Runtime.PhaseChanged += HandlePhaseChanged;
        }

        private void OnDestroy()
        {
            if (Runtime != null)
            {
                Runtime.BellRung -= HandleRuntimeBellRung;
                Runtime.PhaseChanged -= HandlePhaseChanged;
            }

            if (instance == this)
            {
                instance = null;
            }
        }

        private void Update()
        {
            if (Runtime == null)
            {
                return;
            }

            if (autoUpdate)
            {
                Runtime.Update(Time.deltaTime);
            }

            if (debugRingEnabled && Input.GetKeyDown(debugRingKey))
            {
                RingNowRandom(BellTrigger.Manual);
            }
        }

        // ---------- 参与者 ----------

        public bool Register(IBellParticipant participant)
        {
            if (Runtime == null || participant == null)
            {
                return false;
            }

            string error;
            bool added = Runtime.Registry.TryAdd(participant, out error);
            if (!added)
            {
                Debug.LogWarning("[钟声] 注册参与者失败：" + error, this);
            }

            return added;
        }

        public bool Register(BellParticipant participant)
        {
            return Register((IBellParticipant)participant);
        }

        public bool Unregister(int participantId)
        {
            return Runtime != null && Runtime.Registry.Remove(participantId);
        }

        public IBellParticipant GetParticipant(int participantId)
        {
            return Runtime != null ? Runtime.Registry.Get(participantId) : null;
        }

        // ---------- 触发 ----------

        /// <summary>B4：手动敲一次随机种类的钟声。</summary>
        public BellResult RingNowRandom(BellTrigger trigger = BellTrigger.Manual)
        {
            return Runtime != null ? Runtime.RingRandomKind(trigger) : null;
        }

        /// <summary>敲指定种类的钟声（调试/剧情用）。</summary>
        public BellResult RingNow(BellKind kind, BellTrigger trigger = BellTrigger.Manual)
        {
            return Runtime != null ? Runtime.Ring(kind, trigger) : null;
        }

        /// <summary>B3：某个角色死亡 → 必定触发位置钟声。</summary>
        public BellResult NotifyDeath(int deadParticipantId)
        {
            return Runtime != null ? Runtime.NotifyDeath(deadParticipantId) : null;
        }

        /// <summary>B5/B6：击杀后选择追踪 → 本次必定位置钟声。</summary>
        public BellResult NotifyTracking(int trackerId, int targetId)
        {
            return Runtime != null ? Runtime.NotifyTracking(trackerId, targetId) : null;
        }

        /// <summary>B6：击杀后选择夺取对手一张牌。</summary>
        public bool TryStealOneCard(int winnerId, int victimId)
        {
            return Runtime != null && Runtime.TryStealOneCard(winnerId, victimId);
        }

        /// <summary>B11：胜者从组件池取一张牌；返回 false 时应结算「恢复 20% 生命」。</summary>
        public bool TryTakeCardFromPool(int winnerId, bool preferTraitFirst = true)
        {
            if (Runtime == null)
            {
                return false;
            }

            IBellParticipant winner = Runtime.Registry.Get(winnerId);
            return Runtime.Pool.TryGrantOneTo(winner, preferTraitFirst);
        }

        /// <summary>B11 的回血比例，供角色层结算。</summary>
        public float EliminationHealPercent
        {
            get { return Runtime != null ? Runtime.Rules.EliminationHealPercent : 0f; }
        }

        public IBellParticipant CheckVictory()
        {
            return Runtime != null ? Runtime.CheckVictory() : null;
        }

        public void ResetTimer()
        {
            if (Runtime != null)
            {
                Runtime.ResetTimer();
            }
        }

        // ---------- 校验 ----------

        /// <summary>把规则、名册、追踪、组件池的问题汇总成一份报告（Editor 校验菜单会调用）。</summary>
        public ValidationReport Validate()
        {
            var report = new ValidationReport();
            if (settings != null)
            {
                settings.CollectIssues(report);
            }
            else
            {
                report.Warn("BellSystem.settings", "未指定 BellSettings，将使用代码里的文档默认值。");
            }

            if (Runtime != null)
            {
                Runtime.CollectIssues(report);
            }
            else
            {
                report.Error("BellSystem", "BellRuntime 未初始化（可能不在 Play 模式或 Awake 尚未执行）。");
            }

            return report;
        }

        [ContextMenu("打印当前钟声状态")]
        private void LogStatus()
        {
            if (Runtime == null)
            {
                Debug.Log("[钟声] 尚未初始化。", this);
                return;
            }

            var lines = new List<string>
            {
                settings != null ? settings.ToSummary() : "（未指定 BellSettings，使用默认规则）",
                "阶段：" + Runtime.Phase.ToDisplayName() + "，已敲响 " + Runtime.SequenceNumber + " 次",
                "在场人数：" + Runtime.Registry.InPlayCount,
                Runtime.Tracking.Describe(),
                Runtime.Pool.Describe(),
            };

            if (Runtime.LastResult != null)
            {
                lines.Add("最近一次结果：\n" + Runtime.LastResult.ToMultilineText());
            }

            Debug.Log("[钟声] " + string.Join("\n", lines), this);
        }

        private void HandleRuntimeBellRung(BellResult result)
        {
            if (logBells && result != null)
            {
                Debug.Log("[钟声] " + result.ToMultilineText(), this);
            }

            if (BellRung != null)
            {
                BellRung(result);
            }
        }

        private void HandlePhaseChanged(BellPhase phase)
        {
            if (logBells)
            {
                Debug.Log("[钟声] 阶段切换 → " + phase.ToDisplayName() + "（B9）", this);
            }

            if (PhaseChanged != null)
            {
                PhaseChanged(phase);
            }
        }
    }
}
