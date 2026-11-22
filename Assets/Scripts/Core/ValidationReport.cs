using System.Collections.Generic;
using System.Text;

namespace Ring.Core
{
    /// <summary>校验结果的严重级别。</summary>
    public enum ValidationSeverity
    {
        /// <summary>只是提示，不影响运行。</summary>
        Info = 0,

        /// <summary>数值可能不符合设计文档，或存在会被运行时兜底的危险。</summary>
        Warning = 1,

        /// <summary>配置缺失/矛盾，系统无法正常工作。</summary>
        Error = 2,
    }

    /// <summary>一条校验结果。</summary>
    public readonly struct ValidationIssue
    {
        public readonly ValidationSeverity Severity;

        /// <summary>出问题的对象名（资产名、系统名、条款号等），用于定位。</summary>
        public readonly string Context;

        public readonly string Message;

        public ValidationIssue(ValidationSeverity severity, string context, string message)
        {
            Severity = severity;
            Context = context ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return "[" + Severity + "] " + Context + "：" + Message;
        }
    }

    /// <summary>
    /// 可复用的校验报告。人物数值、输入映射、钟声编排都用它收集问题，
    /// 免得每个系统各写一套日志格式。
    /// </summary>
    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();

        /// <summary>已收集的问题，按添加顺序。</summary>
        public IReadOnlyList<ValidationIssue> Issues => issues;

        /// <summary>问题总数。</summary>
        public int Count => issues.Count;

        /// <summary>没有任何问题。</summary>
        public bool IsClean => issues.Count == 0;

        /// <summary>是否含 Error 级问题。</summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == ValidationSeverity.Error)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>是否含 Warning 级问题。</summary>
        public bool HasWarnings
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == ValidationSeverity.Warning)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>追加一条问题。</summary>
        public void Add(ValidationSeverity severity, string context, string message)
        {
            issues.Add(new ValidationIssue(severity, context, message));
        }

        /// <summary>追加一条 Info。</summary>
        public void Info(string context, string message)
        {
            Add(ValidationSeverity.Info, context, message);
        }

        /// <summary>追加一条 Warning。</summary>
        public void Warn(string context, string message)
        {
            Add(ValidationSeverity.Warning, context, message);
        }

        /// <summary>追加一条 Error。</summary>
        public void Error(string context, string message)
        {
            Add(ValidationSeverity.Error, context, message);
        }

        /// <summary>合并另一份报告。</summary>
        public void Append(ValidationReport other)
        {
            if (other == null)
            {
                return;
            }

            for (int i = 0; i < other.issues.Count; i++)
            {
                issues.Add(other.issues[i]);
            }
        }

        /// <summary>清空。</summary>
        public void Clear()
        {
            issues.Clear();
        }

        /// <summary>多行文本，便于粘到 Console 或编辑器窗口。</summary>
        public string ToText()
        {
            if (issues.Count == 0)
            {
                return "校验通过：没有发现问题。";
            }

            var builder = new StringBuilder();
            builder.Append("校验结果：共 ").Append(issues.Count).Append(" 条（Error ")
                .Append(CountOf(ValidationSeverity.Error)).Append("，Warning ")
                .Append(CountOf(ValidationSeverity.Warning)).Append("，Info ")
                .Append(CountOf(ValidationSeverity.Info)).Append("）");

            for (int i = 0; i < issues.Count; i++)
            {
                builder.Append('\n').Append(issues[i]);
            }

            return builder.ToString();
        }

        private int CountOf(ValidationSeverity severity)
        {
            int count = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
