using System;

namespace Wizard.Sim
{
    /// <summary>Cardinal facing on the grid. North is +Y in grid space (+Z in world space).</summary>
    public enum Facing
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3,
    }

    public static class FacingExtensions
    {
        public static Facing TurnLeft(this Facing f) => (Facing)(((int)f + 3) % 4);
        public static Facing TurnRight(this Facing f) => (Facing)(((int)f + 1) % 4);
        public static Facing Reverse(this Facing f) => (Facing)(((int)f + 2) % 4);

        /// <summary>Clockwise yaw in degrees, matching Unity's Y rotation (North = 0, East = 90).</summary>
        public static float ToYawDegrees(this Facing f) => (int)f * 90f;

        public static Cell ToOffset(this Facing f)
        {
            switch (f)
            {
                case Facing.North: return new Cell(0, 1);
                case Facing.East: return new Cell(1, 0);
                case Facing.South: return new Cell(0, -1);
                default: return new Cell(-1, 0);
            }
        }
    }

    /// <summary>Integer grid coordinate.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public Cell Step(Facing f) => this + f.ToOffset();

        public int ManhattanDistance(Cell other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

        public static Cell operator +(Cell a, Cell b) => new Cell(a.X + b.X, a.Y + b.Y);
        public static Cell operator -(Cell a, Cell b) => new Cell(a.X - b.X, a.Y - b.Y);
        public static bool operator ==(Cell a, Cell b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Cell a, Cell b) => !(a == b);

        public bool Equals(Cell other) => this == other;
        public override bool Equals(object obj) => obj is Cell other && this == other;
        public override int GetHashCode() => (X * 73856093) ^ (Y * 19349663);
        public override string ToString() => $"({X},{Y})";
    }
}
