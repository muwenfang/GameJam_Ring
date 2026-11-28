using System.Collections.Generic;
using System.Text;
using Ring.Abilities;
using Ring.Bells;
using Ring.Characters;
using Ring.Flow;
using Ring.Maps;
using UnityEngine;
using UnityEngine.UI;

namespace Ring.UI
{
    /// <summary>「对手」这一侧的取人方式。</summary>
    public enum HudOpponentPick
    {
        /// <summary>离玩家最近的在场合格者（默认，混战时最符合直觉）。</summary>
        Nearest = 0,

        /// <summary>按生成顺序第一个还活着的非玩家。</summary>
        FirstAlive = 1,

        /// <summary>固定 id（调试用；盯死某个人机看数据）。</summary>
        FixedId = 2,
    }

    /// <summary>
    /// <see cref="BattleHud"/> 的零件引用。用结构体集中传参，避免十几参的构造函数。
    /// 全部可以由用户在场景里手工搭好后拖进来；留空则按名字自动找。
    /// </summary>
    public struct BattleHudParts
    {
        public Text playerName;
        public Image playerBar;
        public Image playerBarBorder;
        public Image playerBarFill;
        public Text playerBarValue;
        public Image playerTraitBox;
        public RectTransform playerTraitRow;
        public Text playerTraitEmpty;

        public Text opponentName;
        public Image opponentBar;
        public Image opponentBarBorder;
        public Image opponentBarFill;
        public Text opponentBarValue;
        public Image opponentTraitBox;
        public RectTransform opponentTraitRow;
        public Text opponentTraitEmpty;

        public Image bellBadge;
        public Image bellBadgeBorder;
        public Text bellValue;
        public Text roundValue;
        public Text detailLabel;
    }

    /// <summary>
    /// 战斗 HUD：玩家 / 对手血条 + 双方「五个能力槽」 + 中央钟声倒计时与回合数。
    ///
    /// 设计约定（与设计稿一致）：
    /// - 左右各一块：名字在血条上方、血条居中、血条下方是虚线「技能槽」框，固定 5 格，
    ///   按 <see cref="AbilitySlots.All"/> 的顺序显示 **特质 / 普攻 / 本我 / 自我 / 超我**
    ///   各自装的是**哪个人设**（例：`特质 剑客`、`本我 钟表师`），空格只显示槽位名；
    /// - 中间一块：圆角底牌里是「距离下一次钟声还有几秒」，下面一行是「回合 X / Y」。
    ///
    /// 三层分工：
    /// - 本组件只管读（GameFlow → CharacterInstance.Loadout → 五个槽的人设名）和摆字，
    ///   不改钟声规则、不改数值、不生成角色；
    /// - 位置与尺寸全部来自 RectTransform，用户在场景里怎么摆就怎么显示；
    /// - 圆角 / 虚线贴图在运行时按实际像素尺寸生成（见 <see cref="HudSprites"/>），
    ///   用户自己给 Image 指定了贴图就不会被覆盖。
    ///
    /// 接法：场景里手搭 HUD 后把零件拖进 Inspector；或者用菜单「钟声 / 界面 / 生成战斗 HUD（场景）」生成一套再改。
    /// 场景里没有 HUD 时，<see cref="GameUiController"/> 会在运行时用 <see cref="BattleHudBuilder"/> 兜底搭一套。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("钟声/界面 战斗 HUD")]
    public sealed class BattleHud : MonoBehaviour
    {
        // ---------- 名字约定：留空的引用按这些名字在子物体里找 ----------
        // 槽位行保留了旧名 TraitRow，同时接受新名 SkillRow（两种都能自动找到）。
        private const string PlayerNameName = "PlayerName";
        private const string PlayerBarName = "PlayerBar";
        private const string PlayerBarBorderName = "PlayerBarBorder";
        private const string PlayerBarFillName = "PlayerBarFill";
        private const string PlayerBarValueName = "PlayerBarValue";
        private const string PlayerTraitBoxName = "PlayerTraitBox";
        private const string PlayerTraitRowName = "PlayerTraitRow";
        private const string PlayerSlotRowAliasName = "PlayerSkillRow";
        private const string PlayerTraitEmptyName = "PlayerTraitEmpty";

        private const string OpponentNameName = "OpponentName";
        private const string OpponentBarName = "OpponentBar";
        private const string OpponentBarBorderName = "OpponentBarBorder";
        private const string OpponentBarFillName = "OpponentBarFill";
        private const string OpponentBarValueName = "OpponentBarValue";
        private const string OpponentTraitBoxName = "OpponentTraitBox";
        private const string OpponentTraitRowName = "OpponentTraitRow";
        private const string OpponentSlotRowAliasName = "OpponentSkillRow";
        private const string OpponentTraitEmptyName = "OpponentTraitEmpty";

        private const string BellBadgeName = "BellBadge";
        private const string BellBadgeBorderName = "BellBadgeBorder";
        private const string BellValueName = "BellValue";
        private const string RoundValueName = "RoundValue";
        private const string DetailName = "Detail";

        [Header("数据来源")]
        [SerializeField]
        [Tooltip("游戏流程；留空时自动取 GameFlow.Instance。")]
        private GameFlow gameFlow;

        [SerializeField]
        [Tooltip("是否优先把「本场 1v1 的对手」显示在右侧；关掉则用下面的取人规则（调试旧行为时用）。")]
        private bool preferPairedOpponent = true;

        [SerializeField]
        [Tooltip("右侧「对手」取谁：离玩家最近 / 第一个活着的 / 固定 id。preferPairedOpponent 打开时先按本场配对取。")]
        private HudOpponentPick opponentPick = HudOpponentPick.Nearest;

        [SerializeField]
        [Tooltip("opponentPick = FixedId 时盯住的人机 id。")]
        private int fixedOpponentId = 1;

        [Header("文案")]
        [SerializeField]
        [Tooltip("一个槽都没到手时的占位文案（设计稿写的是「状态栏」）；正常对局里 5 个槽位总会显示槽位名。")]
        private string traitBoxEmptyText = "状态栏";


        [SerializeField]
        [Tooltip("血条上的数值格式：{0}=当前生命，{1}=最大生命。")]
        private string healthFormat = "{0} / {1}";

        [SerializeField]
        [Tooltip("是否在血条中间显示数字。")]
        private bool showHealthNumbers = true;

        [SerializeField]
        [Tooltip("左下角是否显示明细（阶段 / 在场人数 / 技能冷却）。默认关，开出来当调试信息用。")]
        private bool showDetails;

        [Header("引用：玩家一侧（留空按名字自动找；槽位行也叫 PlayerSkillRow）")]
        [SerializeField] private Text playerName;
        [SerializeField] private Image playerBar;
        [SerializeField] private Image playerBarBorder;
        [SerializeField] private Image playerBarFill;
        [SerializeField] private Text playerBarValue;
        [SerializeField] private Image playerTraitBox;
        [SerializeField] private RectTransform playerTraitRow;
        [SerializeField] private Text playerTraitEmpty;

        [Header("引用：对手一侧（留空按名字自动找；槽位行也叫 OpponentSkillRow）")]
        [SerializeField] private Text opponentName;
        [SerializeField] private Image opponentBar;
        [SerializeField] private Image opponentBarBorder;
        [SerializeField] private Image opponentBarFill;
        [SerializeField] private Text opponentBarValue;
        [SerializeField] private Image opponentTraitBox;
        [SerializeField] private RectTransform opponentTraitRow;
        [SerializeField] private Text opponentTraitEmpty;

        [Header("引用：中间与附属")]
        [SerializeField] private Image bellBadge;
        [SerializeField] private Image bellBadgeBorder;
        [SerializeField] private Text bellValue;
        [SerializeField] private Text roundValue;
        [SerializeField] private Text detailLabel;

        [Header("布局：五个能力槽")]
        [SerializeField]
        [Tooltip("补建槽格时的兜底宽度。手搭的槽格够 5 个时完全不看这里，新格子还会沿用相邻格子的实际尺寸。")]
        private float traitSlotWidth = 112f;

        [SerializeField]
        [Tooltip("补建「单行式」槽格时的兜底高度（与手搭格子同风格）。")]
        private float traitSlotHeight = 32f;

        [SerializeField]
        [Tooltip("补建「单行式」槽格时的字号（`特质 剑客` 一行显示）。")]
        private int traitSlotFontSize = 17;

        [SerializeField] private float traitSlotSpacing = 8f;

        [SerializeField]
        [Tooltip("补建「两行式」槽格时的兜底高度（上行槽位名、下行人设名）。")]
        private float slotChipHeight = 58f;

        [SerializeField]
        [Tooltip("两行式槽格的上行（槽位名）字号。")]
        private int slotCaptionFontSize = 13;

        [SerializeField]
        [Tooltip("两行式槽格的下行（人设名）字号。")]
        private int slotValueFontSize = 20;

        [SerializeField]
        [Tooltip("槽格文字超宽时自动缩字号（以你在场景里设的字号为上限）。")]
        private bool fitSlotText = true;

        [SerializeField] private int fitSlotTextMinFontSize = 11;

        [Header("文案：技能槽")]
        [SerializeField]
        [Tooltip("两行式槽格「没人设」时下行显示的占位文案；单行式格子只显示槽位名。")]
        private string emptySlotValue = "—";

        [Header("外观")]
        [SerializeField]
        [Tooltip("运行时按实际像素尺寸生成圆角 / 虚线贴图；关掉则完全用你在 Inspector 里指定的贴图。")]
        private bool autoGenerateSprites = true;

        [SerializeField] private float cornerRadius = 8f;
        [SerializeField] private float badgeCornerRadius = 14f;
        [SerializeField] private float borderThickness = 2f;
        [SerializeField] private float barInset = 3f;
        [SerializeField] private float dashPeriod = 12f;
        [SerializeField] private float dashRatio = 0.55f;

        [Header("外观：颜色")]
        [SerializeField] private Color healthTrackColor = new Color(0.9f, 0.92f, 0.95f, 0.94f);
        [SerializeField] private Color borderColor = new Color(0.14f, 0.19f, 0.29f, 1f);
        [SerializeField] private Color playerHealthColor = new Color(0.28f, 0.75f, 0.51f, 1f);
        [SerializeField] private Color opponentHealthColor = new Color(0.88f, 0.35f, 0.31f, 1f);
        [SerializeField] private Color healthTextColor = Color.white;
        [SerializeField] private Color nameColor = new Color(0.96f, 0.97f, 0.99f, 1f);
        [SerializeField] private Color textOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        [SerializeField] private Color badgeFillColor = new Color(0.93f, 0.94f, 0.96f, 1f);
        [SerializeField] private Color badgeTextColor = new Color(0.10f, 0.14f, 0.22f, 1f);
        [SerializeField] private Color roundColor = new Color(0.9f, 0.92f, 0.96f, 1f);
        [SerializeField] private Color traitBoxBorderColor = new Color(0.85f, 0.88f, 0.93f, 0.55f);
        [SerializeField] private Color completeSetColor = new Color(0.92f, 0.80f, 0.42f, 0.95f);
        [SerializeField] private Color traitChipTextColor = new Color(0.10f, 0.13f, 0.17f, 1f);
        [SerializeField] private Color emptySlotFillColor = new Color(0.55f, 0.60f, 0.68f, 0.22f);
        [SerializeField] private Color emptySlotTextColor = new Color(0.80f, 0.84f, 0.90f, 0.55f);
        [SerializeField] private Color warnColor = new Color(0.94f, 0.42f, 0.32f, 1f);

        [SerializeField]
        [Tooltip("倒计时进入这个秒数时数字变色（0 表示不变色）。")]
        private int countdownWarnSeconds = 5;

        [SerializeField]
        [Tooltip("血条追赶速度（每秒填满的比例），数值越大越硬。")]
        private float healthSmoothing = 2.5f;

        // ---------- 运行时 ----------
        /// <summary>一排技能槽的运行时引用。frames / labels / captions 三个列表按下标一一对应。</summary>
        private sealed class SlotRowView
        {
            public Image box;
            public RectTransform row;
            public Text placeholder;

            /// <summary>槽格底色（手搭没给 Image 时为 null，只靠文字显形）。</summary>
            public readonly List<Image> frames = new List<Image>(AbilitySlots.Count);

            /// <summary>人设名（单行式的唯一一行 / 两行式的下行）。</summary>
            public readonly List<Text> labels = new List<Text>(AbilitySlots.Count);

            /// <summary>槽位名（两行式的上行）；单行式格子为 null。</summary>
            public readonly List<Text> captions = new List<Text>(AbilitySlots.Count);

            public string signature = string.Empty;
        }

        private readonly SlotRowView playerRow = new SlotRowView();
        private readonly SlotRowView opponentRow = new SlotRowView();

        private CharacterInstance player;
        private CharacterInstance opponent;
        private MatchSpawner spawner;

        private float playerFillShown = 1f;
        private float opponentFillShown = 1f;
        private float dataTimer;
        private float lookTimer;
        private string lookSignature = string.Empty;
        private bool warnedNoFlow;

        /// <summary>玩家当前血量比例（0~1），血条实际显示值（带追赶）。</summary>
        public float PlayerHealthFill
        {
            get { return playerFillShown; }
        }

        /// <summary>当前 HUD 认定的对手；没开局时为 null。</summary>
        public CharacterInstance Opponent
        {
            get { return opponent; }
        }

        // ---------- 生命周期 ----------

        private void Awake()
        {
            ResolveReferences();
            PrepareRows();
        }

        private void OnEnable()
        {
            dataTimer = 0f;
            lookTimer = 0f;
            playerFillShown = player != null ? player.HealthRatio : 0f;
            opponentFillShown = opponent != null ? opponent.HealthRatio : 0f;
            EnsureFonts();
            ApplyLook();
            RefreshNow();
        }

        /// <summary>
        /// 场景里存下来的 Text 可能丢了字体引用（代码里创建的系统字体对象无法被序列化），
        /// 那样中文会显示成方框；这里统一补一个能显示中文的字体。
        /// </summary>
        private void EnsureFonts()
        {
            EnsureFont(playerName);
            EnsureFont(playerBarValue);
            EnsureFont(playerTraitEmpty);
            EnsureFont(opponentName);
            EnsureFont(opponentBarValue);
            EnsureFont(opponentTraitEmpty);
            EnsureFont(bellValue);
            EnsureFont(roundValue);
            EnsureFont(detailLabel);

            EnsureFontsIn(playerRow);
            EnsureFontsIn(opponentRow);
        }

        private static void EnsureFontsIn(SlotRowView view)
        {
            for (int i = 0; i < view.labels.Count; i++)
            {
                EnsureFont(view.labels[i]);
            }

            for (int i = 0; i < view.captions.Count; i++)
            {
                EnsureFont(view.captions[i]);
            }
        }

        private static void EnsureFont(Text text)
        {
            if (text != null && text.font == null)
            {
                text.font = UiBuilder.GetFont();
            }
        }

        private void Update()
        {
            if (!IsHudState())
            {
                return;
            }

            dataTimer -= Time.deltaTime;
            if (dataTimer <= 0f)
            {
                dataTimer = 0.12f;
                RefreshData();
            }

            RefreshDynamic();

            lookTimer -= Time.deltaTime;
            if (lookTimer <= 0f)
            {
                lookTimer = 0.5f;
                ApplyLookIfSizeChanged();
            }
        }

        /// <summary>立即重算一遍（切换阶段、钟声敲响后由 GameUiController 调用）。</summary>
        public void RefreshNow()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            RefreshData();
            RefreshDynamic();

            if (autoGenerateSprites)
            {
                ApplyLookIfSizeChanged();
            }
        }

        // ---------- 引用解析 ----------

        /// <summary>把场景里搭好的零件凑齐；每项留空时按名字递归查找。</summary>
        private void ResolveReferences()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
            }

            if (playerName == null) playerName = FindText(PlayerNameName);
            if (playerBar == null) playerBar = FindImage(PlayerBarName);
            if (playerBarBorder == null) playerBarBorder = FindImage(PlayerBarBorderName);
            if (playerBarFill == null) playerBarFill = FindImage(PlayerBarFillName);
            if (playerBarValue == null) playerBarValue = FindText(PlayerBarValueName);
            if (playerTraitBox == null) playerTraitBox = FindImage(PlayerTraitBoxName);
            if (playerTraitRow == null) playerTraitRow = FindRect(PlayerTraitRowName);
            if (playerTraitRow == null) playerTraitRow = FindRect(PlayerSlotRowAliasName);
            if (playerTraitEmpty == null) playerTraitEmpty = FindText(PlayerTraitEmptyName);

            if (opponentName == null) opponentName = FindText(OpponentNameName);
            if (opponentBar == null) opponentBar = FindImage(OpponentBarName);
            if (opponentBarBorder == null) opponentBarBorder = FindImage(OpponentBarBorderName);
            if (opponentBarFill == null) opponentBarFill = FindImage(OpponentBarFillName);
            if (opponentBarValue == null) opponentBarValue = FindText(OpponentBarValueName);
            if (opponentTraitBox == null) opponentTraitBox = FindImage(OpponentTraitBoxName);
            if (opponentTraitRow == null) opponentTraitRow = FindRect(OpponentTraitRowName);
            if (opponentTraitRow == null) opponentTraitRow = FindRect(OpponentSlotRowAliasName);
            if (opponentTraitEmpty == null) opponentTraitEmpty = FindText(OpponentTraitEmptyName);

            if (bellBadge == null) bellBadge = FindImage(BellBadgeName);
            if (bellBadgeBorder == null) bellBadgeBorder = FindImage(BellBadgeBorderName);
            if (bellValue == null) bellValue = FindText(BellValueName);
            if (roundValue == null) roundValue = FindText(RoundValueName);
            if (detailLabel == null) detailLabel = FindText(DetailName);

            playerRow.box = playerTraitBox;
            playerRow.row = playerTraitRow;
            playerRow.placeholder = playerTraitEmpty;

            opponentRow.box = opponentTraitBox;
            opponentRow.row = opponentTraitRow;
            opponentRow.placeholder = opponentTraitEmpty;
        }

        private Text FindText(string name)
        {
            Transform found = FindTransform(transform, name);
            return found != null ? found.GetComponent<Text>() : null;
        }

        private Image FindImage(string name)
        {
            Transform found = FindTransform(transform, name);
            return found != null ? found.GetComponent<Image>() : null;
        }

        private RectTransform FindRect(string name)
        {
            Transform found = FindTransform(transform, name);
            return found as RectTransform;
        }

        private static Transform FindTransform(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransform(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // ---------- 五个能力槽 ----------

        private void PrepareRows()
        {
            PrepareRow(playerRow);
            PrepareRow(opponentRow);
        }

        /// <summary>
        /// 把一排技能槽准备好。手搭的格子两种形态都认：
        /// - 一个子物体只有 1 个 Text：**单行式**，显示「特质 剑客」；
        /// - 一个子物体有 2 个 Text：**两行式**，第一个当槽位名（上行），第二个当人设名（下行）。
        ///
        /// 子物体不足 <see cref="AbilitySlots.Count"/>（5）个时按需补建，补出来的格子沿用第 0 格的
        /// 尺寸与形态，保证和手搭的格子长得一样；一格都没有时才用 Inspector 上的兜底尺寸。
        /// </summary>
        private void PrepareRow(SlotRowView view)
        {
            if (view.row == null)
            {
                return;
            }

            if (view.frames.Count == 0)
            {
                CollectHandmadeSlots(view);
            }

            // 形态跟随第一格：有槽位名 Text 就是两行式，否则单行式。
            bool twoLine = view.captions.Count == 0 || view.captions[0] != null;
            for (int i = view.frames.Count; i < AbilitySlots.Count; i++)
            {
                CreateSlotChip(view, i, twoLine);
            }

            if (!fitSlotText)
            {
                return;
            }

            for (int i = 0; i < view.labels.Count; i++)
            {
                ApplySlotTextFit(view.labels[i]);
            }

            for (int i = 0; i < view.captions.Count; i++)
            {
                ApplySlotTextFit(view.captions[i]);
            }
        }

        /// <summary>把行里已有的子物体认成槽位（没有 Text 的子物体——比如纯装饰——会跳过并提示）。</summary>
        private void CollectHandmadeSlots(SlotRowView view)
        {
            int skipped = 0;
            for (int i = 0; i < view.row.childCount; i++)
            {
                Transform child = view.row.GetChild(i);
                Text[] texts = child.GetComponentsInChildren<Text>(true);
                if (texts.Length == 0)
                {
                    skipped++;
                    continue;
                }

                view.frames.Add(child.GetComponent<Image>());
                if (texts.Length >= 2)
                {
                    view.captions.Add(texts[0]);
                    view.labels.Add(texts[1]);
                }
                else
                {
                    view.captions.Add(null);
                    view.labels.Add(texts[0]);
                }
            }

            var group = view.row.GetComponent<HorizontalLayoutGroup>();
            if (group != null)
            {
                group.spacing = traitSlotSpacing;
            }

            if (skipped > 0)
            {
                Debug.LogWarning(
                    "[HUD] " + view.row.name + " 下有 " + skipped + " 个子物体没有 Text，已跳过；"
                    + "槽位格需要 Image（底色）+ Text（槽位名 / 人设名）。", view.row);
            }
        }

        /// <summary>补建一个槽位格。twoLine=true 时上「槽位名」下「人设名」，否则一行「特质 剑客」。</summary>
        private void CreateSlotChip(SlotRowView view, int index, bool twoLine)
        {
            Vector2 size = ReferenceChipSize(view, twoLine);
            RectTransform slot = UiBuilder.CreateRect(view.row, "Slot_" + index);
            slot.sizeDelta = size;

            var frame = slot.gameObject.AddComponent<Image>();
            frame.raycastTarget = false;
            frame.color = emptySlotFillColor;

            if (autoGenerateSprites)
            {
                frame.sprite = HudSprites.RoundedFill(
                    Mathf.Max(2, Mathf.RoundToInt(size.x)),
                    Mathf.Max(2, Mathf.RoundToInt(size.y)),
                    Mathf.RoundToInt(Mathf.Min(cornerRadius, size.y * 0.4f)));
                frame.type = Image.Type.Simple;
            }

            Text caption = null;
            Text label;

            if (twoLine)
            {
                caption = UiBuilder.CreateText(
                    slot, "Caption", string.Empty, slotCaptionFontSize, emptySlotTextColor,
                    TextAnchor.UpperCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
                var captionRect = (RectTransform)caption.transform;
                captionRect.anchorMin = new Vector2(0f, 1f);
                captionRect.anchorMax = new Vector2(1f, 1f);
                captionRect.pivot = new Vector2(0.5f, 1f);
                captionRect.anchoredPosition = new Vector2(0f, -3f);
                captionRect.sizeDelta = new Vector2(-6f, slotCaptionFontSize + 4f);

                label = UiBuilder.CreateText(
                    slot, "Label", string.Empty, slotValueFontSize, traitChipTextColor,
                    TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
                var labelRect = (RectTransform)label.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.offsetMin = new Vector2(3f, 2f);
                labelRect.offsetMax = new Vector2(-3f, -(slotCaptionFontSize + 7f));
            }
            else
            {
                label = UiBuilder.CreateText(
                    slot, "Label", string.Empty, traitSlotFontSize, traitChipTextColor,
                    TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, size);
                UiBuilder.Stretch((RectTransform)label.transform);
                label.rectTransform.offsetMin = new Vector2(4f, 0f);
                label.rectTransform.offsetMax = new Vector2(-4f, 0f);
            }

            label.raycastTarget = false;
            view.frames.Add(frame);
            view.captions.Add(caption);
            view.labels.Add(label);
            slot.gameObject.SetActive(false);
        }

        /// <summary>补建格子的尺寸：优先抄已有格子的 sizeDelta，没有才用 Inspector 兜底值。</summary>
        private Vector2 ReferenceChipSize(SlotRowView view, bool twoLine)
        {
            if (view.frames.Count > 0)
            {
                RectTransform reference = null;
                Image frame = view.frames[0];
                if (frame != null)
                {
                    reference = frame.rectTransform;
                }
                else if (view.labels.Count > 0 && view.labels[0] != null)
                {
                    reference = view.labels[0].rectTransform;
                }

                if (reference != null)
                {
                    Vector2 size = reference.sizeDelta;
                    if (size.x > 4f && size.y > 4f)
                    {
                        return size;
                    }
                }
            }

            return new Vector2(traitSlotWidth, twoLine ? slotChipHeight : traitSlotHeight);
        }

        /// <summary>文字超宽时自动缩字号；以 Text 当前字号为上限，不会把用户放大过的字又压回去。</summary>
        private void ApplySlotTextFit(Text text)
        {
            if (text == null || text.resizeTextForBestFit)
            {
                return;
            }

            int max = Mathf.Max(1, text.fontSize);
            int min = Mathf.Clamp(fitSlotTextMinFontSize, 1, max);
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = max;
            text.resizeTextMinSize = min;
        }

        /// <summary>
        /// 把一侧的五个能力槽填满：每个槽显示「装的是哪个人设」，空格只显示槽位名。
        /// 只在「槽内容变了」时重排文字，避免每帧刷 UI。
        /// </summary>
        private void FillSlotRow(SlotRowView view, CharacterInstance character, bool playerSide)
        {
            if (view.row == null)
            {
                return;
            }

            // 集齐一套时整框变金色；这个状态可能因为抢牌 / 被抢而变，所以每次都重设颜色。
            if (view.box != null)
            {
                bool complete = character != null && character.HasCompleteSet;
                view.box.color = complete ? completeSetColor : traitBoxBorderColor;
            }

            string signature = BuildSlotSignature(character);
            if (signature == view.signature)
            {
                return;
            }

            view.signature = signature;

            AbilityLoadout loadout = character != null ? character.Loadout : null;
            bool usable = loadout != null && !loadout.IsEmpty;

            // 还没生成角色：整排收起，退回占位文案，避免一排空框显得像坏了。
            // 有角色但某个槽空着：照常显示该槽的槽位名（灰色），玩家能看出「这里还缺一张」。
            if (view.placeholder != null)
            {
                view.placeholder.gameObject.SetActive(character == null);
                view.placeholder.text = traitBoxEmptyText;
            }

            int count = Mathf.Min(view.frames.Count, AbilitySlots.Count);
            for (int i = 0; i < view.frames.Count; i++)
            {
                GameObject go = SlotObject(view, i);
                bool shown = character != null && i < count;
                if (go != null)
                {
                    go.SetActive(shown);
                }

                if (!shown)
                {
                    continue;
                }

                AbilitySlot slot = AbilitySlots.All[i];
                AbilityCard card = default;
                int extraTraits = 0;
                bool filled = usable && TryGetDisplayCard(loadout, slot, out card, out extraTraits);

                string caption = slot.ToDisplayName();
                string value = filled
                    ? PersonaIds.ToChineseName(card.Persona) + (extraTraits > 0 ? "+" + extraTraits : string.Empty)
                    : emptySlotValue;

                SetSlot(
                    view, i,
                    filled ? CharacterInstance.GetPersonaColor(card.Persona, playerSide) : emptySlotFillColor,
                    filled ? traitChipTextColor : emptySlotTextColor,
                    caption, value, filled);
            }
        }

        /// <summary>
        /// 某个槽该显示哪张卡：技能槽直接取槽里的卡；特质槽可以囤多张，
        /// 取**最后获得**的那张（与「形象跟随特质」一致），余下的显示成「+N」。
        /// </summary>
        private static bool TryGetDisplayCard(AbilityLoadout loadout, AbilitySlot slot, out AbilityCard card, out int extraTraits)
        {
            extraTraits = 0;
            card = default;

            if (loadout == null)
            {
                return false;
            }

            if (slot == AbilitySlot.Trait)
            {
                IReadOnlyList<AbilityCard> traits = loadout.GetTraitCards();
                if (traits.Count == 0)
                {
                    return false;
                }

                card = traits[traits.Count - 1];
                extraTraits = traits.Count - 1;
                return true;
            }

            return loadout.TryGet(slot, out card);
        }

        private static GameObject SlotObject(SlotRowView view, int index)
        {
            if (index < 0 || index >= view.frames.Count)
            {
                return null;
            }

            Image frame = view.frames[index];
            if (frame != null)
            {
                return frame.gameObject;
            }

            Text label = view.labels[index];
            return label != null ? label.gameObject : null;
        }

        private static void SetSlot(SlotRowView view, int index, Color fill, Color ink, string caption, string value, bool filled)
        {
            if (index < view.frames.Count && view.frames[index] != null)
            {
                view.frames[index].color = fill;
            }

            Text captionLabel = index < view.captions.Count ? view.captions[index] : null;
            Text valueLabel = index < view.labels.Count ? view.labels[index] : null;

            if (captionLabel != null)
            {
                captionLabel.text = caption;
                captionLabel.color = ink;
                if (valueLabel != null)
                {
                    valueLabel.text = value;
                    valueLabel.color = ink;
                }

                return;
            }

            if (valueLabel != null)
            {
                // 单行式：有内容时「特质 剑客」，空格只留槽位名。
                valueLabel.text = filled ? caption + " " + value : caption;
                valueLabel.color = ink;
            }
        }

        /// <summary>槽内容的指纹：任一槽的人设 / 附加特质数变了才重排。</summary>
        private static string BuildSlotSignature(CharacterInstance character)
        {
            AbilityLoadout loadout = character != null ? character.Loadout : null;
            if (loadout == null)
            {
                return "none";
            }

            var builder = new StringBuilder(AbilitySlots.Count * 4);
            for (int i = 0; i < AbilitySlots.Count; i++)
            {
                AbilityCard card;
                int extraTraits;
                bool filled = TryGetDisplayCard(loadout, AbilitySlots.All[i], out card, out extraTraits);
                builder.Append(filled ? (int)card.Persona : -1);
                builder.Append(filled ? extraTraits : 0).Append(',');
            }

            return builder.ToString();
        }

        // ---------- 刷新 ----------

        /// <summary>只在这些阶段才读数据，避免主菜单/结算时摸到已销毁的角色。</summary>
        private bool IsHudState()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
                if (gameFlow == null)
                {
                    return false;
                }
            }

            MatchState state = gameFlow.State;
            return state == MatchState.Loading || state == MatchState.Collect || state == MatchState.Elimination;
        }

        private void RefreshData()
        {
            if (gameFlow == null)
            {
                gameFlow = GameFlow.Instance;
                if (gameFlow == null)
                {
                    if (!warnedNoFlow)
                    {
                        warnedNoFlow = true;
                        Debug.LogWarning("[HUD] 场景里没有 GameFlow，HUD 不会显示数值。", this);
                    }

                    return;
                }
            }

            spawner = gameFlow.Spawner;
            player = gameFlow.Player;

            if (player == null && spawner != null && spawner.Instances.Count > 0)
            {
                player = spawner.Instances[0];
            }

            opponent = PickOpponent();

            RefreshBar(playerBarFill, playerBarValue, player, true, false);
            RefreshBar(opponentBarFill, opponentBarValue, opponent, false, false);

            FillSlotRow(playerRow, player, true);
            FillSlotRow(opponentRow, opponent, false);

            RefreshRound();
            RefreshDetail();
        }

        private void RefreshName(Text label, string title, CharacterInstance character)
        {
            if (label == null)
            {
                return;
            }

            if (label.GetComponent<Outline>() == null)
            {
                var outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = textOutlineColor;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        private void RefreshBar(Image fill, Text value, CharacterInstance character, bool playerSide, bool force)
        {
            if (fill == null && value == null)
            {
                return;
            }

            if (value != null)
            {
                if (!showHealthNumbers || character == null)
                {
                    value.text = string.Empty;
                }
                else
                {
                    value.text = string.Format(
                        healthFormat,
                        Mathf.CeilToInt(character.CurrentHealth),
                        Mathf.CeilToInt(character.MaxHealth));
                }

                value.color = healthTextColor;
            }

            if (playerSide)
            {
                playerFillShown = force ? TargetFill(character) : playerFillShown;
            }
            else
            {
                opponentFillShown = force ? TargetFill(character) : opponentFillShown;
            }
        }

        private static float TargetFill(CharacterInstance character)
        {
            return character != null ? character.HealthRatio : 0f;
        }

        /// <summary>每帧的轻量部分：倒计时数字与血条追赶。</summary>
        private void RefreshDynamic()
        {
            float speed = Mathf.Max(0.01f, healthSmoothing) * Time.deltaTime;
            playerFillShown = Mathf.MoveTowards(playerFillShown, TargetFill(player), speed);
            opponentFillShown = Mathf.MoveTowards(opponentFillShown, TargetFill(opponent), speed);

            if (playerBarFill != null)
            {
                playerBarFill.fillAmount = playerFillShown;
            }

            if (opponentBarFill != null)
            {
                opponentBarFill.fillAmount = opponentFillShown;
            }

            if (bellValue == null)
            {
                return;
            }

            BellSystem bells = gameFlow != null ? gameFlow.Bells : null;
            Color color = badgeTextColor;
            string text;

            if (bells == null || bells.Runtime == null || !bells.Runtime.TimerEnabled)
            {
                text = "—";
            }
            else
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(bells.SecondsUntilNextBell));
                text = seconds.ToString();
                if (countdownWarnSeconds > 0 && seconds <= countdownWarnSeconds)
                {
                    color = warnColor;
                }
            }

            bellValue.text = text;
            bellValue.color = color;
        }

        private void RefreshRound()
        {
            if (roundValue == null)
            {
                return;
            }

            BellSystem bells = gameFlow != null ? gameFlow.Bells : null;
            int current = bells != null && bells.Runtime != null ? bells.Runtime.SequenceNumber + 1 : 1;

            roundValue.color = roundColor;
            if (roundValue.GetComponent<Outline>() == null)
            {
                var outline = roundValue.gameObject.AddComponent<Outline>();
                outline.effectColor = textOutlineColor;
                outline.effectDistance = new Vector2(1.5f, -1.5f);
            }
        }

        private void RefreshDetail()
        {
            if (detailLabel == null)
            {
                return;
            }

            if (!showDetails)
            {
                detailLabel.gameObject.SetActive(false);
                return;
            }

            detailLabel.gameObject.SetActive(true);

            var builder = new StringBuilder();
            builder.Append("阶段：").Append(gameFlow.State.ToDisplayName());

            if (spawner != null && spawner.Instances.Count > 0)
            {
                int alive = 0;
                for (int i = 0; i < spawner.Instances.Count; i++)
                {
                    CharacterInstance character = spawner.Instances[i];
                    if (character == null || character.IsDead)
                    {
                        continue;
                    }

                    if (character.Participant == null || character.Participant.IsInPlay)
                    {
                        alive++;
                    }
                }

                builder.Append("　在场 ").Append(alive).Append('/').Append(spawner.SpawnedCount);
            }

            if (player != null)
            {
                DerivedAbilityStats derived = player.Derived;
                builder.Append('\n').Append('：').Append(player.DisplayName)
                    .Append("　生命 ").Append(Mathf.CeilToInt(player.CurrentHealth)).Append('/')
                    .Append(Mathf.CeilToInt(player.MaxHealth))
                    .Append("　防御 ").Append(Mathf.RoundToInt(derived.Defense))
                    .Append("（减伤 ").Append(AbilityMath.Percent(derived.DamageReduction)).Append("）");
                builder.Append('\n').Append(DescribeAbilities(player));
            }

            detailLabel.text = builder.ToString();
        }

        /// <summary>五个能力槽：特质（被动）、普攻、本我/自我/超我，带冷却状态。</summary>
        private static string DescribeAbilities(CharacterInstance character)
        {
            if (character == null || character.Loadout == null || character.Loadout.IsEmpty)
            {
                return "能力：—";
            }

            var builder = new StringBuilder("能力：");
            builder.Append(character.Loadout.Describe());

            IReadOnlyList<AbilityCard> cards = character.Loadout.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCard card = cards[i];
                AbilityDefinition definition = card.Definition;
                builder.Append('\n').Append("　").Append(card.Slot.ToDisplayName()).Append('：');
                builder.Append(definition != null ? definition.AbilityName : "（缺少数值）");

                if (card.Slot.IsBasicAttack())
                {
                    builder.Append("　伤害 ").Append(AbilityMath.Number(character.Derived.BasicAttackDamage))
                        .Append("　间隔 ").Append(AbilityMath.Number(character.Derived.AttackIntervalSeconds)).Append("s");
                }
                else if (card.Slot.IsSkill())
                {
                    float cooldown = character.Abilities != null ? character.Abilities.GetCooldownRemaining(card.Slot) : 0f;
                    builder.Append(cooldown > 0f
                        ? "　冷却 " + Mathf.CeilToInt(cooldown) + "s"
                        : "　就绪");
                }
                else
                {
                    builder.Append("　被动");
                }
            }

            return builder.ToString();
        }

        // ---------- 对手 ----------

        private CharacterInstance PickOpponent()
        {
            if (spawner == null || spawner.Instances.Count == 0)
            {
                return null;
            }

            // 三场并行 1v1：HUD 右侧应该显示「玩家本场的对手」，而不是全图最近的人机。
            // 位置钟声 / B8 重分配后配对会变，GameUiController 订阅 ArenasChanged 催这里立刻重算。
            if (preferPairedOpponent && gameFlow != null && player != null)
            {
                ArenaPlan plan = gameFlow.CurrentPlan;
                if (plan != null)
                {
                    int pairedId = plan.OpponentOf(player.Id);
                    if (pairedId >= 0)
                    {
                        for (int i = 0; i < spawner.Instances.Count; i++)
                        {
                            CharacterInstance paired = spawner.Instances[i];
                            if (paired != null && paired.Id == pairedId && IsCandidate(paired))
                            {
                                return paired;
                            }
                        }
                    }
                }
            }

            if (opponentPick == HudOpponentPick.FixedId)
            {
                for (int i = 0; i < spawner.Instances.Count; i++)
                {
                    CharacterInstance character = spawner.Instances[i];
                    if (character != null && character.Id == fixedOpponentId && IsCandidate(character))
                    {
                        return character;
                    }
                }
            }

            CharacterInstance best = null;
            float bestDistance = float.MaxValue;
            Vector3 origin = player != null ? player.transform.position : Vector3.zero;

            for (int i = 0; i < spawner.Instances.Count; i++)
            {
                CharacterInstance character = spawner.Instances[i];
                if (!IsCandidate(character))
                {
                    continue;
                }

                if (opponentPick == HudOpponentPick.FirstAlive)
                {
                    return character;
                }

                float distance = (character.transform.position - origin).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = character;
                }
            }

            return best;
        }

        /// <summary>可当对手的人：不是玩家、没死、还在场上。</summary>
        private bool IsCandidate(CharacterInstance character)
        {
            if (character == null || character == player || character.IsDead)
            {
                return false;
            }

            return character.Participant == null || character.Participant.IsInPlay;
        }

        // ---------- 外观 ----------

        /// <summary>尺寸变了才重新生成贴图（Canvas 缩放不影响 rect 尺寸，只有真正改布局时才需要重建）。</summary>
        private void ApplyLookIfSizeChanged()
        {
            if (!autoGenerateSprites)
            {
                return;
            }

            string signature = BuildLookSignature();
            if (signature == lookSignature)
            {
                return;
            }

            lookSignature = signature;
            ApplyLook();
        }

        private string BuildLookSignature()
        {
            var builder = new StringBuilder();
            AppendSize(builder, playerBar);
            AppendSize(builder, playerTraitBox);
            AppendChipSizes(builder, playerRow);
            AppendSize(builder, opponentBar);
            AppendSize(builder, opponentTraitBox);
            AppendChipSizes(builder, opponentRow);
            AppendSize(builder, bellBadge);
            return builder.ToString();
        }

        private static void AppendChipSizes(StringBuilder builder, SlotRowView view)
        {
            for (int i = 0; i < view.frames.Count; i++)
            {
                AppendSize(builder, view.frames[i]);
            }
        }

        private static void AppendSize(StringBuilder builder, Graphic graphic)
        {
            if (graphic == null)
            {
                builder.Append("0x0;");
                return;
            }

            Vector2 size = ((RectTransform)graphic.transform).rect.size;
            builder.Append(Mathf.RoundToInt(size.x)).Append('x').Append(Mathf.RoundToInt(size.y)).Append(';');
        }

        /// <summary>按当前像素尺寸生成圆角 / 虚线贴图。用户自己指定的贴图不动。</summary>
        private void ApplyLook()
        {
            if (!autoGenerateSprites)
            {
                return;
            }

            int radius = Mathf.RoundToInt(Mathf.Max(0f, cornerRadius));
            int thickness = Mathf.RoundToInt(Mathf.Max(1f, borderThickness));
            int inset = Mathf.RoundToInt(Mathf.Max(1f, barInset));

            ApplyBarLook(playerBar, playerBarBorder, playerBarFill, playerHealthColor, radius, thickness, inset, true);
            ApplyBarLook(opponentBar, opponentBarBorder, opponentBarFill, opponentHealthColor, radius, thickness, inset, false);

            ApplyBoxLook(playerTraitBox, radius, thickness);
            ApplyBoxLook(opponentTraitBox, radius, thickness);

            ApplyChipLook(playerRow, radius);
            ApplyChipLook(opponentRow, radius);

            if (bellBadge != null)
            {
                Vector2 size = bellBadge.rectTransform.rect.size;
                int w = Mathf.RoundToInt(size.x);
                int h = Mathf.RoundToInt(size.y);
                if (w > 4 && h > 4 && HudSprites.IsGenerated(bellBadge.sprite))
                {
                    bellBadge.sprite = HudSprites.RoundedFill(w, h, Mathf.RoundToInt(badgeCornerRadius));
                    bellBadge.type = Image.Type.Simple;
                }

                bellBadge.color = badgeFillColor;
            }

            if (bellBadgeBorder != null)
            {
                Vector2 size = bellBadgeBorder.rectTransform.rect.size;
                int w = Mathf.RoundToInt(size.x);
                int h = Mathf.RoundToInt(size.y);
                if (w > 4 && h > 4 && HudSprites.IsGenerated(bellBadgeBorder.sprite))
                {
                    bellBadgeBorder.sprite = HudSprites.RoundedOutline(
                        w, h, Mathf.RoundToInt(badgeCornerRadius), thickness);
                    bellBadgeBorder.type = Image.Type.Simple;
                }

                bellBadgeBorder.color = borderColor;
            }
        }

        private void ApplyBarLook(
            Image bar, Image border, Image fill, Color fillColor, int radius, int thickness, int inset, bool fillFromLeft)
        {
            if (bar == null)
            {
                return;
            }

            Vector2 size = bar.rectTransform.rect.size;
            int w = Mathf.RoundToInt(size.x);
            int h = Mathf.RoundToInt(size.y);
            if (w <= 4 || h <= 4)
            {
                return;
            }

            if (HudSprites.IsGenerated(bar.sprite))
            {
                bar.sprite = HudSprites.RoundedFill(w, h, radius);
                bar.type = Image.Type.Simple;
            }

            bar.color = healthTrackColor;

            if (border != null && HudSprites.IsGenerated(border.sprite))
            {
                border.sprite = HudSprites.RoundedOutline(w, h, radius, thickness);
                border.type = Image.Type.Simple;
                border.color = borderColor;
            }

            if (fill == null)
            {
                return;
            }

            // 填充条必须内缩，贴图才能和边框留出间隙；用户手搭时也顺手对齐一下。
            if (fill.transform.parent == bar.transform)
            {
                var fillRect = fill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = new Vector2(inset, inset);
                fillRect.offsetMax = new Vector2(-inset, -inset);
            }

            int innerW = Mathf.Max(2, w - (inset * 2));
            int innerH = Mathf.Max(2, h - (inset * 2));
            int innerRadius = Mathf.Max(0, radius - inset);

            if (HudSprites.IsGenerated(fill.sprite))
            {
                fill.sprite = HudSprites.RoundedFill(innerW, innerH, innerRadius);
            }

            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = fillFromLeft ? (int)Image.OriginHorizontal.Left : (int)Image.OriginHorizontal.Right;
            fill.color = fillColor;
        }

        /// <summary>槽格底色也走圆角贴图（手搭的格子 sprite 为空时补上；自己指定过贴图的不动）。</summary>
        private void ApplyChipLook(SlotRowView view, int radius)
        {
            for (int i = 0; i < view.frames.Count; i++)
            {
                Image frame = view.frames[i];
                if (frame == null)
                {
                    continue;
                }

                Vector2 size = frame.rectTransform.rect.size;
                int w = Mathf.RoundToInt(size.x);
                int h = Mathf.RoundToInt(size.y);
                if (w <= 4 || h <= 4)
                {
                    continue;
                }

                if (frame.sprite == null || HudSprites.IsGenerated(frame.sprite))
                {
                    frame.sprite = HudSprites.RoundedFill(w, h, Mathf.RoundToInt(Mathf.Min(radius, h * 0.4f)));
                    frame.type = Image.Type.Simple;
                }
            }
        }

        private void ApplyBoxLook(Image box, int radius, int thickness)
        {
            if (box == null)
            {
                return;
            }

            Vector2 size = box.rectTransform.rect.size;
            int w = Mathf.RoundToInt(size.x);
            int h = Mathf.RoundToInt(size.y);
            if (w <= 4 || h <= 4)
            {
                return;
            }

            if (HudSprites.IsGenerated(box.sprite))
            {
                box.sprite = HudSprites.DashedRoundedOutline(
                    w, h, radius, thickness, dashPeriod, dashRatio);
                box.type = Image.Type.Simple;
            }

            // 颜色由 FillTraitRow 负责（要区分「已集齐」的金色），这里不覆盖。
        }

        /// <summary>给 Inspector 用：改完参数想立刻看效果时点一下（Play 模式下才有意义）。</summary>
        [ContextMenu("立即刷新 HUD 外观")]
        private void RefreshLookFromContextMenu()
        {
            lookSignature = string.Empty;
            ApplyLookIfSizeChanged();
        }

        /// <summary>由 <see cref="BattleHudBuilder"/> / 编辑器工具在生成后回填引用。</summary>
        public void AssignParts(BattleHudParts parts)
        {
            playerName = parts.playerName;
            playerBar = parts.playerBar;
            playerBarBorder = parts.playerBarBorder;
            playerBarFill = parts.playerBarFill;
            playerBarValue = parts.playerBarValue;
            playerTraitBox = parts.playerTraitBox;
            playerTraitRow = parts.playerTraitRow;
            playerTraitEmpty = parts.playerTraitEmpty;

            opponentName = parts.opponentName;
            opponentBar = parts.opponentBar;
            opponentBarBorder = parts.opponentBarBorder;
            opponentBarFill = parts.opponentBarFill;
            opponentBarValue = parts.opponentBarValue;
            opponentTraitBox = parts.opponentTraitBox;
            opponentTraitRow = parts.opponentTraitRow;
            opponentTraitEmpty = parts.opponentTraitEmpty;

            bellBadge = parts.bellBadge;
            bellBadgeBorder = parts.bellBadgeBorder;
            bellValue = parts.bellValue;
            roundValue = parts.roundValue;
            detailLabel = parts.detailLabel;

            playerRow.box = playerTraitBox;
            playerRow.row = playerTraitRow;
            playerRow.placeholder = playerTraitEmpty;

            opponentRow.box = opponentTraitBox;
            opponentRow.row = opponentTraitRow;
            opponentRow.placeholder = opponentTraitEmpty;

            // 生成器在 Awake 之后才回填引用，这里补一次槽位准备。
            PrepareRows();
            lookSignature = string.Empty;
        }
    }
}
