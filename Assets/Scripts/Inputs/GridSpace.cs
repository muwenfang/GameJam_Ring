using System;
using Ring.Core;

namespace Ring.Inputs
{
    /// <summary>
    /// 地图上的一格：25 列 × 12 行，(0,0) 是左下角那一格。
    /// 依据《钟声_架构分析_详版》§一「地图尺寸 25 格 × 12 格（横版 2D）」与 §5.1「格坐标系统一」。
    ///
    /// 纯 C#、零 UnityEngine 依赖：AI（M6）可以直接构造格坐标，
    /// .check/belltest 也能在不启动 Unity 的情况下断言「世界 → 格」的取整行为。
    /// </summary>
    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>
    {
        /// <summary>列，从左往右 0..Columns-1。</summary>
        public int Col;

        /// <summary>行，从下往上 0..Rows-1。</summary>
        public int Row;

        public GridCoord(int col, int row)
        {
            Col = col;
            Row = row;
        }

        public bool Equals(GridCoord other)
        {
            return Col == other.Col && Row == other.Row;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoord other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((Col * 397) ^ Row);
        }

        public override string ToString()
        {
            return "(" + Col + ", " + Row + ")";
        }

        public static bool operator ==(GridCoord a, GridCoord b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(GridCoord a, GridCoord b)
        {
            return !a.Equals(b);
        }
    }

    /// <summary>
    /// 「世界坐标 ↔ 格坐标」的唯一换算规则（D12）。
    ///
    /// 输入层把鼠标屏幕坐标投到 z = gamePlaneZ 的游戏平面得到世界 XY，
    /// 再由本结构做 floor 取整得到 <see cref="GridCoord"/>。反向换算用于把
    /// 「格」还原成世界坐标（技能范围、机关选点、HUD 高亮格）。
    ///
    /// 刻意做成可序列化的纯数据结构：Inspector 里直接填数值，不新建资产、不由代码创建对象；
    /// 与 <see cref="GridCoord"/> 一起可以编进离线回归工程。
    /// </summary>
    [Serializable]
    public struct GridSpace
    {
        /// <summary>文档口径的列数。</summary>
        public const int DocumentColumns = 25;

        /// <summary>文档口径的行数。</summary>
        public const int DocumentRows = 12;

        /// <summary>文档口径下一格对应的世界单位（与 PlayerMotor.unitsPerCell 一致）。</summary>
        public const float DocumentCellSize = 1f;

        /// <summary>一格对应多少世界单位。必须 > 0，且要和 PlayerMotor / AbilityCaster 的 unitsPerCell 相同。</summary>
        public float CellSize;

        /// <summary>格 (0,0) 左下角的世界 X。地图不在世界原点时（例如按擂台中心摆放）由这里补偿。</summary>
        public float OriginX;

        /// <summary>格 (0,0) 左下角的世界 Y。</summary>
        public float OriginY;

        /// <summary>列数（边界判定用）。</summary>
        public int Columns;

        /// <summary>行数（边界判定用）。</summary>
        public int Rows;

        /// <summary>文档默认值：25×12、格距 1、原点在世界 (0,0)。</summary>
        public static GridSpace DocumentDefault
        {
            get
            {
                return new GridSpace
                {
                    CellSize = DocumentCellSize,
                    OriginX = 0f,
                    OriginY = 0f,
                    Columns = DocumentColumns,
                    Rows = DocumentRows,
                };
            }
        }

        /// <summary>安全格距：配置成 0 或负数时回退到文档值，避免除零。</summary>
        public float SafeCellSize
        {
            get { return CellSize > 0f ? CellSize : DocumentCellSize; }
        }

        /// <summary>安全列数。</summary>
        public int SafeColumns
        {
            get { return Columns > 0 ? Columns : DocumentColumns; }
        }

        /// <summary>安全行数。</summary>
        public int SafeRows
        {
            get { return Rows > 0 ? Rows : DocumentRows; }
        }

        /// <summary>
        /// 世界坐标 → 格坐标。用 <see cref="Math.Floor(double)"/> 而不是 C# 的 (int) 强转：
        /// 强转会把 -0.5 截断成 0，导致地图左半边整整错一格。
        /// 不钳制边界：地图外的格子也照实返回，由 <see cref="IsInside"/> 单独判定。
        /// </summary>
        public GridCoord WorldToCell(float worldX, float worldY)
        {
            float size = SafeCellSize;
            int col = FloorToInt((worldX - OriginX) / size);
            int row = FloorToInt((worldY - OriginY) / size);
            return new GridCoord(col, row);
        }

        /// <summary>
        /// 世界坐标 → 格坐标，并同时给出是否落在 25×12 边界内。
        /// 无论 inside 与否，cell 都会被填好（技能层可能要自己判「前方 4 格内」的射程）。
        /// </summary>
        public bool TryWorldToCell(float worldX, float worldY, out GridCoord cell)
        {
            cell = WorldToCell(worldX, worldY);
            return IsInside(cell);
        }

        /// <summary>格中心的世界坐标（用于摆放特效 / 高亮格）。</summary>
        public void CellToWorldCenter(GridCoord cell, out float worldX, out float worldY)
        {
            float size = SafeCellSize;
            worldX = OriginX + (cell.Col + 0.5f) * size;
            worldY = OriginY + (cell.Row + 0.5f) * size;
        }

        /// <summary>格是否在配置的地图边界内。</summary>
        public bool IsInside(GridCoord cell)
        {
            return cell.Col >= 0 && cell.Col < SafeColumns
                   && cell.Row >= 0 && cell.Row < SafeRows;
        }

        /// <summary>把任意格钳制到地图边界内（机关选点的兜底）。</summary>
        public GridCoord Clamp(GridCoord cell)
        {
            int col = Math.Max(0, Math.Min(SafeColumns - 1, cell.Col));
            int row = Math.Max(0, Math.Min(SafeRows - 1, cell.Row));
            return new GridCoord(col, row);
        }

        /// <summary>把 Inspector 里填坏的值修回可用范围（OnValidate / Awake 调用）。</summary>
        public void Sanitize()
        {
            if (CellSize <= 0f)
            {
                CellSize = DocumentCellSize;
            }

            if (Columns <= 0)
            {
                Columns = DocumentColumns;
            }

            if (Rows <= 0)
            {
                Rows = DocumentRows;
            }
        }

        /// <summary>收集配置问题（编辑器自检、运行时诊断共用）。</summary>
        public void CollectIssues(ValidationReport report)
        {
            if (report == null)
            {
                return;
            }

            const string context = "格坐标换算";

            if (CellSize <= 0f)
            {
                report.Error(context, "CellSize 必须大于 0，否则世界→格换算无意义（运行时会回退到 " + DocumentCellSize + "）。");
            }

            if (Columns <= 0 || Rows <= 0)
            {
                report.Error(context, "Columns / Rows 必须大于 0（运行时会回退到 "
                                      + DocumentColumns + "×" + DocumentRows + "）。");
            }

            if (CellSize > 0f && Math.Abs(CellSize - DocumentCellSize) > 0.0001f)
            {
                report.Warn(context, "格距 " + CellSize + " 与文档默认 " + DocumentCellSize
                                     + " 不同；请确认 PlayerMotor / AbilityCaster 的 unitsPerCell 已同步，否则技能射程会不一致。");
            }

            if (Columns > 0 && Rows > 0 && (Columns != DocumentColumns || Rows != DocumentRows))
            {
                report.Info(context, "边界配置为 " + Columns + "×" + Rows
                                     + "，与文档 25×12 不同（当前场景是圆形擂台时属预期）。");
            }
        }

        /// <summary>一行调试文本。</summary>
        public string Describe()
        {
            return "格距 " + SafeCellSize + "，原点 (" + OriginX + ", " + OriginY + ")，边界 "
                   + SafeColumns + "×" + SafeRows;
        }

        private static int FloorToInt(double value)
        {
            return (int)Math.Floor(value);
        }
    }
}
