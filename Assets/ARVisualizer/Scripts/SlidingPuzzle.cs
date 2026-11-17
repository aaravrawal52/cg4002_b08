using System;
using System.Collections.Generic;

namespace ARVisualizer
{
    public enum PuzzlePhase { Ready, Playing, Paused, Solved }

    /// <summary>
    /// A numbered sliding-tile puzzle on a 3x3, 4x4 or 5x5 board. The caller supplies a monotonic clock.
    /// Positions count from the top-left, row by row; tile 0 is the gap.
    /// </summary>
    public sealed class SlidingPuzzle
    {
        public const int MinimumSize = 3, MaximumSize = 5;

        public int Size { get; private set; }
        public int Moves { get; private set; }
        public PuzzlePhase Phase { get; private set; }
        public int BlankIndex { get; private set; }

        readonly Random random;
        int[] tiles;
        double banked, resumedAt;

        public SlidingPuzzle(int size = MinimumSize, int? seed = null)
        {
            random = seed.HasValue ? new Random(seed.Value) : new Random();
            Size = Math.Clamp(size, MinimumSize, MaximumSize);
            Shuffle();
        }

        /// <summary>Starts from a given board, for example a saved game. It must hold 0 to size*size-1 once each.</summary>
        public SlidingPuzzle(int size, IReadOnlyList<int> arrangement)
        {
            random = new Random();
            Size = Math.Clamp(size, MinimumSize, MaximumSize);
            if (arrangement == null || arrangement.Count != Size * Size) throw new ArgumentException("One value per position is required", nameof(arrangement));
            tiles = new int[Size * Size];
            var seen = new bool[tiles.Length];
            for (int i = 0; i < tiles.Length; i++)
            {
                int tile = arrangement[i];
                if (tile < 0 || tile >= tiles.Length || seen[tile]) throw new ArgumentException("Each tile must appear once", nameof(arrangement));
                seen[tile] = true;
                tiles[i] = tile;
                if (tile == 0) BlankIndex = i;
            }
        }

        public int TileAt(int index) => tiles[index];
        public int IndexOf(int tile) => Array.IndexOf(tiles, tile);

        public bool IsSolved
        {
            get
            {
                for (int i = 0; i < tiles.Length - 1; i++)
                    if (tiles[i] != i + 1) return false;
                return true;
            }
        }

        /// <summary>Seconds spent playing. Pauses, and the time before the first start, are not counted.</summary>
        public double Elapsed(double now) => banked + (Phase == PuzzlePhase.Playing ? Math.Max(0, now - resumedAt) : 0);

        /// <summary>Start, pause or resume. On a solved board this deals and starts a new game.</summary>
        public void Toggle(double now)
        {
            if (Phase == PuzzlePhase.Playing)
            {
                banked = Elapsed(now);
                Phase = PuzzlePhase.Paused;
                return;
            }
            if (Phase == PuzzlePhase.Solved) Shuffle();
            resumedAt = now;
            Phase = PuzzlePhase.Playing;
        }

        /// <summary>
        /// Slides the tile beside the gap one step in a direction: x is +1 for right, y is +1 for down.
        /// Only while playing. Returns false when no tile can move that way.
        /// </summary>
        public bool Slide(int x, int y, double now)
        {
            if (Phase != PuzzlePhase.Playing || !Step(x, y)) return false;
            ++Moves;
            if (IsSolved)
            {
                banked = Elapsed(now);
                Phase = PuzzlePhase.Solved;
            }
            return true;
        }

        /// <summary>Changes the board size by a number of steps and deals a new game. Not while playing.</summary>
        public bool Resize(int steps)
        {
            int next = Math.Clamp(Size + steps, MinimumSize, MaximumSize);
            if (Phase == PuzzlePhase.Playing || next == Size) return false;
            Size = next;
            Shuffle();
            return true;
        }

        /// <summary>Deals a new game of the same size, waiting to be started. Not while playing.</summary>
        public bool Reset()
        {
            if (Phase == PuzzlePhase.Playing) return false;
            Shuffle();
            return true;
        }

        bool Step(int x, int y)
        {
            if ((x == 0) == (y == 0) || Math.Abs(x) > 1 || Math.Abs(y) > 1) return false;
            int column = BlankIndex % Size - x, row = BlankIndex / Size - y;
            if (column < 0 || column >= Size || row < 0 || row >= Size) return false;
            int source = row * Size + column;
            tiles[BlankIndex] = tiles[source];
            tiles[source] = 0;
            BlankIndex = source;
            return true;
        }

        // Legal moves from the solved board always leave a solvable one.
        void Shuffle()
        {
            tiles = new int[Size * Size];
            for (int i = 0; i < tiles.Length - 1; i++) tiles[i] = i + 1;
            BlankIndex = tiles.Length - 1;
            int lastX = 0, lastY = 0;
            for (int made = 0; made < tiles.Length * 30 || IsSolved;)
            {
                int direction = random.Next(4);
                int x = direction == 0 ? 1 : direction == 1 ? -1 : 0;
                int y = direction == 2 ? 1 : direction == 3 ? -1 : 0;
                if ((x == -lastX && y == -lastY) || !Step(x, y)) continue; // Never undo the previous move.
                lastX = x; lastY = y;
                ++made;
            }
            Moves = 0;
            banked = 0;
            Phase = PuzzlePhase.Ready;
        }
    }
}
