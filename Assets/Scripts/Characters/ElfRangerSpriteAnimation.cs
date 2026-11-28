using System;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace Ring.Characters
{
    /// <summary>
    /// 精灵游侠序列帧表现：Idle / Run / Attack（短弓）/ Heavy（破甲重箭）/ Heal（自然恩赐）
    /// / Jump / DodgeFwd / DodgeBack，另有「光环」叠加层与「缚地荆棘」地形表现。
    ///
    /// 素材来自 Assets/Resources/ElfRanger（由 .tools/elf_ranger/build_frames.py 生成）：
    /// 源图是白底 RGB 稿，脚本做了白底转 alpha、去白污染、边缘外扩与统一画布：
    /// 全部角色帧画布 800×800、脚底基线 y=799（与画布中心相距 399.5 px）、PPU = 292，
    /// 因此人物高度恒为 583 / 292 ≈ 2.0 世界单位，再乘 <see cref="visualScale"/> 对齐其他人设。
    ///
    /// 帧序与 Assets/Animations/ElfRanger 下 13 个 .anim 剪辑一一对应：
    /// Idle 6f@8 / Run 6f@12 / Attack 10f@20（撒放帧 6 = 0.30s）
    /// / Heavy 12f@18（撒放帧 7 = 0.389s）/ Heal 12f@15 / Jump 6f@12
    /// / DodgeFwd、DodgeBack 6f@15 / Halo 8f@14 循环
    /// / BrambleGrow 8f@16 → BrambleIdle 6f@8 循环 → BrambleFade 3f@10 / Arrow 12f@30 循环。
    ///
    /// 伤害与弹道由 AbilityCaster 以回调传入，在「撒放帧」触发；动作被打断时兜底补发一次，
    /// 保证连点普攻不会因为协程被 StopCoroutine 而凭空丢箭。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/精灵游侠序列帧 ElfRangerSpriteAnimation")]
    public sealed class ElfRangerSpriteAnimation : MonoBehaviour
    {
        private const string ResourceFolder = "ElfRanger";

        /// <summary>角色帧与光环帧共用的导入 PPU（见 gen_meta.py）。</summary>
        private const float PixelsPerUnit = 292f;

        [Header("渲染目标")]
        [Tooltip("留空时自动取本物体（或子物体）上的 SpriteRenderer。")]
        [SerializeField] private SpriteRenderer targetRenderer;

        [Header("帧率（与 .anim 剪辑一致）")]
        [SerializeField, Min(1f)] private float idleFps = 8f;
        [SerializeField, Min(1f)] private float runFps = 12f;
        [Tooltip("短弓 10 帧 20fps = 0.5s；撒放帧 6 = 0.30s。")]
        [SerializeField, Min(1f)] private float attackFps = 20f;
        [Tooltip("重箭 12 帧 18fps = 0.667s；撒放帧 7 = 0.389s。")]
        [SerializeField, Min(1f)] private float heavyFps = 18f;
        [Tooltip("自然恩赐 12 帧 15fps = 0.8s；第 4 帧起光晕铺满。")]
        [SerializeField, Min(1f)] private float healFps = 15f;
        [SerializeField, Min(1f)] private float jumpFps = 12f;
        [SerializeField, Min(1f)] private float dodgeFps = 15f;
        [SerializeField, Min(1f)] private float haloFps = 14f;

        [Header("脚底对齐（像素）")]
        [Tooltip("全部角色帧共用 800×800 画布，脚底在 y=799，画布中心 y=400，故偏移恒为 399.5。")]
        [SerializeField] private float feetOffsetPixels = 399.5f;

        [Header("显示尺寸")]
        [Tooltip("PPU 292 下人物高 2.0 世界单位；0.7 后约 1.4，与剑士 1.43 / 斯巴达 1.33 同量级。")]
        [SerializeField, Min(0.01f)] private float visualScale = 0.7f;

        [Header("命中帧（与 .anim 一致）")]
        [Tooltip("短弓撒放帧；6 / 20fps = 0.30s。")]
        [SerializeField, Min(0)] private int attackImpactFrameIndex = 6;
        [Tooltip("重箭撒放帧；7 / 18fps = 0.389s。")]
        [SerializeField, Min(0)] private int heavyImpactFrameIndex = 7;
        [Tooltip("自然恩赐光晕最亮帧；4 / 15fps ≈ 0.267s。")]
        [SerializeField, Min(0)] private int healImpactFrameIndex = 4;

        [Header("AnimationClip 绑定（Inspector 拖入）")]
        [Tooltip("把 Assets/Animations/ElfRanger/ 下的 13 个 .anim 拖到这里完成显式绑定与一致性校验。运行时切帧读 Resources/ElfRanger，字段不参与播放；改用 Animator 的步骤见 Docs/18 第七节。")]
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip runClip;
        [SerializeField] private AnimationClip attackClip;
        [SerializeField] private AnimationClip heavyClip;
        [SerializeField] private AnimationClip healClip;
        [SerializeField] private AnimationClip jumpClip;
        [SerializeField] private AnimationClip dodgeFwdClip;
        [SerializeField] private AnimationClip dodgeBackClip;
        [SerializeField] private AnimationClip haloClip;
        [SerializeField] private AnimationClip brambleGrowClip;
        [SerializeField] private AnimationClip brambleIdleClip;
        [SerializeField] private AnimationClip brambleFadeClip;
        [SerializeField] private AnimationClip arrowClip;

        private Sprite[] idle, run, attack, heavy, heal, jump, dodgeFwd, dodgeBack, halo;

        private SpriteRenderer haloRenderer;
        private Coroutine actionRoutine;
        private Coroutine haloRoutine;
        private Action pendingImpact;
        private bool impactFired;
        private bool moving;
        private bool ready;
        private float runTimer;
        private int runIndex;
        private string activeAction = string.Empty;

        /// <summary>当前实际渲染器，供外部取 flipX / sortingOrder。</summary>
        public SpriteRenderer Renderer => targetRenderer;

        public bool IsActing => actionRoutine != null;
        public string ActiveAction => activeAction;
        public bool IsMoving => moving;
        public bool IsHaloActive => haloRoutine != null;

        /// <summary>13 个 AnimationClip 是否都已在 Inspector 绑定（仅状态查询，不影响运行时切帧）。</summary>
        public bool ClipsBound => idleClip != null && runClip != null && attackClip != null
                                  && heavyClip != null && healClip != null && jumpClip != null
                                  && dodgeFwdClip != null && dodgeBackClip != null && haloClip != null
                                  && brambleGrowClip != null && brambleIdleClip != null
                                  && brambleFadeClip != null && arrowClip != null;

        public string DescribeClipBindings()
        {
            return "Idle=" + ClipName(idleClip) + " / Run=" + ClipName(runClip)
                 + " / Attack=" + ClipName(attackClip) + " / Heavy=" + ClipName(heavyClip)
                 + " / Heal=" + ClipName(healClip) + " / Jump=" + ClipName(jumpClip)
                 + " / DodgeFwd=" + ClipName(dodgeFwdClip) + " / DodgeBack=" + ClipName(dodgeBackClip)
                 + " / Halo=" + ClipName(haloClip) + " / Bramble=" + ClipName(brambleGrowClip)
                 + "," + ClipName(brambleIdleClip) + "," + ClipName(brambleFadeClip)
                 + " / Arrow=" + ClipName(arrowClip);
        }

        [ContextMenu("校验 AnimationClip 绑定")]
        private void LogClipBindings()
        {
            Debug.Log("[游侠动画] " + DescribeClipBindings(), this);
            ValidateClipBindings(true);
        }

        private static string ClipName(AnimationClip clip) => clip != null ? clip.name : "未绑定";

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

            ApplyFrame(run[runIndex]);
        }

        /// <summary>由 PlayerMotor 调用：true 在移动（循环跑动帧），false 回到待机。</summary>
        public void SetMoving(bool value)
        {
            if (moving == value)
            {
                return;
            }

            moving = value;
            runTimer = 0f;
            runIndex = 0;

            if (actionRoutine == null)
            {
                ResumeLocomotion();
            }
        }

        /// <summary>普攻：短弓。impact 在撒放帧（默认第 6 帧 = 0.30s）触发一次。</summary>
        public bool PlayAttack(Action impact)
        {
            int frames = attack != null ? attack.Length : 0;
            int impactFrame = frames > 0 ? Mathf.Min(attackImpactFrameIndex, frames - 1) : 0;
            return Play(attack, impact, impactFrame, attackFps, "attack");
        }

        /// <summary>本我：破甲重箭。impact 在撒放帧（默认第 7 帧 = 0.389s）触发一次。</summary>
        public bool PlayHeavy(Action impact)
        {
            int frames = heavy != null ? heavy.Length : 0;
            int impactFrame = frames > 0 ? Mathf.Min(heavyImpactFrameIndex, frames - 1) : 0;
            return Play(heavy, impact, impactFrame, heavyFps, "heavy");
        }

        /// <summary>
        /// 自我：自然恩赐。播放治疗分镜，并在光晕帧同时点亮 <paramref name="haloSeconds"/> 秒光环。
        /// 治疗数值由 AbilityRuntime 在施法时结算，这里只负责表现。
        /// </summary>
        public bool PlayHeal(float haloSeconds = 1.5f, Action impact = null)
        {
            int frames = heal != null ? heal.Length : 0;
            int impactFrame = frames > 0 ? Mathf.Min(healImpactFrameIndex, frames - 1) : 0;
            Action combined = () =>
            {
                PlayHalo(haloSeconds);
                impact?.Invoke();
            };
            return Play(heal, combined, impactFrame, healFps, "heal");
        }

        /// <summary>空格起跳：6 帧 12fps = 0.5s，与 CombatBody 滞空时长同量级。</summary>
        public bool PlayJump()
        {
            if (actionRoutine != null && activeAction == "jump")
            {
                // 幂等：显式绑定与兜底轮询可能同时调用，重复播放会让分镜卡在第一帧。
                return true;
            }

            return Play(jump, null, int.MaxValue, jumpFps, "jump");
        }

        /// <summary>闪避：forward 播前闪（DodgeFwd），否则后闪（DodgeBack）。</summary>
        public bool PlayDodge(bool forward)
        {
            Sprite[] frames = forward ? dodgeFwd : dodgeBack;
            return Play(frames, null, int.MaxValue, dodgeFps, forward ? "dodgeFwd" : "dodgeBack");
        }

        /// <summary>点亮光环叠加层 <paramref name="seconds"/> 秒（8 帧 14fps 循环）。</summary>
        public void PlayHalo(float seconds)
        {
            if (!ready || halo == null || halo.Length == 0)
            {
                return;
            }

            EnsureHaloRenderer();
            if (haloRenderer == null)
            {
                return;
            }

            if (haloRoutine != null)
            {
                StopCoroutine(haloRoutine);
            }

            haloRoutine = StartCoroutine(HaloSequence(Mathf.Max(0f, seconds)));
        }

        public void StopHalo()
        {
            if (haloRoutine != null)
            {
                StopCoroutine(haloRoutine);
                haloRoutine = null;
            }

            if (haloRenderer != null)
            {
                haloRenderer.enabled = false;
            }
        }

        /// <summary>外部重建 Visual 后重新绑定渲染器并套用当前状态帧。</summary>
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

            EnsureHaloRenderer();

            if (actionRoutine == null)
            {
                ResumeLocomotion();
            }
        }

        private bool Play(Sprite[] frames, Action impact, int impactFrame, float fps, string kind)
        {
            if (!ready || targetRenderer == null || frames == null || frames.Length == 0)
            {
                return false;
            }

            Interrupt();

            pendingImpact = impact;
            impactFired = false;
            activeAction = kind;
            actionRoutine = StartCoroutine(Sequence(frames, impactFrame, fps));
            return true;
        }

        /// <summary>打断当前单次动作：先补发未触发的回调，再停协程。</summary>
        private void Interrupt()
        {
            if (actionRoutine == null)
            {
                return;
            }

            FireImpact();
            StopCoroutine(actionRoutine);
            actionRoutine = null;
            activeAction = string.Empty;
        }

        private IEnumerator Sequence(Sprite[] frames, int impactFrame, float fps)
        {
            float frameTime = 1f / Mathf.Max(1f, fps);

            for (int i = 0; i < frames.Length; i++)
            {
                ApplyFrame(frames[i]);
                if (i >= impactFrame)
                {
                    FireImpact();
                }

                yield return new WaitForSeconds(frameTime);
            }

            FireImpact();
            actionRoutine = null;
            activeAction = string.Empty;
            ResumeLocomotion();
        }

        private IEnumerator HaloSequence(float seconds)
        {
            haloRenderer.enabled = true;
            float frameTime = 1f / Mathf.Max(1f, haloFps);
            float elapsed = 0f;
            int index = 0;

            while (elapsed < seconds)
            {
                haloRenderer.sprite = halo[index];
                index = (index + 1) % halo.Length;
                elapsed += frameTime;
                yield return new WaitForSeconds(frameTime);
            }

            haloRenderer.enabled = false;
            haloRoutine = null;
        }

        /// <summary>触发本次动作的结算，只触发一次（命中帧 / 播完 / 被打断三处兜底）。</summary>
        private void FireImpact()
        {
            if (impactFired)
            {
                return;
            }

            impactFired = true;
            Action callback = pendingImpact;
            pendingImpact = null;
            callback?.Invoke();
        }

        private void ResumeLocomotion()
        {
            runTimer = 0f;
            runIndex = 0;

            if (moving && run != null && run.Length > 0)
            {
                ApplyFrame(run[0]);
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
                ApplyFrame(idle[0]);
            }
        }

        /// <summary>
        /// 换人设时本组件会被销毁，但光环是 Visual 下的子物体、会随 Visual 一起保留。
        /// 必须一并清掉：残留的 SpriteRenderer 会被下一个动画组件当作渲染目标取错。
        /// </summary>
        private void OnDestroy()
        {
            if (haloRoutine != null)
            {
                StopCoroutine(haloRoutine);
                haloRoutine = null;
            }

            if (haloRenderer != null)
            {
                GameObject haloObject = haloRenderer.gameObject;
                haloRenderer = null;
                if (Application.isPlaying)
                {
                    Destroy(haloObject);
                }
                else
                {
                    DestroyImmediate(haloObject);
                }
            }
        }

        private void ApplyFrame(Sprite sprite)
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

            // 脚底锚定：Sprite 中心上移「脚底到中心」的距离，脚底恒在 transform 原点。
            transform.localScale = new Vector3(scale, scale, 1f);
            transform.localPosition = new Vector3(0f, feetWorld, 0f);
        }

        private void EnsureHaloRenderer()
        {
            if (haloRenderer != null || halo == null || halo.Length == 0)
            {
                return;
            }

            Transform existing = transform.Find("Halo");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("Halo");
                go.transform.SetParent(transform, false);
            }

            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one;

            haloRenderer = go.GetComponent<SpriteRenderer>();
            if (haloRenderer == null)
            {
                haloRenderer = go.AddComponent<SpriteRenderer>();
            }

            // 画笔顺序压在角色之上，色彩由贴图自身 alpha 决定。
            haloRenderer.sortingOrder = (targetRenderer != null ? targetRenderer.sortingOrder : 10) + 1;
            haloRenderer.color = Color.white;
            haloRenderer.sprite = halo[0];
            haloRenderer.enabled = false;
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
                Debug.LogWarning("[游侠动画] 没有找到 SpriteRenderer，序列帧无处渲染。", this);
                return;
            }

            ApplyIdle();
            EnsureHaloRenderer();
            ValidateClipBindings(false);
        }

        private void ValidateClipBindings(bool verbose)
        {
            CheckClip("Idle", idleClip, idle, verbose, idleFps);
            CheckClip("Run", runClip, run, verbose, runFps);
            CheckClip("Attack", attackClip, attack, verbose, attackFps);
            CheckClip("Heavy", heavyClip, heavy, verbose, heavyFps);
            CheckClip("Heal", healClip, heal, verbose, healFps);
            CheckClip("Jump", jumpClip, jump, verbose, jumpFps);
            CheckClip("DodgeFwd", dodgeFwdClip, dodgeFwd, verbose, dodgeFps);
            CheckClip("DodgeBack", dodgeBackClip, dodgeBack, verbose, dodgeFps);
            CheckClip("Halo", haloClip, halo, verbose, haloFps);

            if (verbose && !ClipsBound)
            {
                Debug.LogWarning("[游侠动画] 13 个 AnimationClip 尚未全部绑定：" + DescribeClipBindings()
                                 + "。请在 Inspector 把 Assets/Animations/ElfRanger/*.anim 拖入对应字段。", this);
            }
        }

        private void CheckClip(string label, AnimationClip clip, Sprite[] frames, bool verbose, float fps)
        {
            if (clip == null)
            {
                return;
            }

            int expected = frames != null ? frames.Length : 0;
            int actual = Mathf.Max(1, Mathf.RoundToInt(clip.length * Mathf.Max(1f, clip.frameRate)));
            if (expected > 0 && actual != expected)
            {
                Debug.LogWarning("[游侠动画] " + label + " 剪辑帧数 " + actual
                                 + " 与 Resources 帧数 " + expected + " 不一致。", this);
            }
            else if (verbose)
            {
                Debug.Log("[游侠动画] " + label + " 剪辑 " + actual + " 帧 @ "
                          + Mathf.Max(1f, clip.frameRate) + "fps；运行时节拍 " + fps + "fps。", this);
            }
        }

        private void LoadFrames()
        {
            Sprite[] all = Resources.LoadAll<Sprite>(ResourceFolder);
            if (all == null || all.Length == 0)
            {
                Debug.LogWarning("[游侠动画] Assets/Resources/" + ResourceFolder
                                 + " 下没有找到任何 Sprite，角色将保持不可见。", this);
                idle = run = attack = heavy = heal = jump = dodgeFwd = dodgeBack = halo = Array.Empty<Sprite>();
                return;
            }

            idle = Filter(all, "Idle_");
            run = Filter(all, "Run_");
            attack = Filter(all, "Attack_");
            heavy = Filter(all, "Heavy_");
            heal = Filter(all, "Heal_");
            jump = Filter(all, "Jump_");
            dodgeFwd = Filter(all, "DodgeFwd_");
            dodgeBack = Filter(all, "DodgeBack_");
            halo = Filter(all, "Halo_");

            if (idle.Length == 0) Debug.LogWarning("[游侠动画] 没有找到 Idle_* 帧。", this);
            if (run.Length == 0) Debug.LogWarning("[游侠动画] 没有找到 Run_* 帧。", this);
            if (attack.Length == 0) Debug.LogWarning("[游侠动画] 没有找到 Attack_* 帧。", this);
            if (heavy.Length == 0) Debug.LogWarning("[游侠动画] 没有找到 Heavy_* 帧。", this);
            if (heal.Length == 0) Debug.LogWarning("[游侠动画] 没有找到 Heal_* 帧。", this);
        }

        private static Sprite[] Filter(Sprite[] all, string suffixPrefix)
        {
            // 全部帧都在同一个 Resources 目录，文件名形如 ElfRanger_Attack_03，
            // 因此前缀要带上 ElfRanger_ 才不会把 Heal 与 Halo 之类的分组搞混。
            string prefix = ResourceFolder + "_" + suffixPrefix;
            return all
                .Where(s => s != null && s.name.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(s => s.name, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
