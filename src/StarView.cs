using System.Collections.Generic;

namespace StarFieldAlgo
{
    /// <summary>
    /// Streaming viewport loader (network of unit cells).
    /// Rules:
    ///   · The "current cell" is the anchor; 4 cells stay resident: current, up, left, up-left;
    ///   · After moving one step to the north-west, the former up-left cell becomes the new current cell;
    ///   · The window shifts with the anchor: incrementally load 3 cells, unload 3 cells,
    ///     keeping 4 cells resident at all times;
    ///   · Repeated for continued north-west movement.
    /// Pure functions: cell-coordinate arithmetic only, no texture management.
    /// C#5-compatible (no tuples): cells are represented by CellKey.
    /// </summary>
    public static class StarView
    {
        /// <summary>A cell coordinate key (value equality for set operations).</summary>
        public struct CellKey
        {
            public int X, Y;
            public CellKey(int x, int y) { X = x; Y = y; }
            public override bool Equals(object obj)
            {
                if (!(obj is CellKey)) return false;
                var o = (CellKey)obj;
                return X == o.X && Y == o.Y;
            }
            public override int GetHashCode() { return X * 397 ^ Y; }
            public override string ToString() { return "(" + X + "," + Y + ")"; }
        }

        /// <summary>Current viewport: cell coordinates (curX, curY) as the first cell.</summary>
        public struct View
        {
            public int curX, curY;
        }

        /// <summary>Resident load set (fixed order): current → up → left → up-left.</summary>
        public static CellKey[] Window(View v)
        {
            return new[]
            {
                new CellKey(v.curX, v.curY),          // current
                new CellKey(v.curX, v.curY - 1),      // up
                new CellKey(v.curX - 1, v.curY),      // left
                new CellKey(v.curX - 1, v.curY - 1),  // up-left
            };
        }

        /// <summary>Move the viewport one step north-west: new current cell = former up-left cell.</summary>
        public static View MoveNorthWest(View v)
        {
            return new View { curX = v.curX - 1, curY = v.curY - 1 };
        }

        /// <summary>Cells to load after the window shifts (new window − old window).</summary>
        public static CellKey[] ToLoad(View oldV, View newV)
        {
            var oldW = Window(oldV);
            var oldSet = new HashSet<CellKey>(oldW);
            var list = new List<CellKey>();
            foreach (var c in Window(newV))
                if (!oldSet.Contains(c)) list.Add(c);
            return list.ToArray();
        }

        /// <summary>Cells to unload after the window shifts (old window − new window).</summary>
        public static CellKey[] ToUnload(View oldV, View newV)
        {
            var newW = Window(newV);
            var newSet = new HashSet<CellKey>(newW);
            var list = new List<CellKey>();
            foreach (var c in Window(oldV))
                if (!newSet.Contains(c)) list.Add(c);
            return list.ToArray();
        }
    }
}
