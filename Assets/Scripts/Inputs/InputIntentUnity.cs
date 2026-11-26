using UnityEngine;

namespace Ring.Inputs
{
    /// <summary>
    /// <see cref="InputIntent"/> / <see cref="GridSpace"/> 在 Unity 侧的 Vector2 便捷扩展。
    ///
    /// 单独成文件，是为了让 Intent / GridSpace 保持零 UnityEngine 依赖、能编进离线回归；
    /// 这里只做「float ↔ Vector2」的无状态转换，不含任何轮询或场景逻辑。
    /// UI / 场景对象一律由用户在 Inspector 里拖，本文件不创建任何物体。
    /// </summary>
    public static class InputIntentUnity
    {
        public static Vector2 MoveDir(this InputIntent intent)
        {
            return new Vector2(intent.MoveX, intent.MoveY);
        }

        public static Vector2 AimWorld(this InputIntent intent)
        {
            return new Vector2(intent.AimWorldX, intent.AimWorldY);
        }

        public static Vector2Int AimCellIndex(this InputIntent intent)
        {
            return new Vector2Int(intent.AimCell.Col, intent.AimCell.Row);
        }

        public static InputIntent WithMove(this InputIntent intent, Vector2 dir)
        {
            intent.MoveX = dir.x;
            intent.MoveY = dir.y;
            return intent;
        }

        public static InputIntent WithAimWorld(this InputIntent intent, Vector2 world, GridSpace grid)
        {
            GridCoord cell = grid.WorldToCell(world.x, world.y);
            intent.HasAimPoint = true;
            intent.AimWorldX = world.x;
            intent.AimWorldY = world.y;
            intent.AimCell = cell;
            intent.AimInsideGrid = grid.IsInside(cell);
            return intent;
        }

        /// <summary>格中心的世界坐标（摆放特效 / 高亮格用）。</summary>
        public static Vector2 CellCenterWorld(this GridSpace grid, GridCoord cell)
        {
            grid.CellToWorldCenter(cell, out float x, out float y);
            return new Vector2(x, y);
        }

        public static Vector2 CellCenterWorld(this GridSpace grid, int col, int row)
        {
            return grid.CellCenterWorld(new GridCoord(col, row));
        }
    }
}
