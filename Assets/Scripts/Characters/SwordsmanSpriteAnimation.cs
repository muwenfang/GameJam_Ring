using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 剑士序列帧表现：Idle（持刀站立）/ Run（跑步循环）/ Attack（挥剑单次）/ Dash（突刺单次）。
    ///
    /// 素材来自 Assets/Resources/Swordsman，四套序列的画布基准并不一致：
    /// 挥剑帧（256×240）脚底距画布底约 20px，跑步帧（219×240）约 66px，
    /// 突刺帧（302×250）约 17px。若直接按 Sprite 中心摆放，切换动作时会上下跳几十像素。
    /// 这里按「脚底锚定」换算每套序列的局部偏移，让所有动作的脚底始终落在
    /// transform 原点，切换动作时人物不会漂浮。
    ///
    /// 帧序与 Assets/Animations/Swordsman 下的 .anim 剪辑保持一致：
    /// Attack 在蓄力帧与刀光帧各多停留一帧（8 个采样），Run 为 7 帧循环，
    /// Dash 在刀光爆发帧多停留一帧（6 个采样）。
    ///
    /// 注意：突刺的位移由 AbilityCaster 一次性设置 transform.position 完成（瞬移式），
    /// 本组件只负责其后的姿态分镜，不驱动位移；突刺序列也刻意不做水平锚定，
    /// 以便保留素材自带的冲刺速度感。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/剑士序列帧 SwordsmanSpriteAnimation")]
    public sealed class SwordsmanSpriteAnimation : MonoBehaviour
    {
        private const string ResourceFolder = "Swordsman";
        private const string RunPrefix = "Run_";
        private const string AttackPrefix = "Attack_";
        private const string DashPrefix = "Dash_";

        /// <summary>素材导入时的 Pixels Per Unit（见各 png 的 .meta）。</summary>
        private const float PixelsPerUnit = 100f;

        [Header("渲染目标")]
        [Tooltip("留空时自动取本物体（或子物体）上的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Header("帧率")]
        [SerializeField, Min(0.1f)] private float runFramesPerSecond = 12f;
        [SerializeField, Min(0.1f)] private float attackFramesPerSecond = 14f;
        [Tooltip("突刺帧率。略高于挥剑，强调突进的迅捷。")]
        [SerializeField, Min(0.1f)] private float dashFramesPerSecond = 16f;

        [Header("脚底对齐（像素；素材 PPU = 100）")]
        [Tooltip("挥剑序列：脚底到 Sprite 中心的像素距离（脚底在 y≈220，画布高 240）。")]
        [SerializeField] private float attackFeetOffsetPixels = 100f;
        [Tooltip("跑步序列：脚底到 Sprite 中心的像素距离（脚底在 y≈174，画布高 240）。")]
        [SerializeField] private float runFeetOffsetPixels = 54f;
        [Tooltip("突刺序列：脚底到 Sprite 中心的像素距离（脚底在 y≈232，画布高 250）。")]
        [SerializeField] private float dashFeetOffsetPixels = 107f;

        [Header("显示尺寸")]
        [Tooltip("整体缩放。0.75 时人物本体约 1.1 世界单位高，与其他人设的占位块量级接近。")]
        [SerializeField, Min(0.01f)] private float visualScale = 0.75f;

        [Header("挥剑补帧（与 Swordsman_Attack.anim 保持一致）")]
        [Tooltip("蓄力帧在 Attack 序列中的下标。")]
        [SerializeField] private int windUpFrameIndex = 1;
        [Tooltip("蓄力帧额外停留的采样数。")]
        [SerializeField, Range(0, 3)] private int windUpHoldFrames = 1;
        [Tooltip("刀光帧在 Attack 序列中的下标。")]
        [SerializeField] private int impactFrameIndex = 3;
        [Tooltip("刀光帧额外停留的采样数。")]
        [SerializeField, Range(0, 3)] private int impactHoldFrames = 1;

        [Header("突刺补帧（与 Swordsman_Dash.anim 保持一致）")]
        [Tooltip("刀光爆发帧在 Dash 序列中的下标。")]
        [SerializeField] private int dashImpactFrameIndex = 3;
        [Tooltip("刀光爆发帧额外停留的采样数。")]
        [SerializeField, Range(0, 3)] private int dashImpactHoldFrames = 1;

        private Sprite[] idleFrames;
        private Sprite[] runFrames;
        private Sprite[] attackFrames;
        private Sprite[] dashFrames;
        private int[] attackTimeline;
        private int[] dashTimeline;

        /// <summary>当前正在播放的单次动作（攻击或突刺）；null 表示由 Update 接管移动/待机。</summary>
        private Coroutine actionRoutine;

        /// <summary>actionRoutine 当前承载的是突刺而非挥剑。</summary>
        private bool dashing;

        /// <summary>
        /// 本次单次动作到达「命中帧」时执行的一次性回调（由 AbilityCaster 传入伤害结算）。
        /// 用动画帧驱动判定，避免伤害在动作第 0 帧就结算、与挥剑/突刺的视觉脱节。
        /// 动作被打断或播完时都会兜底触发，保证不会出现「动画播了但伤害丢了」。
        /// </summary>
        private System.Action pendingImpact;
        private bool impactFired;

        private bool moving;
        private bool ready;
        private float runTimer;
        private int runIndex;

        /// <summary>是否有单次动作（挥剑或突刺）正在播放，此时 Update 不接管帧。</summary>
        public bool IsActing
        {
            get { return actionRoutine != null; }
        }

        /// <summary>是否正在播放突刺。</summary>
        public bool IsDashing
        {
            get { return actionRoutine != null && dashing; }
        }

        /// <summary>是否正在播放挥剑。</summary>
        public bool IsAttacking
        {
            get { return actionRoutine != null && !dashing; }
        }

        /// <summary>是否处于移动状态（由 PlayerMotor 每帧驱动）。</summary>
        public bool IsMoving
        {
            get { return moving; }
        }

        /// <summary>当前实际渲染器，供外部取 flipX / sortingOrder。</summary>
        public SpriteRenderer Renderer
        {
            get { return targetRenderer; }
        }

        private void Awake()
        {
            SetUp();
        }

        private void Update()
        {
            if (!ready || targetRenderer == null || actionRoutine != null)
            {
                return;
            }

            if (!moving)
            {
                ApplyCurrentState();
                return;
            }

            if (runFrames == null || runFrames.Length == 0)
            {
                ApplyCurrentState();
                return;
            }

            runTimer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(0.1f, runFramesPerSecond);
            while (runTimer >= frameTime)
            {
                runTimer -= frameTime;
                runIndex = (runIndex + 1) % runFrames.Length;
            }

            ApplyFrame(runFrames[runIndex], runFeetOffsetPixels);
        }

        /// <summary>
        /// 由 PlayerMotor 调用：true 播跑步循环，false 回到站立帧。
        /// 单次动作（挥剑/突刺）过程中只记录状态，等动作播完再接管。
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

            if (!moving && actionRoutine == null)
            {
                ApplyCurrentState();
            }
        }

        /// <summary>
        /// 打一次挥剑。onImpact 在刀光帧（impactFrameIndex）触发一次，用于结算伤害。
        /// 返回 false 表示动画没有真正播放（未就绪 / 帧缺失），调用方应立即自行结算。
        /// 重复调用会重新从头播放，并先补发上一次尚未结算的命中，避免打断导致丢伤害。
        /// </summary>
        public bool PlayAttack(System.Action onImpact = null)
        {
            if (!ready || targetRenderer == null)
            {
                return false;
            }

            if (attackFrames == null || attackFrames.Length == 0
                || attackTimeline == null || attackTimeline.Length == 0)
            {
                Debug.LogWarning("[剑士动画] 没有加载到挥剑帧，请确认 Assets/Resources/" + ResourceFolder
                                 + "/Attack_*.png 已导入为 Sprite。", this);
                return false;
            }

            if (actionRoutine != null)
            {
                // 上一次动作被这里打断：若它还没到命中帧，立即补结算，避免伤害凭空消失。
                FireImpact();
                StopCoroutine(actionRoutine);
            }

            pendingImpact = onImpact;
            impactFired = false;
            dashing = false;
            actionRoutine = StartCoroutine(AttackSequence());
            return true;
        }

        /// <summary>
        /// 播放突刺分镜；与挥剑共用动作锁，避免两段序列帧互相覆盖。
        /// onImpact 在刀光爆发帧（dashImpactFrameIndex）触发一次，语义同 <see cref="PlayAttack"/>。
        /// </summary>
        public bool PlayDash(System.Action onImpact = null)
        {
            if (!ready || targetRenderer == null)
            {
                return false;
            }

            if (dashFrames == null || dashFrames.Length == 0
                || dashTimeline == null || dashTimeline.Length == 0)
            {
                Debug.LogWarning("[剑士动画] 没有加载到突刺帧，请确认 Assets/Resources/" + ResourceFolder
                                 + "/Dash_*.png 已导入为 Sprite。", this);
                return false;
            }

            if (actionRoutine != null)
            {
                FireImpact();
                StopCoroutine(actionRoutine);
            }

            pendingImpact = onImpact;
            impactFired = false;
            dashing = true;
            actionRoutine = StartCoroutine(DashSequence());
            return true;
        }

        /// <summary>
        /// 外部重建 Visual 之后调用（例如角色被提升为玩家时重建外观），
        /// 重新套用当前状态帧、缩放与脚底对齐。
        /// </summary>
        public void RefreshVisual()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
                if (targetRenderer == null)
                {
                    targetRenderer = GetComponentInChildren<SpriteRenderer>();
                }
            }

            if (!ready)
            {
                SetUp();
                return;
            }

            if (actionRoutine == null)
            {
                ApplyCurrentState();
            }
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
            BuildAttackTimeline();
            BuildDashTimeline();
            ready = true;

            if (targetRenderer == null)
            {
                Debug.LogWarning("[剑士动画] 没有找到 SpriteRenderer，序列帧无处渲染。", this);
                return;
            }

            ApplyCurrentState();
        }

        private void ApplyCurrentState()
        {
            // 静止时用挥剑序列的起手帧（持刀站立），比跑步第一帧更适合当待机。
            if (idleFrames != null && idleFrames.Length > 0)
            {
                ApplyFrame(idleFrames[0], attackFeetOffsetPixels);
                return;
            }

            if (runFrames != null && runFrames.Length > 0)
            {
                ApplyFrame(runFrames[0], runFeetOffsetPixels);
            }
        }

        private void ApplyFrame(Sprite sprite, float feetOffsetPixels)
        {
            if (targetRenderer == null || sprite == null)
            {
                return;
            }

            if (targetRenderer.sprite != sprite)
            {
                targetRenderer.sprite = sprite;
            }

            float scale = Mathf.Max(0.01f, visualScale);
            float feetWorld = feetOffsetPixels / PixelsPerUnit * scale;

            // 脚底锚定：Sprite 中心上移「脚底到中心的距离」，脚底即落在 transform 原点。
            transform.localScale = new Vector3(scale, scale, 1f);
            transform.localPosition = new Vector3(0f, feetWorld, 0f);
        }

        private System.Collections.IEnumerator AttackSequence()
        {
            float frameTime = 1f / Mathf.Max(0.1f, attackFramesPerSecond);

            for (int i = 0; i < attackTimeline.Length; i++)
            {
                ApplyFrame(attackFrames[attackTimeline[i]], attackFeetOffsetPixels);
                if (attackTimeline[i] == impactFrameIndex)
                {
                    FireImpact();
                }

                yield return new WaitForSeconds(frameTime);
            }

            // 兜底：时间轴里若没有命中帧（参数被改坏），也要保证结算不被吞掉。
            FireImpact();
            actionRoutine = null;
            dashing = false;
            ResumeLocomotion();
        }

        private System.Collections.IEnumerator DashSequence()
        {
            float frameTime = 1f / Mathf.Max(0.1f, dashFramesPerSecond);

            for (int i = 0; i < dashTimeline.Length; i++)
            {
                ApplyFrame(dashFrames[dashTimeline[i]], dashFeetOffsetPixels);
                if (dashTimeline[i] == dashImpactFrameIndex)
                {
                    FireImpact();
                }

                yield return new WaitForSeconds(frameTime);
            }

            FireImpact();
            actionRoutine = null;
            dashing = false;
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
            System.Action callback = pendingImpact;
            pendingImpact = null;
            if (callback != null)
            {
                callback();
            }
        }

        /// <summary>单次动作结束后接回当前移动/待机状态，避免 Update 接手前闪一帧。</summary>
        private void ResumeLocomotion()
        {
            if (moving && runFrames != null && runFrames.Length > 0)
            {
                runTimer = 0f;
                runIndex = 0;
                ApplyFrame(runFrames[0], runFeetOffsetPixels);
                return;
            }

            ApplyCurrentState();
        }

        private void LoadFrames()
        {
            // Resources.LoadAll 只接受「文件夹」路径，不做前缀匹配，
            // 因此这里一次性取整包，再按名字前缀分组（原实现传 "Swordsman/Run_" 会拿到空数组）。
            Sprite[] all = Resources.LoadAll<Sprite>(ResourceFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[剑士动画] Assets/Resources/" + ResourceFolder
                                 + " 下没有找到任何 Sprite，角色将保持不可见。", this);
                runFrames = new Sprite[0];
                attackFrames = new Sprite[0];
                dashFrames = new Sprite[0];
                idleFrames = new Sprite[0];
                return;
            }

            runFrames = FilterAndSort(all, RunPrefix);
            attackFrames = FilterAndSort(all, AttackPrefix);
            dashFrames = FilterAndSort(all, DashPrefix);

            if (attackFrames.Length > 0)
            {
                idleFrames = new[] { attackFrames[0] };
            }
            else if (runFrames.Length > 0)
            {
                idleFrames = new[] { runFrames[0] };
            }
            else
            {
                idleFrames = new Sprite[0];
            }

            if (runFrames.Length == 0)
            {
                Debug.LogWarning("[剑士动画] 没有找到 " + RunPrefix + "* 帧，跑步动作将不会播放。", this);
            }

            if (attackFrames.Length == 0)
            {
                Debug.LogWarning("[剑士动画] 没有找到 " + AttackPrefix + "* 帧，挥剑动作将不会播放。", this);
            }

            if (dashFrames.Length == 0)
            {
                Debug.LogWarning("[剑士动画] 没有找到 " + DashPrefix + "* 帧，突刺动作将不会播放。", this);
            }
        }

        private static Sprite[] FilterAndSort(Sprite[] all, string prefix)
        {
            var list = new List<Sprite>();
            for (int i = 0; i < all.Length; i++)
            {
                Sprite sprite = all[i];
                if (sprite != null && sprite.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    list.Add(sprite);
                }
            }

            // 文件名都是两位数补零（Attack_00…、Run_00…），Ordinal 排序即帧序。
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list.ToArray();
        }

        private void BuildAttackTimeline()
        {
            if (attackFrames == null || attackFrames.Length == 0)
            {
                attackTimeline = new int[0];
                return;
            }

            var timeline = new List<int>(attackFrames.Length + 2);
            for (int i = 0; i < attackFrames.Length; i++)
            {
                timeline.Add(i);

                int extra = 0;
                if (i == windUpFrameIndex)
                {
                    extra += windUpHoldFrames;
                }

                if (i == impactFrameIndex)
                {
                    extra += impactHoldFrames;
                }

                for (int repeat = 0; repeat < extra; repeat++)
                {
                    timeline.Add(i);
                }
            }

            attackTimeline = timeline.ToArray();
        }

        private void BuildDashTimeline()
        {
            if (dashFrames == null || dashFrames.Length == 0)
            {
                dashTimeline = new int[0];
                return;
            }

            var timeline = new List<int>(dashFrames.Length + Mathf.Max(0, dashImpactHoldFrames));
            for (int i = 0; i < dashFrames.Length; i++)
            {
                timeline.Add(i);

                if (i == dashImpactFrameIndex)
                {
                    for (int repeat = 0; repeat < dashImpactHoldFrames; repeat++)
                    {
                        timeline.Add(i);
                    }
                }
            }

            dashTimeline = timeline.ToArray();
        }
    }
}
