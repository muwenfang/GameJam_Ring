using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 力士序列帧表现：Idle（站立）/ Run（跑动循环）/ Punch（连环拳单次）
    /// / Dash（崩进冲撞单次）/ Jump（跳跃单次）。
    ///
    /// 素材来自 Assets/Resources/Strongman，四套序列的画布尺寸与人物占比差异明显：
    /// Run 219×250、Dash 256×240、Punch 362×362、Jump 192×200。按 alpha 包围盒实测
    /// 「脚底到 Sprite 中心」的像素距离分别为 109 / 88 / 153 / 82（PPU = 100）。
    /// 若统一按中心摆放，切换动作时脚底最多跳 71 px（scale 0.72 下约 0.51 世界单位）。
    /// 本组件按「脚底锚定」为每套序列换算局部偏移，让脚底始终落在 transform 原点。
    ///
    /// 帧序、帧率与 Assets/Animations/Strongman 下五个 .anim 剪辑一一对应：
    /// Idle 单帧 / Run 7 帧 10fps 循环 / Punch 12 帧 14fps 单次
    /// / Dash 6 帧 14fps 单次 / Jump 8 帧 18fps 单次（0.444s，贴合 CombatBody 的 0.45s 滞空）。
    ///
    /// 伤害判定由 AbilityCaster 以回调传入，在命中帧触发；动作被打断或播完时兜底补发，
    /// 保证「连点攻击」不会因协程被 StopCoroutine 而凭空丢伤害。
    ///
    /// 跳跃接线：PlayerMotor 重构为 CombatBody 驱动后不再直接调用 PlayJump()，
    /// 这里由组件订阅 CombatBody.IsAirborne 的上升沿自行播放，避免改动战斗层脚本。
    /// 人体高度仍由 CombatBody.LateUpdate 驱动 JumpOffset 子物体，本组件不叠加位移。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/力士序列帧 StrongmanSpriteAnimation")]
    public sealed class StrongmanSpriteAnimation : MonoBehaviour
    {
        private const string ResourceFolder = "Strongman";
        private const float PixelsPerUnit = 100f;

        [Header("渲染目标")]
        [Tooltip("留空时自动取本物体（或子物体）上的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Header("帧率（与 .anim 剪辑一致）")]
        [SerializeField, Min(1f)] private float runFps = 10f;
        [SerializeField, Min(1f)] private float actionFps = 14f;
        [SerializeField, Min(1f)] private float jumpFps = 18f;

        [Header("脚底对齐（像素；PPU = 100，实测 alpha 包围盒）")]
        [SerializeField] private float runFeetOffsetPixels = 109f;
        [SerializeField] private float dashFeetOffsetPixels = 88f;
        [SerializeField] private float punchFeetOffsetPixels = 153f;
        [SerializeField] private float jumpFeetOffsetPixels = 82f;

        [Header("显示尺寸")]
        [Tooltip("整体缩放，默认 0.72 与其他人设占位量级接近。")]
        [SerializeField, Min(0.01f)] private float visualScale = 0.72f;
        [Tooltip("四套素材人物像素高度中位数 190/153/249/181，Punch 固有偏大约 30%。默认全 1 保持统一缩放；如需对齐观感可逐套微调。")]
        [SerializeField, Min(0.01f)] private float runScaleMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float dashScaleMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float punchScaleMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float jumpScaleMultiplier = 1f;

        [Header("命中帧（与 .anim 一致）")]
        [Tooltip("连环拳结算伤害的帧下标；AbilityCaster 的平A伤害在此帧落地。")]
        [SerializeField, Min(0)] private int punchImpactFrameIndex = 4;
        [Tooltip("崩进冲撞结算伤害的帧下标；-1 表示取序列最后一帧（冲击爆发帧）。")]
        [SerializeField, Min(-1)] private int dashImpactFrameIndex = -1;

        [Header("跳跃")]
        [Tooltip("关闭后不再自动订阅 CombatBody.IsAirborne，改由外部自行调用 PlayJump()。")]
        [SerializeField] private bool autoPlayJumpOnAirborne = true;

        private Sprite[] run, dash, punch, jump;
        private Coroutine action;
        private Action pendingImpact;
        private bool impactFired;
        private bool moving;
        private bool ready;
        private float runTimer;
        private int runIndex;

        // 当前单次动作使用的锚定参数；Sequence 协程按帧读取。
        private float activeFeetOffset;
        private float activeScaleMultiplier;
        private string activeAction = string.Empty;

        private Ring.Flow.CombatBody body;
        private bool wasAirborne;

        /// <summary>当前实际渲染器，供外部取 flipX / sortingOrder。</summary>
        public SpriteRenderer Renderer => targetRenderer;

        /// <summary>是否有单次动作（连环拳 / 冲撞 / 跳跃）正在播放。</summary>
        public bool IsActing => action != null;
        public bool IsDashing => action != null && activeAction == "dash";
        public bool IsPunching => action != null && activeAction == "punch";
        public bool IsJumping => action != null && activeAction == "jump";
        public bool IsMoving => moving;

        private void Awake()
        {
            SetUp();
        }

        private void Update()
        {
            if (!ready || targetRenderer == null)
            {
                return;
            }

            PollJumpState();

            if (action != null)
            {
                return;
            }

            if (!moving || run == null || run.Length == 0)
            {
                ApplyCurrentState();
                return;
            }

            runTimer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(1f, runFps);
            while (runTimer >= frameTime)
            {
                runTimer -= frameTime;
                runIndex = (runIndex + 1) % run.Length;
            }

            ApplyFrame(run[runIndex], runFeetOffsetPixels, runScaleMultiplier);
        }

        /// <summary>由 PlayerMotor 调用：true 播跑步循环，false 回到站立帧。</summary>
        public void SetMoving(bool value)
        {
            if (moving == value)
            {
                return;
            }

            moving = value;
            runTimer = 0f;
            runIndex = 0;

            if (action == null)
            {
                ApplyCurrentState();
            }
        }

        /// <summary>
        /// 打一次崩进冲撞。impact 在冲击爆发帧（默认最后一帧）触发一次，用于结算伤害。
        /// 返回 false 表示帧缺失 / 未就绪，调用方应立即自行结算。
        /// </summary>
        public bool PlayDash(Action impact)
        {
            int impactFrame = dashImpactFrameIndex >= 0
                ? Mathf.Min(dashImpactFrameIndex, Mathf.Max(0, (dash != null ? dash.Length : 1) - 1))
                : Mathf.Max(0, (dash != null ? dash.Length : 1) - 1);
            return Play(dash, impact, impactFrame, dashFeetOffsetPixels,
                        dashScaleMultiplier, actionFps, "dash");
        }

        /// <summary>
        /// 打一次连环拳。impact 在 punchImpactFrameIndex 触发一次，语义同 <see cref="PlayDash"/>。
        /// </summary>
        public bool PlayPunch(Action impact)
        {
            int impactFrame = punch != null && punch.Length > 0
                ? Mathf.Min(punchImpactFrameIndex, punch.Length - 1)
                : 0;
            return Play(punch, impact, impactFrame, punchFeetOffsetPixels,
                        punchScaleMultiplier, actionFps, "punch");
        }

        /// <summary>空格起跳时播放完整跳跃分镜；18fps × 8 帧 = 0.444s，与 CombatBody 滞空时长一致。</summary>
        public bool PlayJump()
        {
            return Play(jump, null, int.MaxValue, jumpFeetOffsetPixels,
                        jumpScaleMultiplier, jumpFps, "jump");
        }

        /// <summary>
        /// 外部重建 Visual 之后调用（例如角色被提升为玩家时重建外观），
        /// 重新绑定渲染器并套用当前状态帧、缩放与脚底对齐。
        /// </summary>
        public void RefreshVisual()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            body = null;

            if (!ready)
            {
                SetUp();
                return;
            }

            if (action == null)
            {
                ApplyCurrentState();
            }
        }

        private bool Play(Sprite[] frames, Action impact, int impactFrame,
                          float feetOffset, float scaleMultiplier, float fps, string kind)
        {
            if (!ready || targetRenderer == null || frames == null || frames.Length == 0)
            {
                return false;
            }

            if (action != null)
            {
                // 上一次动作被这里打断：若它还没到命中帧，立即补结算，避免伤害凭空消失。
                FireImpact();
                StopCoroutine(action);
            }

            pendingImpact = impact;
            impactFired = false;
            activeFeetOffset = feetOffset;
            activeScaleMultiplier = scaleMultiplier;
            activeAction = kind;
            action = StartCoroutine(Sequence(frames, impactFrame, fps));
            return true;
        }

        private IEnumerator Sequence(Sprite[] frames, int impactFrame, float fps)
        {
            float frameTime = 1f / Mathf.Max(1f, fps);

            for (int i = 0; i < frames.Length; i++)
            {
                ApplyFrame(frames[i], activeFeetOffset, activeScaleMultiplier);
                if (i >= impactFrame)
                {
                    FireImpact();
                }

                yield return new WaitForSeconds(frameTime);
            }

            // 兜底：时间轴里若没有命中帧（参数被改坏），也要保证结算不被吞掉。
            FireImpact();
            action = null;
            activeAction = string.Empty;
            ResumeLocomotion();
        }

        /// <summary>
        /// 触发本次动作的命中结算，只触发一次。
        /// 命中帧、动作播完、动作被打断三处都会走到这里，确保伤害既不提前也不丢失。
        /// </summary>
        private void FireImpact()
        {
            if (impactFired)
            {
                return;
            }

            impactFired = true;
            Action callback = pendingImpact;
            pendingImpact = null;
            if (callback != null)
            {
                callback();
            }
        }

        /// <summary>单次动作结束后接回当前移动/待机状态，避免 Update 接手前闪一帧。</summary>
        private void ResumeLocomotion()
        {
            runTimer = 0f;
            runIndex = 0;
            ApplyCurrentState();
        }

        private void ApplyCurrentState()
        {
            if (run != null && run.Length > 0)
            {
                ApplyFrame(run[0], runFeetOffsetPixels, runScaleMultiplier);
            }
        }

        private void ApplyFrame(Sprite sprite, float feetOffsetPixels, float scaleMultiplier)
        {
            if (targetRenderer == null || sprite == null)
            {
                return;
            }

            if (targetRenderer.sprite != sprite)
            {
                targetRenderer.sprite = sprite;
            }

            float scale = Mathf.Max(0.01f, visualScale) * Mathf.Max(0.01f, scaleMultiplier);
            float feetWorld = feetOffsetPixels / PixelsPerUnit * scale;

            // 脚底锚定：Sprite 中心上移「脚底到中心的距离」，脚底即落在 transform 原点。
            transform.localScale = new Vector3(scale, scale, 1f);
            transform.localPosition = new Vector3(0f, feetWorld, 0f);
        }

        /// <summary>
        /// 订阅 CombatBody.IsAirborne 的上升沿播放跳跃帧。
        /// 组件挂在 Visual 子物体上，CombatBody 在角色根物体，用 GetComponentInParent 向上取。
        /// 战斗层脚本正在被并行重构，这样接线不改动它们；若将来引入事件总线，可改为订阅事件。
        /// </summary>
        private void PollJumpState()
        {
            if (!autoPlayJumpOnAirborne)
            {
                return;
            }

            if (body == null)
            {
                body = GetComponentInParent<Ring.Flow.CombatBody>();
            }

            bool airborne = body != null && body.IsAirborne;
            if (airborne && !wasAirborne && action == null)
            {
                PlayJump();
            }

            wasAirborne = airborne;
        }

        private void SetUp()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            if (targetRenderer == null)
            {
                targetRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            LoadFrames();
            ready = true;

            if (targetRenderer == null)
            {
                Debug.LogWarning("[力士动画] 没有找到 SpriteRenderer，序列帧无处渲染。", this);
                return;
            }

            ApplyCurrentState();
        }

        private void LoadFrames()
        {
            Sprite[] all = Resources.LoadAll<Sprite>(ResourceFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[力士动画] Assets/Resources/" + ResourceFolder
                                 + " 下没有找到任何 Sprite，角色将保持不可见。", this);
                run = Array.Empty<Sprite>();
                dash = Array.Empty<Sprite>();
                punch = Array.Empty<Sprite>();
                jump = Array.Empty<Sprite>();
                return;
            }

            run = Filter(all, "Run_");
            dash = Filter(all, "Dash_");
            punch = Filter(all, "Punch_");
            jump = Filter(all, "Jump_");

            if (run.Length == 0) Debug.LogWarning("[力士动画] 没有找到 Run_* 帧。", this);
            if (dash.Length == 0) Debug.LogWarning("[力士动画] 没有找到 Dash_* 帧。", this);
            if (punch.Length == 0) Debug.LogWarning("[力士动画] 没有找到 Punch_* 帧。", this);
            if (jump.Length == 0) Debug.LogWarning("[力士动画] 没有找到 Jump_* 帧。", this);
        }

        private static Sprite[] Filter(Sprite[] all, string prefix)
        {
            return all
                .Where(s => s != null && s.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(s => s.name, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
