using System.Collections.Generic;

namespace Ring.Bells
{
    /// <summary>一次钟声之后某个参与者持有的卡（快照），用于 UI 与日志。</summary>
    public sealed class BellAssignment
    {
        public BellAssignment(int participantId, string displayName, IReadOnlyList<TraitCard> traits, IReadOnlyList<SkillCard> skills)
        {
            ParticipantId = participantId;
            DisplayName = displayName;
            Traits = traits;
            Skills = skills;
        }

        public int ParticipantId { get; }

        public string DisplayName { get; }

        public IReadOnlyList<TraitCard> Traits { get; }

        public IReadOnlyList<SkillCard> Skills { get; }

        public string Describe()
        {
            var parts = new List<string>(Traits.Count + Skills.Count + 1);
            parts.Add(DisplayName);
            for (int i = 0; i < Traits.Count; i++)
            {
                parts.Add(Traits[i].ToDisplayName());
            }

            for (int i = 0; i < Skills.Count; i++)
            {
                parts.Add(Skills[i].ToDisplayName());
            }

            return string.Join(" / ", parts);
        }
    }

    /// <summary>位置钟声产生的一次位置互换（= 交换对手）。</summary>
    public sealed class BellPositionSwap
    {
        public BellPositionSwap(int participantAId, int participantBId)
        {
            ParticipantAId = participantAId;
            ParticipantBId = participantBId;
        }

        public int ParticipantAId { get; }

        public int ParticipantBId { get; }

        public override string ToString()
        {
            return ParticipantAId + " <-> " + ParticipantBId;
        }
    }

    /// <summary>
    /// B12：淘汰制下人数为奇数时，落单者与自己的复制体对决。
    /// 复制体的具体生成属于战斗/角色系统，钟声系统只给出「该给谁做复制体、数值打折多少」。
    /// </summary>
    public sealed class BellCloneRequest
    {
        public BellCloneRequest(int sourceParticipantId, float healthMultiplier, float defenseMultiplier)
        {
            SourceParticipantId = sourceParticipantId;
            HealthMultiplier = healthMultiplier;
            DefenseMultiplier = defenseMultiplier;
        }

        public int SourceParticipantId { get; }

        /// <summary>复制体生命倍率，B12 默认 0.8（低 20%）。</summary>
        public float HealthMultiplier { get; }

        /// <summary>复制体防御倍率，B12 默认 0.8（低 20%）。</summary>
        public float DefenseMultiplier { get; }
    }

    /// <summary>
    /// 一次钟声的执行结果。钟声系统只负责产生这份结果，播报 / 动画 / 存档由上层消费，
    /// 这样纯逻辑才能离线测试。
    /// </summary>
    public sealed class BellResult
    {
        public BellResult(BellKind kind, BellTrigger trigger, BellPhase phase, int sequenceNumber)
        {
            Kind = kind;
            Trigger = trigger;
            Phase = phase;
            SequenceNumber = sequenceNumber;
            Assignments = new List<BellAssignment>();
            Swaps = new List<BellPositionSwap>();
            Notes = new List<string>();
            Executed = true;
        }

        public BellKind Kind { get; }

        public BellTrigger Trigger { get; }

        public BellPhase Phase { get; }

        public int SequenceNumber { get; }

        /// <summary>false 表示本次钟声没有真正生效（人不够、无可分配内容等），Summary 里写原因。</summary>
        public bool Executed { get; set; }

        public string Summary { get; set; }

        public List<BellAssignment> Assignments { get; }

        public List<BellPositionSwap> Swaps { get; }

        /// <summary>B12 落单者的复制体请求；没有落单者时为 null。</summary>
        public BellCloneRequest Clone { get; set; }

        public List<string> Notes { get; }

        public static BellResult CreateNotExecuted(
            BellKind kind,
            BellTrigger trigger,
            BellPhase phase,
            int sequenceNumber,
            string reason)
        {
            var result = new BellResult(kind, trigger, phase, sequenceNumber)
            {
                Executed = false,
                Summary = reason,
            };
            return result;
        }

        public BellResult AddNote(string note)
        {
            if (!string.IsNullOrEmpty(note))
            {
                Notes.Add(note);
            }

            return this;
        }

        /// <summary>把「第 N 次 位置钟声（死亡强制触发，淘汰制）已执行」这类头部信息写进 Summary。</summary>
        public string BuildHeader()
        {
            return "第 " + SequenceNumber + " 次" + Kind.ToDisplayName()
                + "（" + Trigger.ToDisplayName() + "，" + Phase.ToDisplayName() + "）";
        }

        public string ToMultilineText()
        {
            var lines = new List<string>(Assignments.Count + Swaps.Count + Notes.Count + 4);
            lines.Add(BuildHeader() + (Executed ? "" : " —— 未执行") );
            if (!string.IsNullOrEmpty(Summary))
            {
                lines.Add(Summary);
            }

            if (Clone != null)
            {
                lines.Add("B12 落单者 " + Clone.SourceParticipantId + " 与复制体对决（生命 ×"
                    + Clone.HealthMultiplier.ToString("0.##") + "，防御 ×"
                    + Clone.DefenseMultiplier.ToString("0.##") + "）");
            }

            for (int i = 0; i < Notes.Count; i++)
            {
                lines.Add("· " + Notes[i]);
            }

            for (int i = 0; i < Assignments.Count; i++)
            {
                lines.Add(" · " + Assignments[i].Describe());
            }

            for (int i = 0; i < Swaps.Count; i++)
            {
                lines.Add(" · 位置互换：" + Swaps[i]);
            }

            return string.Join("\n", lines);
        }

        public override string ToString()
        {
            return BuildHeader();
        }
    }
}
