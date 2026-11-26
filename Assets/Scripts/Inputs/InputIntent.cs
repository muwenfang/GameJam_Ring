using System;
using Ring.Core;

namespace Ring.Inputs
{
    /// <summary>
    /// 鼠标选点的取样方式（D12 的交互口径，见 Docs/12）。
    ///
    /// 平A 已是左键、闪避已是右键（Docs/01 键位表），所以默认用「悬停跟随」：
    /// 选点不占用任何按键，技能施放时取当帧鼠标位置即可。
    /// 需要「按一下确认」的玩法再切到 Click，并把确认键设成中键避免冲突。
    /// </summary>
    public enum AimMode
    {
        /// <summary>每帧把鼠标位置投影成世界 / 格坐标，不占用按键（默认）。</summary>
        Hover = 0,

        /// <summary>只有按下指定鼠标键的那一帧才刷新并锁定选点，同时触发 AimPicked。</summary>
        Click = 1,
    }

    /// <summary>
    /// M0 输入层的唯一输出：玩家与 AI 同构的「输入意图」。
    ///
    /// 字段对应《钟声_架构分析_详版》§4.1：
    /// moveDir, jump, basicAttack, dodge, skillIndex, aimPoint。
    /// D3「玩家 / AI 同构」要求 AI（M6）也产出这个结构，M2 / M3 只消费它、不关心输入来自谁。
    ///
    /// aimPoint 按 D12 拆成三份：世界 XY（精度值）、<see cref="GridCoord"/>（格坐标）、
    /// 以及两个有效性标志。这样 M3 里「游侠超我：鼠标指定前方 4 格内任一格」
    /// 直接拿 AimCell 用，M9 表现层拿 AimWorldX/Y 摆特效，互不牵扯。
    ///
    /// 纯 C#、零 UnityEngine：可编进 .check/belltest 做离线断言，AI 也能直接构造。
    /// 需要 Vector2 的 Unity 侧扩展见 <see cref="InputIntentUnity"/>。
    /// </summary>
    [Serializable]
    public struct InputIntent
    {
        /// <summary>移动方向 X，已归一化（斜向不会更快）。</summary>
        public float MoveX;

        /// <summary>移动方向 Y。</summary>
        public float MoveY;

        /// <summary>本帧是否按下跳跃。</summary>
        public bool Jump;

        /// <summary>本帧是否按下平A。</summary>
        public bool BasicAttack;

        /// <summary>本帧是否按下闪避。</summary>
        public bool Dodge;

        /// <summary>本帧施放的技能槽：0 = 无，1 = 本我，2 = 自我，3 = 超我。</summary>
        public int SkillIndex;

        /// <summary>aimPoint 是否有效（相机缺失 / 射线未命中 / 被 UI 挡住时为 false）。</summary>
        public bool HasAimPoint;

        /// <summary>选点的世界 X（屏幕上鼠标投到游戏平面上的 XY）。</summary>
        public float AimWorldX;

        /// <summary>选点的世界 Y。</summary>
        public float AimWorldY;

        /// <summary>选点落在哪一格（世界 → 格，floor 取整）。</summary>
        public GridCoord AimCell;

        /// <summary>选点格是否落在 25×12 边界内；false 时技能层应自行拒绝或钳制。</summary>
        public bool AimInsideGrid;

        /// <summary>空意图（AI 未决策 / 无输入）。</summary>
        public static InputIntent Empty
        {
            get { return default(InputIntent); }
        }

        public bool HasMove
        {
            get { return MoveX != 0f || MoveY != 0f; }
        }

        public bool HasSkill
        {
            get { return SkillIndex >= 1 && SkillIndex <= 3; }
        }

        /// <summary>本帧完全没有任何动作。</summary>
        public bool IsIdle
        {
            get
            {
                return !HasMove && !Jump && !BasicAttack && !Dodge && !HasSkill;
            }
        }

        /// <summary>构造一份带选点的意图（AI / 离线测试用）。</summary>
        public static InputIntent WithAim(float worldX, float worldY, GridCoord cell, bool insideGrid)
        {
            return new InputIntent
            {
                HasAimPoint = true,
                AimWorldX = worldX,
                AimWorldY = worldY,
                AimCell = cell,
                AimInsideGrid = insideGrid,
            };
        }

        /// <summary>收集意图本身的自相矛盾之处，供自检工具调用。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "输入意图";

            if (SkillIndex < 0 || SkillIndex > 3)
            {
                report.Error(context, "skillIndex 只能是 0（无）或 1~3，当前是 " + SkillIndex + "。");
            }

            float sqr = MoveX * MoveX + MoveY * MoveY;
            if (sqr > 1.0001f)
            {
                report.Warn(context, "moveDir 未归一化（长度 " + Math.Sqrt(sqr).ToString("F3") + "），斜向移动会更快。");
            }

            if (AimInsideGrid && !HasAimPoint)
            {
                report.Warn(context, "AimInsideGrid 为 true 但 HasAimPoint 为 false，选点状态自相矛盾。");
            }
        }

        /// <summary>一行调试文本，验收 D12 时直接看 Console。</summary>
        public string Describe()
        {
            string aim = HasAimPoint
                ? "，选点 世界(" + AimWorldX.ToString("F2") + ", " + AimWorldY.ToString("F2")
                  + ") → 格" + AimCell + (AimInsideGrid ? string.Empty : "（地图外）")
                : "，无有效选点";
            return "移动(" + MoveX.ToString("F2") + ", " + MoveY.ToString("F2") + ")"
                   + "，跳" + (Jump ? "1" : "0") + " A" + (BasicAttack ? "1" : "0")
                   + " 闪" + (Dodge ? "1" : "0") + " 技能" + SkillIndex + aim;
        }
    }
}
