using System;
using System.Collections.Generic;
using System.Text;
using Ring.Characters;
using Ring.Core;

namespace Ring.Abilities
{
    /// <summary>
    /// 六人设 × 五槽位 = 30 条能力的默认数值表，完全按《技能表.txt》抄录。
    ///
    /// 为什么放在代码里而不是 .asset：
    ///   1. 技能数是固定的 30 条，且每条字段很多、互相引用（属性/层数/控制），
    ///      写在代码里能编译期发现拼错，也能被离线测试直接验证；
    ///   2. 策划要调参时用 <c>Assets/Data/Abilities/AbilityTuning.asset</c> 覆盖（见 AbilityTuning），
    ///      不必改动这张表，也不必把 30 条字段全部手写进 YAML。
    ///
    /// 注意：普攻槽的数值也在这张表里。角色资产上的 <c>BasicAttackProfile</c> 是旧版身体级镜像，
    /// 新代码以本表为准（两者不一致时 Editor 校验会给 Info 提示）。
    /// </summary>
    public static class AbilityCatalog
    {
        /// <summary>一个人设的槽位数。</summary>
        public const int SlotsPerPersona = AbilitySlots.Count;

        /// <summary>能力总数 = 6 人设 × 5 槽位。</summary>
        public const int TotalCount = 30;

        private static readonly AbilityDefinition[] Definitions = BuildAll();

        /// <summary>全部 30 条，顺序为「人设 → 槽位」。</summary>
        public static IReadOnlyList<AbilityDefinition> All
        {
            get { return Definitions; }
        }

        /// <summary>按 (人设, 槽位) 取定义，找不到返回 null。</summary>
        public static AbilityDefinition Get(PersonaId persona, AbilitySlot slot)
        {
            AbilityDefinition found;
            return TryGet(persona, slot, out found) ? found : null;
        }

        /// <summary>按 (人设, 槽位) 取定义。</summary>
        public static bool TryGet(PersonaId persona, AbilitySlot slot, out AbilityDefinition definition)
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                AbilityDefinition candidate = Definitions[i];
                if (candidate.Persona == persona && candidate.Slot == slot)
                {
                    definition = candidate;
                    return true;
                }
            }

            definition = null;
            return false;
        }

        /// <summary>取某个人设的五个能力（顺序同 <see cref="AbilitySlots.All"/>）。</summary>
        public static IReadOnlyList<AbilityDefinition> GetPersona(PersonaId persona)
        {
            var list = new List<AbilityDefinition>(SlotsPerPersona);
            for (int i = 0; i < AbilitySlots.Count; i++)
            {
                AbilityDefinition definition = Get(persona, AbilitySlots.All[i]);
                if (definition != null)
                {
                    list.Add(definition);
                }
            }

            return list;
        }

        /// <summary>取所有人在某个槽位上的能力（例如全部 6 个特质）。</summary>
        public static IReadOnlyList<AbilityDefinition> GetSlot(AbilitySlot slot)
        {
            var list = new List<AbilityDefinition>(PersonaIds.Count);
            for (int i = 0; i < PersonaIds.Count; i++)
            {
                AbilityDefinition definition = Get((PersonaId)i, slot);
                if (definition != null)
                {
                    list.Add(definition);
                }
            }

            return list;
        }

        /// <summary>某个人设的普攻定义（战斗层取平A参数时用）。</summary>
        public static AbilityDefinition GetBasicAttack(PersonaId persona)
        {
            return Get(persona, AbilitySlot.BasicAttack);
        }

        /// <summary>校验整张表：数量、重复、以及每条定义的自检。</summary>
        public static void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "技能总表";

            if (Definitions.Length != TotalCount)
            {
                report.Error(context, "应有 " + TotalCount + " 条能力，实际 " + Definitions.Length + " 条。");
            }

            for (int i = 0; i < PersonaIds.Count; i++)
            {
                for (int s = 0; s < AbilitySlots.Count; s++)
                {
                    if (!TryGet((PersonaId)i, AbilitySlots.All[s], out _))
                    {
                        report.Error(context, PersonaIds.ToChineseName((PersonaId)i) + " 缺少「"
                            + AbilitySlots.All[s].ToDisplayName() + "」能力。");
                    }
                }
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < Definitions.Length; i++)
            {
                AbilityDefinition definition = Definitions[i];
                string key = (int)definition.Persona + ":" + (int)definition.Slot;
                if (!seen.Add(key))
                {
                    report.Error(context, definition.ToDisplayName() + " 定义重复。");
                }

                definition.CollectIssues(report);
            }
        }

        /// <summary>整张表的一行概要（Editor 工具与日志）。</summary>
        public static string ToOverview()
        {
            var builder = new StringBuilder();
            builder.Append("能力总表：").Append(Definitions.Length).Append(" 条（").Append(PersonaIds.Count)
                .Append(" 人设 × ").Append(SlotsPerPersona).Append(" 槽）");

            for (int s = 0; s < AbilitySlots.Count; s++)
            {
                builder.Append('\n').Append("· ").Append(AbilitySlots.All[s].ToLongName()).Append('：');
                IReadOnlyList<AbilityDefinition> slotAbilities = GetSlot(AbilitySlots.All[s]);
                for (int i = 0; i < slotAbilities.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("、");
                    }

                    builder.Append(slotAbilities[i].AbilityName);
                }
            }

            return builder.ToString();
        }

        // ================= 数据 =================

        private static AbilityDefinition Define(
            PersonaId persona,
            AbilitySlot slot,
            string name,
            string description,
            AbilityShape shape)
        {
            return new AbilityDefinition
            {
                Persona = persona,
                Slot = slot,
                AbilityName = name,
                Description = description,
                Shape = shape,
            };
        }

        private static AbilityDefinition[] BuildAll()
        {
            var list = new List<AbilityDefinition>(TotalCount);

            BuildSwordsman(list);
            BuildSteamMechanic(list);
            BuildSpartan(list);
            BuildRanger(list);
            BuildClockmaker(list);
            BuildStrongman(list);

            return list.ToArray();
        }

        /// <summary>① 剑客：最高机动、高频连击。</summary>
        private static void BuildSwordsman(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.Swordsman, AbilitySlot.Trait, "剑意",
                    "每次造成伤害（平A/任意技能）得 1 层，持续 2 秒，上限 3 层，叠层刷新；每层 +10% 最终伤害（全局增伤乘区，不绑定技能）。",
                    AbilityShape.None);
                d.MaxStacks = 3;
                d.StackDurationSeconds = 2f;
                d.StackDamageBonus = 0.10f;
                d.RequiredHitsForTrigger = 1;
                d.TriggerWindowSeconds = 2f;
                d.Notes = "设计意图：靠短 CD 连击稳定维持满层。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Swordsman, AbilitySlot.BasicAttack, "近战横斩",
                    "正前方 1 格长 × 2 格宽矩形；前摇 0.15s；伤害 4。",
                    AbilityShape.Rect);
                d.AreaLengthCells = 1f;
                d.AreaWidthCells = 2f;
                d.WindupSeconds = 0.15f;
                d.Damage = 4f;
                d.Notes = "旧《数据和结构》把这条写成「正前方 2 格」，两版冲突，本表按《技能表》1×2 记录。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Swordsman, AbilitySlot.Id, "突进斩",
                    "向输入方向突进 2 格，覆盖长 3 × 宽 2 线性区域；伤害 12；CD 2s（全表最短）。",
                    AbilityShape.Rect);
                d.DashCells = 2f;
                d.AreaLengthCells = 3f;
                d.AreaWidthCells = 2f;
                d.Damage = 12f;
                d.CooldownSeconds = 2f;
                d.Notes = "位移本身是否造成伤害取决于方向与命中，战斗层先按「突进后判定 3×2」实现。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Swordsman, AbilitySlot.Ego, "敛息",
                    "立即清除自身所有负面效果；1.5s 内 +30% 减伤；回复 10 生命；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.DurationSeconds = 1.5f;
                d.DamageReductionBonus = 0.30f;
                d.HealAmount = 10f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Swordsman, AbilitySlot.Superego, "剑域",
                    "自身周围 2.5 格半径领域持续 2s；施法者在域内时攻击转化为等量全领域剑气；域内敌人移速 −30%、受到伤害 +15%；CD 5s。",
                    AbilityShape.Circle);
                d.RadiusCells = 2.5f;
                d.DurationSeconds = 2f;
                d.CooldownSeconds = 5f;
                d.SlowPercent = 0.30f;
                d.SlowSeconds = 2f;
                d.DamageTakenBonusPercent = 0.15f;
                d.Notes = "「攻击转化为全领域剑气」需要战斗层配合：本表只描述范围与减益。";
                list.Add(d);
            }
        }

        /// <summary>② 蒸汽机械师：近身爆发，打完拉开。</summary>
        private static void BuildSteamMechanic(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.SteamMechanic, AbilitySlot.Trait, "过载损耗",
                    "实际移速 = 基础移速 ×（当前生命 / 生命上限），回血同步恢复；每累计受击 3 次进入「蓄势」，下次平A命中 ≥1 枚时击退 1 格并消耗蓄势。",
                    AbilityShape.None);
                d.MoveSpeedScalesWithHealth = true;
                d.RequiredHitsForTrigger = 3;
                d.KnockbackCells = 1f;
                d.Notes = "蓄势未命中不消耗；击退触发后受击计数清零。文档写「3.24×（当前生命/110）」，"
                    + "本质就是本表的基础移速 × 生命比例。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.SteamMechanic, AbilitySlot.BasicAttack, "蒸汽铳（散弹）",
                    "正前方 5 格扇形，总散射角 28°（±14°）；弹速 6 格/s；前摇 0.2s；4 枚散弹，每枚 2 伤害。",
                    AbilityShape.Fan);
                d.RangeCells = 5f;
                d.SpreadDegrees = 28f;
                d.ProjectileSpeed = 6f;
                d.WindupSeconds = 0.2f;
                d.PelletCount = 4;
                d.PelletDamage = 2f;
                d.Notes = "四枚角度 −14° / −4.7° / +4.7° / +14°。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.SteamMechanic, AbilitySlot.Id, "高压喷射",
                    "前方 4 格扇形，总散射角 36°（±18°）；7 枚蒸汽弹，每枚 2 伤害；命中 ≥3 枚时目标移速 −30% 持续 0.5s；弹速 6 格/s；前摇 0.3s；CD 4s。",
                    AbilityShape.Fan);
                d.RangeCells = 4f;
                d.SpreadDegrees = 36f;
                d.ProjectileSpeed = 6f;
                d.WindupSeconds = 0.3f;
                d.PelletCount = 7;
                d.PelletDamage = 2f;
                d.HitsRequiredForBonus = 3;
                d.SlowPercent = 0.30f;
                d.SlowSeconds = 0.5f;
                d.CooldownSeconds = 4f;
                d.Notes = "角度 ±18 / ±12 / ±6 / 0。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.SteamMechanic, AbilitySlot.Ego, "紧急排气",
                    "原地停止 0.5s（无无敌帧）；立即回复 15；清除自身所有减速/定身；随后 2s 受伤 −25%；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.ImmobileSeconds = 0.5f;
                d.HealAmount = 15f;
                d.DurationSeconds = 2f;
                d.DamageTakenMultiplier = 0.75f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.SteamMechanic, AbilitySlot.Superego, "高性能模式",
                    "2s 内移速与攻击力 ×1.5；结束后 1s 内两者降为 70% 再恢复；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.DurationSeconds = 2f;
                d.MoveSpeedMultiplier = 1.5f;
                d.AttackSpeedMultiplier = 1.5f;
                d.Notes = "末尾 1s 的 70% 回落属于「结束后惩罚」，BattleRuntime 用一次短 buff 实现；本表只描述主段。";
                list.Add(d);
            }
        }

        /// <summary>③ 斯巴达战士：狂战士 / 举盾坦。</summary>
        private static void BuildSpartan(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.Spartan, AbilitySlot.Trait, "斯巴达韧性",
                    "「半举盾」姿态：基础防御 +8；每秒回复 0.5 生命。",
                    AbilityShape.None);
                d.DefenseFlatBonus = 8f;
                d.HealPerSecond = 0.5f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Spartan, AbilitySlot.BasicAttack, "长矛突刺",
                    "前方 2 格长 × 1 格宽矩形；前摇 0.2s；伤害 5。",
                    AbilityShape.Rect);
                d.AreaLengthCells = 2f;
                d.AreaWidthCells = 1f;
                d.WindupSeconds = 0.2f;
                d.Damage = 5f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Spartan, AbilitySlot.Id, "战阵横扫",
                    "前方 2×2 矩形蓄力横扫；伤害 10；命中目标移速 −20% 持续 1.5s；CD 4s。",
                    AbilityShape.Rect);
                d.AreaLengthCells = 2f;
                d.AreaWidthCells = 2f;
                d.Damage = 10f;
                d.SlowPercent = 0.20f;
                d.SlowSeconds = 1.5f;
                d.CooldownSeconds = 4f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Spartan, AbilitySlot.Ego, "绝对防御",
                    "「全举盾」2s：正前方 180° 扇形受伤 −90%；自身移速 −50%；禁止普攻与本我技能；可再次点击提前解除；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.DurationSeconds = 2f;
                d.DamageTakenMultiplier = 0.10f;
                d.MoveSpeedMultiplier = 0.5f;
                d.CanCancelEarly = true;
                d.Notes = "方向性减伤（只挡正面 180°）需要战斗层按朝向判定，这里只给总倍率。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Spartan, AbilitySlot.Superego, "狂战",
                    "投矛前方 5 格（弹速 8 格/s，宽 1 格），首个目标 8 伤害；随后「单刀狂战」3s：防御 −30%，平A 改单刀（射程 1 格、单发 2 伤害、间隔 0.5s）；姿态结束 1.5s「重新装备」硬直（移速 −30%、无法攻击）；CD 6s。",
                    AbilityShape.Line);
                d.RangeCells = 5f;
                d.AreaWidthCells = 1f;
                d.ProjectileSpeed = 8f;
                d.Damage = 8f;
                d.CooldownSeconds = 6f;
                d.DurationSeconds = 3f;
                d.DefenseMultiplier = 0.7f;
                d.AttackIntervalOverride = 0.5f;
                d.BasicAttackDamageOverride = 2f;
                d.Notes = "单刀射程 1 格与结束后 1.5s 的 −30% 移速/禁止攻击写在描述里："
                    + "这两项分别属于「平A 覆盖」与「脆弱期」，战斗层按时间轴实现。";
                list.Add(d);
            }
        }

        /// <summary>④ 精灵游侠：射程最远、脆皮。</summary>
        private static void BuildRanger(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.Ranger, AbilitySlot.Trait, "精灵之目",
                    "射程 8 格；每次平A或本我技能命中得 1 层「风之优雅」，持续 2s，上限 3 层，每层 +5% 移速；满 3 层时下一次平A消耗层数变「重箭」，该次伤害 +2。",
                    AbilityShape.None);
                d.RangeCells = 8f;
                d.MaxStacks = 3;
                d.StackDurationSeconds = 2f;
                d.StackMoveSpeedBonus = 0.05f;
                d.StackAttackDamageBonus = 2f;
                d.RequiredHitsForTrigger = 3;
                d.TriggerWindowSeconds = 2f;
                d.Notes = "特质带射程：持有者把手上那把武器的射程抬到 8 格（拆卡混装时同样生效）。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Ranger, AbilitySlot.BasicAttack, "游侠短弓",
                    "前方 8 格长 × 1 格宽直线弹道；弹速 10 格/s；前摇 0.15s；伤害 3。",
                    AbilityShape.Line);
                d.RangeCells = 8f;
                d.AreaWidthCells = 1f;
                d.ProjectileSpeed = 10f;
                d.WindupSeconds = 0.15f;
                d.Damage = 3f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Ranger, AbilitySlot.Id, "破甲重箭",
                    "射程 8 格宽 1 格；首个目标 8 伤害 + 移速 −30% 持续 1.5s；弹速 12 格/s；前摇 0.2s；CD 4s。",
                    AbilityShape.Line);
                d.RangeCells = 8f;
                d.AreaWidthCells = 1f;
                d.Damage = 8f;
                d.ProjectileSpeed = 12f;
                d.WindupSeconds = 0.2f;
                d.SlowPercent = 0.30f;
                d.SlowSeconds = 1.5f;
                d.CooldownSeconds = 4f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Ranger, AbilitySlot.Ego, "自然恩赐",
                    "原地停止 0.3s；回复 12；清除自身 1 个持续伤害或减速/定身；随后 1.5s 减伤 20%；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.ImmobileSeconds = 0.3f;
                d.HealAmount = 12f;
                d.DurationSeconds = 1.5f;
                d.DamageReductionBonus = 0.20f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Ranger, AbilitySlot.Superego, "缚地荆棘",
                    "鼠标指定前方 4 格内任一格为中心，生成 3 格长 × 1 格宽荆棘地形持续 2s；敌人试图离开时被缠绕定身 0.75s；游侠自身穿越不受影响；CD 4s。",
                    AbilityShape.Ground);
                d.RequiresPointerTarget = true;
                d.RangeCells = 4f;
                d.AreaLengthCells = 3f;
                d.AreaWidthCells = 1f;
                d.DurationSeconds = 2f;
                d.RootSeconds = 0.75f;
                d.CooldownSeconds = 4f;
                d.Notes = "全表唯一需要鼠标选点的技能；「离开时」的判定由地形系统触发。";
                list.Add(d);
            }
        }

        /// <summary>⑤ 钟表师：时间系，对框架要求最高。</summary>
        private static void BuildClockmaker(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.Clockmaker, AbilitySlot.Trait, "逆时",
                    "受到必定致死一击时，把自身状态（生命、坐标、增益/减益）倒回 3 秒前，该次致死伤害作废；CD 30s。",
                    AbilityShape.None);
                d.CooldownSeconds = 30f;
                d.DurationSeconds = 3f;
                d.Notes = "特质自带冷却（全表唯一）。3 秒快照由角色实例负责保存，特质只给时长与冷却。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Clockmaker, AbilitySlot.BasicAttack, "时钟飞刃",
                    "前方 5 格 × 宽 1 格直线；弹速 8 格/s；前摇 0.2s；去程命中 4 伤害，到最远端自动折返，回程再 4 伤害；飞刃未回归前不能发出第二次攻击。",
                    AbilityShape.Line);
                d.RangeCells = 5f;
                d.AreaWidthCells = 1f;
                d.ProjectileSpeed = 8f;
                d.WindupSeconds = 0.2f;
                d.Damage = 4f;
                d.Returns = true;
                d.SecondaryDamage = 4f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Clockmaker, AbilitySlot.Id, "催龄",
                    "前方 2×4 格内敌人造成 8 点「岁月侵蚀」；目标移速/攻速/防御全部 −20% 持续 2s；前摇 0.4s；CD 4s。",
                    AbilityShape.Rect);
                d.AreaLengthCells = 2f;
                d.AreaWidthCells = 4f;
                d.Damage = 8f;
                d.WindupSeconds = 0.4f;
                d.CooldownSeconds = 4f;
                d.SlowPercent = 0.20f;
                d.SlowSeconds = 2f;
                d.AttackSpeedMultiplier = 0.8f;
                d.DefenseMultiplier = 0.8f;
                d.Notes = "矩形的「2×4」按「前方 2 格长 × 4 格宽」理解。三条 −20% 都作用在命中目标身上。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Clockmaker, AbilitySlot.Ego, "回溯",
                    "生命值与全部增益/减益还原到 3 秒前，清除所有持续伤害；只还原生理数值、不还原坐标；单次回复上限 25；若 3 秒前生命更低则不倒扣；CD 8s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 8f;
                d.DurationSeconds = 3f;
                d.HealAmount = 25f;
                d.Notes = "HealAmount 记的是「回复上限 25」。快照与还原由角色实例执行，技能只给窗口与上限。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Clockmaker, AbilitySlot.Superego, "慢摆",
                    "改变周围半径 5 格时间流速 2s：域内目标动作速度 ×0.6（移速、攻速、施法读条、起跳动作均变慢）；前摇 0.4s；CD 6s。",
                    AbilityShape.Circle);
                d.RadiusCells = 5f;
                d.DurationSeconds = 2f;
                d.WindupSeconds = 0.4f;
                d.CooldownSeconds = 6f;
                d.MoveSpeedMultiplier = 0.6f;
                d.AttackSpeedMultiplier = 0.6f;
                d.Notes = "「动作速度」用一个统一倍率近似，具体到读条/起跳由战斗层乘算。";
                list.Add(d);
            }
        }

        /// <summary>⑥ 力士：近战控制、击退、眩晕。</summary>
        private static void BuildStrongman(List<AbilityDefinition> list)
        {
            {
                AbilityDefinition d = Define(PersonaId.Strongman, AbilitySlot.Trait, "力量",
                    "对同一目标每累计第 3 次平A附带 1s 眩晕，计数清零；按目标独立计数，1.5s 未命中该目标清零；目标眩晕结束后获得 1.5s 眩晕抗性（免疫新眩晕）。",
                    AbilityShape.None);
                d.RequiredHitsForTrigger = 3;
                d.StunSeconds = 1f;
                d.TriggerWindowSeconds = 1.5f;
                d.StunResistanceSeconds = 1.5f;
                d.Notes = "计数必须按目标分开，不能用单一计数器。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Strongman, AbilitySlot.BasicAttack, "出拳",
                    "前方 1 格长 × 2 格宽矩形；前摇 0.2s；伤害 3。",
                    AbilityShape.Rect);
                d.AreaLengthCells = 1f;
                d.AreaWidthCells = 2f;
                d.WindupSeconds = 0.2f;
                d.Damage = 3f;
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Strongman, AbilitySlot.Id, "崩进冲撞",
                    "前摇 0.25s 后直线冲刺 10 格/s、极限 5 格，判定 1×2，命中首个敌人即停；冲刺期霸体（免疫击退）但受伤正常；分段判定：1 格 4 伤+击退 1／2 格 5 伤+击退 1.5／3 格 6 伤+击退 2／4 格 8 伤+击退 0.5+眩晕 0.6s／5 格 10 伤+眩晕 1.0s；未命中跑满 5 格后 0.3s 硬直；CD 4s。",
                    AbilityShape.Rect);
                d.WindupSeconds = 0.25f;
                d.DashCells = 5f;
                d.DashSpeedCellsPerSecond = 10f;
                d.AreaLengthCells = 1f;
                d.AreaWidthCells = 2f;
                d.Damage = 10f;
                d.KnockbackCells = 2f;
                d.StunSeconds = 1f;
                d.CooldownSeconds = 4f;
                d.Notes = "Damage/Knockback/Stun 记的是 5 格档位（最高一档）；1~4 格的分段表写在描述里，"
                    + "战斗层按实际冲刺距离查表。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Strongman, AbilitySlot.Ego, "沉腰调息",
                    "2s 调息：无法攻击、受伤 −25%、免疫击退；每 1s 结算一次（该段未受伤回 4，受伤回 2）；2s 全程未被攻击则额外 +8 护盾持续 3s（完美调息）；可提前结束但无护盾；CD 5s。",
                    AbilityShape.Self);
                d.CooldownSeconds = 5f;
                d.DurationSeconds = 2f;
                d.DamageTakenMultiplier = 0.75f;
                d.HealPerSecond = 4f;
                d.ShieldAmount = 8f;
                d.ShieldSeconds = 3f;
                d.CanCancelEarly = true;
                d.Notes = "HealPerSecond 记的是「未受伤段」的 4 点；受伤段 2 点需要战斗层按段结算。";
                list.Add(d);
            }

            {
                AbilityDefinition d = Define(PersonaId.Strongman, AbilitySlot.Superego, "虚手锚定",
                    "前摇 0.3s，前方 4 格长 × 2 格宽直线（遇墙在墙前停止），命中首个敌人 2 伤害并记为「锚点」；目标 1s 内可自由行动，1s 结束时被强制拉回锚点坐标并定身 0.5s；CD 5s。",
                    AbilityShape.Line);
                d.AreaLengthCells = 4f;
                d.AreaWidthCells = 2f;
                d.Damage = 2f;
                d.WindupSeconds = 0.3f;
                d.RootSeconds = 0.5f;
                d.CooldownSeconds = 5f;
                d.Notes = "「1s 后拉回锚点」是全局时间回调，由战斗层记录锚点坐标。";
                list.Add(d);
            }
        }
    }
}
