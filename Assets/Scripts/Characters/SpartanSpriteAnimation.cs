using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 斯巴达战士序列帧表现：Idle（半举盾）/ Thrust（长矛突刺）/ Sweep（战阵横扫）
    /// / Shield（绝对防御：抬盾 → 保持 → 收盾）。
    ///
    /// 素材来自 Assets/Resources/Spartan，目前只有三套动作帧：
    /// Thrust_00~05（6 帧，画布 234×230）、Sweep_00~06（7 帧，217×215）、
    /// Shield_00~05（6 帧，213×247）。按 alpha 包围盒实测「脚底到 Sprite 中心」的
    /// 像素距离分别为 114 / 106.5 / 121.5（PPU = 100）；若统一按中心摆放，切换动作时
    /// 脚底最多跳 15 px。本组件按「脚底锚定」为每套换算局部偏移，脚底恒在 transform 原点。
    ///
    /// 素材没有 Run / Idle 专用帧，因此：
    /// - Idle 取 Shield_00（半举盾）——正好对应特质「半举盾姿态」；
    /// - 移动时保持该姿态（不虚构跑步摆腿）；
    /// - 若日后补进 Run_*.png，本组件会自动识别并循环播放，无需改代码。
    ///
    /// 帧序与 Assets/Animations/Spartan 下五个 .anim 剪辑一一对应：
    /// Idle 单帧 / Thrust 6 帧 15fps（命中帧 3 = 0.200s，正好是规格的前摇 0.2s）
    /// / Sweep 7 帧 12fps（命中帧 3 = 0.250s，弧光帧）
    /// / Shield 6 帧 12fps（0.5s 抬盾，末帧为完全举盾）/ ShieldHold 单帧。
    ///
    /// 伤害判定由 AbilityCaster 以回调传入，在命中帧触发；动作被打断或播完时兜底补发，
    /// 保证「连点攻击」不会因协程被 StopCoroutine 而凭空丢伤害。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/斯巴达序列帧 SpartanSpriteAnimation")]
    public sealed class SpartanSpriteAnimation : MonoBehaviour
    {
        private const string ResourceFolder = "Spartan";
        private const float PixelsPerUnit = 100f;

        [Header("渲染目标")]
        [Tooltip("留空时自动取本物体（或子物体）上的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Header("帧率（与 .anim 剪辑一致）")]
        [Tooltip("突刺 6 帧 15fps = 0.4s；命中帧 3 = 0.2s，对应规格的前摇 0.2s。")]
        [SerializeField, Min(1f)] private float thrustFps = 15f;
        [Tooltip("横扫 7 帧 12fps = 0.583s；命中帧 3 = 0.25s，对应蓄力后的弧光帧。")]
        [SerializeField, Min(1f)] private float sweepFps = 12f;
        [Tooltip("举盾 6 帧 12fps = 0.5s 抬满；收盾按同一帧率倒序播回。")]
        [SerializeField, Min(1f)] private float shieldFps = 12f;
        [Tooltip("仅在存在 Run_*.png 时生效。")]
        [SerializeField, Min(1f)] private float runFps = 10f;

        [Header("脚底对齐（像素；PPU = 100，实测 alpha 包围盒）")]
        [SerializeField] private float idleFeetOffsetPixels = 121.5f;
        [SerializeField] private float thrustFeetOffsetPixels = 114f;
        [SerializeField] private float sweepFeetOffsetPixels = 106.5f;
        [SerializeField] private float shieldFeetOffsetPixels = 121.5f;
        [SerializeField] private float runFeetOffsetPixels = 121.5f;

        [Header("显示尺寸")]
        [Tooltip("整体缩放。三套素材人物像素高度中位数 149/154/171，0.78 与其他人设量级接近。")]
        [SerializeField, Min(0.01f)] private float visualScale = 0.78f;
        [Tooltip("逐套微调倍率，默认全 1 保持统一缩放。")]
        [SerializeField, Min(0.01f)] private float thrustScaleMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float sweepScaleMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float shieldScaleMultiplier = 1f;

        [Header("命中帧（与 .anim 一致）")]
        [Tooltip("突刺结算伤害的帧下标；3 / 15fps = 0.200s，对应规格前摇 0.2s。")]
        [SerializeField, Min(0)] private int thrustImpactFrameIndex = 3;
        [Tooltip("横扫结算伤害的帧下标；3 / 12fps = 0.250s，对应弧光帧。")]
        [SerializeField, Min(0)] private int sweepImpactFrameIndex = 3;

        [Header("AnimationClip 绑定（Inspector 拖入）")]
        [Tooltip("把 Assets/Animations/Spartan/ 下的五个 .anim 拖到这里，即完成资产的显式绑定与一致性校验。运行时切帧仍读 Resources/Spartan，字段不参与播放；若要改用 Animator，见 Docs/17 第七节。")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip thrustClip;
        [SerializeField] private AnimationClip sweepClip;
        [SerializeField] private AnimationClip shieldClip;
        [SerializeField] private AnimationClip shieldHoldClip;

        private Sprite[] thrust, sweep, shield, idle, run;
        private Coroutine action;
        private Action pendingImpact;
        private bool impactFired;
        private bool moving;
        private bool ready;
        private float runTimer;
        private int runIndex;

        // 举盾状态：抬盾阶段 → 保持阶段（可被 EndShield 打断）→ 收盾阶段。
        private bool shieldCancelRequested;
        private float shieldHoldSeconds;

        // 当前单次动作使用的锚定参数；协程按帧读取。
        private float activeFeetOffset;
        private float activeScaleMultiplier;
        private string activeAction = string.Empty;

        /// <summary>当前实际渲染器，供外部取 flipX / sortingOrder。</summary>
        public SpriteRenderer Renderer => targetRenderer;

        /// <summary>是否有单次动作（突刺 / 横扫 / 举盾）正在播放。</summary>
        public bool IsActing => action != null;
        public bool IsThrusting => action != null && activeAction == "thrust";
        public bool IsSweeping => action != null && activeAction == "sweep";
        public bool IsShielding => action != null && activeAction == "shield";
        public bool IsMoving => moving;

        /// <summary>是否已经抬满盾（等价于「绝对防御」正在生效的表现）。</summary>
        public bool IsShieldRaised { get; private set; }

        /// <summary>五个 AnimationClip 是否都已在 Inspector 绑定（仅状态查询，不影响运行时切帧）。</summary>
        public bool ClipsBound => idleClip != null && thrustClip != null && sweepClip != null
                                  && shieldClip != null && shieldHoldClip != null;

        /// <summary>列出五个剪辑的绑定状态，便于核对「斯巴达动画是否已绑定」。</summary>
        public string DescribeClipBindings()
        {
            return "Idle=" + ClipName(idleClip) + " / Thrust=" + ClipName(thrustClip)
                 + " / Sweep=" + ClipName(sweepClip) + " / Shield=" + ClipName(shieldClip)
                 + " / ShieldHold=" + ClipName(shieldHoldClip);
        }

        [ContextMenu("校验 AnimationClip 绑定")]
        private void LogClipBindings()
        {
            Debug.Log("[斯巴达动画] " + DescribeClipBindings(), this);
            ValidateClipBindings(true);
        }

        private static string ClipName(AnimationClip clip) => clip != null ? clip.name : "未绑定";

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

            if (action != null)
            {
                return;
            }

            if (!moving || run == null || run.Length == 0)
            {
                ApplyIdle();
                return;
            }

            runTimer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(1f, runFps);
            while (runTimer >= frameTime)
            {
                runTimer -= frameTime;
                runIndex = (runIndex + 1) % run.Length;
            }

            ApplyFrame(run[runIndex], runFeetOffsetPixels, 1f);
        }

        /// <summary>
        /// 由 PlayerMotor 调用：true 表示在移动，false 回到半举盾待机。
        /// 素材没有跑步循环，移动时保持半举盾姿态；日后补 Run_*.png 会自动切换。
        /// </summary>
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
                if (moving && run != null && run.Length > 0)
                {
                    ApplyFrame(run[0], runFeetOffsetPixels, 1f);
                }
                else
                {
                    ApplyIdle();
                }
            }
        }

        /// <summary>
        /// 打一次长矛突刺（普攻 / 投矛）。impact 在命中帧（默认第 3 帧 = 0.200s）触发一次。
        /// 返回 false 表示帧缺失 / 未就绪，调用方应立即自行结算。
        /// </summary>
        public bool PlayThrust(Action impact)
        {
            int frames = thrust != null ? thrust.Length : 0;
            int impactFrame = frames > 0 ? Mathf.Min(thrustImpactFrameIndex, frames - 1) : 0;
            return Play(thrust, impact, impactFrame, thrustFeetOffsetPixels,
                        thrustScaleMultiplier, thrustFps, "thrust");
        }

        /// <summary>
        /// 打一次战阵横扫（本我）。impact 在弧光帧（默认第 3 帧 = 0.250s）触发一次。
        /// </summary>
        public bool PlaySweep(Action impact)
        {
            int frames = sweep != null ? sweep.Length : 0;
            int impactFrame = frames > 0 ? Mathf.Min(sweepImpactFrameIndex, frames - 1) : 0;
            return Play(sweep, impact, impactFrame, sweepFeetOffsetPixels,
                        sweepScaleMultiplier, sweepFps, "sweep");
        }

        /// <summary>
        /// 「绝对防御」：抬盾（0.5s）→ 保持 <paramref name="holdSeconds"/> 秒（可被
        /// <see cref="EndShield"/> 提前打断）→ 倒序收盾。onRaised 在完全举盾瞬间回调一次。
        /// 幂等：已在举盾时重复调用直接返回 true，避免姿态被重置。
        /// </summary>
        public bool BeginShield(float holdSeconds, Action onRaised = null)
        {
            if (action != null && activeAction == "shield")
            {
                return true;
            }

            if (!ready || targetRenderer == null || shield == null || shield.Length == 0)
            {
                return false;
            }

            Interrupt();
            pendingImpact = null;
            impactFired = true;

            // holdSeconds 是「姿态总时长」，抬盾本身要占 shield.Length / shieldFps 秒，
            // 保持段要扣掉这一段，否则盾会比防护效果多举 0.5s。
            float raiseSeconds = shield.Length / Mathf.Max(1f, shieldFps);
            shieldHoldSeconds = Mathf.Max(0f, holdSeconds - raiseSeconds);
            shieldCancelRequested = false;
            IsShieldRaised = false;
            activeFeetOffset = shieldFeetOffsetPixels;
            activeScaleMultiplier = shieldScaleMultiplier;
            activeAction = "shield";
            action = StartCoroutine(ShieldSequence(onRaised));
            return true;
        }

        /// <summary>提前收盾（自我技能再次点击 / 姿态被取消 / 角色死亡）。</summary>
        public void EndShield()
        {
            if (action != null && activeAction == "shield")
            {
                shieldCancelRequested = true;
            }
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

            if (!ready)
            {
                SetUp();
                return;
            }

            if (action == null)
            {
                ApplyIdle();
            }
        }

        private bool Play(Sprite[] frames, Action impact, int impactFrame,
                          float feetOffset, float scaleMultiplier, float fps, string kind)
        {
            if (!ready || targetRenderer == null || frames == null || frames.Length == 0)
            {
                return false;
            }

            Interrupt();

            pendingImpact = impact;
            impactFired = false;
            activeFeetOffset = feetOffset;
            activeScaleMultiplier = scaleMultiplier;
            activeAction = kind;
            action = StartCoroutine(Sequence(frames, impactFrame, fps));
            return true;
        }

        /// <summary>打断当前单次动作：先补发未结算的命中回调，再停协程。</summary>
        private void Interrupt()
        {
            if (action == null)
            {
                return;
            }

            FireImpact();
            StopCoroutine(action);
            action = null;
            activeAction = string.Empty;
            IsShieldRaised = false;
            shieldCancelRequested = false;
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

        private IEnumerator ShieldSequence(Action onRaised)
        {
            float frameTime = 1f / Mathf.Max(1f, shieldFps);

            // 抬盾：正向播完全部帧，末帧即「完全举盾」。
            for (int i = 0; i < shield.Length; i++)
            {
                ApplyFrame(shield[i], activeFeetOffset, activeScaleMultiplier);
                yield return new WaitForSeconds(frameTime);
            }

            IsShieldRaised = true;
            onRaised?.Invoke();

            // 保持：逐帧轮询，便于 EndShield 立即收盾，同时尊重 Time.timeScale。
            Sprite held = shield[shield.Length - 1];
            float elapsed = 0f;
            while (!shieldCancelRequested && elapsed < shieldHoldSeconds)
            {
                ApplyFrame(held, activeFeetOffset, activeScaleMultiplier);
                elapsed += Time.deltaTime;
                yield return null;
            }

            IsShieldRaised = false;

            // 收盾：倒序播回（Shield_05 → Shield_00），避免需要额外的收盾素材。
            for (int i = shield.Length - 1; i >= 0; i--)
            {
                ApplyFrame(shield[i], activeFeetOffset, activeScaleMultiplier);
                yield return new WaitForSeconds(frameTime);
            }

            action = null;
            activeAction = string.Empty;
            shieldCancelRequested = false;
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

        /// <summary>单次动作结束后接回当前待机 / 移动状态，避免 Update 接手前闪一帧。</summary>
        private void ResumeLocomotion()
        {
            runTimer = 0f;
            runIndex = 0;

            if (moving && run != null && run.Length > 0)
            {
                ApplyFrame(run[0], runFeetOffsetPixels, 1f);
            }
            else
            {
                ApplyIdle();
            }
        }

        private void ApplyIdle()
        {
            if (idle != null && idle.Length > 0)
            {
                ApplyFrame(idle[0], idleFeetOffsetPixels, 1f);
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
                Debug.LogWarning("[斯巴达动画] 没有找到 SpriteRenderer，序列帧无处渲染。", this);
                return;
            }

            ApplyIdle();
            ValidateClipBindings(false);
        }

        /// <summary>
        /// 校验 Inspector 绑定的 .anim 与 Resources 切帧数据是否一致。
        /// 默认静默（运行时 AddComponent 时字段必为空，不应每局刷日志）；verbose 供 ContextMenu 手动核对。
        /// </summary>
        private void ValidateClipBindings(bool verbose)
        {
            CheckClip("Idle", idleClip, 1, verbose);
            CheckClip("Thrust", thrustClip, thrust, verbose);
            CheckClip("Sweep", sweepClip, sweep, verbose);
            CheckClip("Shield", shieldClip, shield, verbose);
            CheckClip("ShieldHold", shieldHoldClip, 1, verbose);

            if (verbose && !ClipsBound)
            {
                Debug.LogWarning("[斯巴达动画] 五个 AnimationClip 尚未全部绑定：" + DescribeClipBindings()
                                 + "。请在 Inspector 把 Assets/Animations/Spartan/*.anim 拖入对应字段。", this);
            }
        }

        private void CheckClip(string label, AnimationClip clip, int expectedFrames, bool verbose)
        {
            if (clip == null || expectedFrames <= 0)
            {
                return;
            }

            float fps = Mathf.Max(1f, clip.frameRate);
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
            if (frames != expectedFrames)
            {
                Debug.LogWarning("[斯巴达动画] " + label + " 剪辑帧数 " + frames
                                 + " 与 Resources 帧数 " + expectedFrames + " 不一致。", this);
            }
            else if (verbose)
            {
                Debug.Log("[斯巴达动画] " + label + " 剪辑 " + frames + " 帧 @ " + fps + "fps，与运行时一致。", this);
            }
        }

        private void CheckClip(string label, AnimationClip clip, Sprite[] frames, bool verbose)
        {
            CheckClip(label, clip, frames != null ? frames.Length : 0, verbose);
        }

        private void LoadFrames()
        {
            Sprite[] all = Resources.LoadAll<Sprite>(ResourceFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[斯巴达动画] Assets/Resources/" + ResourceFolder
                                 + " 下没有找到任何 Sprite，角色将保持不可见。", this);
                thrust = Array.Empty<Sprite>();
                sweep = Array.Empty<Sprite>();
                shield = Array.Empty<Sprite>();
                idle = Array.Empty<Sprite>();
                run = Array.Empty<Sprite>();
                return;
            }

            thrust = Filter(all, "Thrust_");
            sweep = Filter(all, "Sweep_");
            shield = Filter(all, "Shield_");
            run = Filter(all, "Run_");

            // 没有专用 Idle 帧时退回 Shield_00（半举盾），对应特质描述的姿态。
            Sprite[] idleFrames = Filter(all, "Idle_");
            idle = idleFrames.Length > 0
                ? idleFrames
                : (shield.Length > 0 ? new[] { shield[0] } : Array.Empty<Sprite>());

            if (thrust.Length == 0) Debug.LogWarning("[斯巴达动画] 没有找到 Thrust_* 帧。", this);
            if (sweep.Length == 0) Debug.LogWarning("[斯巴达动画] 没有找到 Sweep_* 帧。", this);
            if (shield.Length == 0) Debug.LogWarning("[斯巴达动画] 没有找到 Shield_* 帧。", this);
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
