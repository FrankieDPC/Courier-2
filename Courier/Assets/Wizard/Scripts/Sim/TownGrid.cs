using System;

namespace Wizard.Sim
{
    public enum CellKind
    {
        Road,
        Lot,
    }

    /// <summary>
    /// City-block layout: a road runs every <see cref="Pitch"/> cells in both directions and the
    /// gaps between are blocks of building lots. Only a rectangle of blocks is unlocked; the town
    /// grows by unlocking a ring of blocks at a time.
    /// </summary>
    public sealed class TownGrid
    {
        /// <summary>Lots along one side of a block. 2 means every lot touches a road.</summary>
        public int BlockSize { get; }
        public int Pitch => BlockSize + 1;

        public int BlocksX { get; }
        public int BlocksY { get; }
        public int Width => BlocksX * Pitch + 1;
        public int Height => BlocksY * Pitch + 1;

        // Unlocked blocks, inclusive.
        public int MinBlockX { get; private set; }
        public int MinBlockY { get; private set; }
        public int MaxBlockX { get; private set; }
        public int MaxBlockY { get; private set; }

        public event Action Expanded;

        public TownGrid(int blocksX, int blocksY, int blockSize, int minBlockX, int minBlockY, int maxBlockX, int maxBlockY)
        {
            if (blockSize < 1) throw new ArgumentOutOfRangeException(nameof(blockSize));
            BlocksX = blocksX;
            BlocksY = blocksY;
            BlockSize = blockSize;
            MinBlockX = Clamp(minBlockX, 0, blocksX - 1);
            MinBlockY = Clamp(minBlockY, 0, blocksY - 1);
            MaxBlockX = Clamp(maxBlockX, MinBlockX, blocksX - 1);
            MaxBlockY = Clamp(maxBlockY, MinBlockY, blocksY - 1);
        }

        public Cell UnlockedMin => new Cell(MinBlockX * Pitch, MinBlockY * Pitch);
        public Cell UnlockedMax => new Cell((MaxBlockX + 1) * Pitch, (MaxBlockY + 1) * Pitch);

        public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public CellKind KindAt(Cell c) => (c.X % Pitch == 0 || c.Y % Pitch == 0) ? CellKind.Road : CellKind.Lot;

        public bool IsUnlocked(Cell c)
        {
            var min = UnlockedMin;
            var max = UnlockedMax;
            return c.X >= min.X && c.Y >= min.Y && c.X <= max.X && c.Y <= max.Y;
        }

        public bool IsDrivable(Cell c) => InBounds(c) && IsUnlocked(c) && KindAt(c) == CellKind.Road;

        public bool IsLot(Cell c) => InBounds(c) && KindAt(c) == CellKind.Lot;

        public bool HasRoadAccess(Cell c)
        {
            for (int f = 0; f < 4; f++)
                if (IsDrivable(c.Step((Facing)f))) return true;
            return false;
        }

        public TownGrid Clone() => new TownGrid(BlocksX, BlocksY, BlockSize, MinBlockX, MinBlockY, MaxBlockX, MaxBlockY);

        public bool CanExpand => MinBlockX > 0 || MinBlockY > 0 || MaxBlockX < BlocksX - 1 || MaxBlockY < BlocksY - 1;

        /// <summary>Unlocks one ring of blocks around the current area (clamped to the map).</summary>
        public bool ExpandRing()
        {
            if (!CanExpand) return false;
            MinBlockX = Math.Max(0, MinBlockX - 1);
            MinBlockY = Math.Max(0, MinBlockY - 1);
            MaxBlockX = Math.Min(BlocksX - 1, MaxBlockX + 1);
            MaxBlockY = Math.Min(BlocksY - 1, MaxBlockY + 1);
            Expanded?.Invoke();
            return true;
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
