using System;
using System.Collections.Generic;

namespace Farm.Sim
{
    public enum Ground
    {
        Grass,

        /// <summary>Farmland: walkable and can be tilled.</summary>
        Dirt,
        Path,
        Water,
    }

    /// <summary>Things standing on a tile. Anything but None blocks movement.</summary>
    public enum Structure
    {
        None,
        House,
        Well,
        Bin,
        Shed,
        Fence,
        Tree,
        Rock,
    }

    /// <summary>One farm tile and whatever's growing on it.</summary>
    public sealed class Tile
    {
        public Ground Ground { get; internal set; }
        public Structure Structure { get; internal set; }
        public bool Tilled { get; internal set; }
        public bool Watered { get; internal set; }
        public CropType Crop { get; internal set; }

        /// <summary>Watered days this crop has had.</summary>
        public int Growth { get; internal set; }

        public bool IsWalkable => Structure == Structure.None && Ground != Ground.Water;
        public bool IsFarmland => Ground == Ground.Dirt && Structure == Structure.None;
        public bool HasCrop => Crop != null;
        public bool IsRipe => Crop != null && Growth >= Crop.DaysToGrow;

        /// <summary>Wells and ponds both provide water.</summary>
        public bool IsWaterSource => Structure == Structure.Well || Ground == Ground.Water;

        internal void CopyFrom(Tile o)
        {
            Ground = o.Ground;
            Structure = o.Structure;
            Tilled = o.Tilled;
            Watered = o.Watered;
            Crop = o.Crop;
            Growth = o.Growth;
        }
    }

    /// <summary>
    /// The farm grid. Built from an ASCII layout (see <see cref="Parse"/>) so designers can sketch a
    /// farm in a text editor. Cell (x, y) has y pointing north (up the screen); the first text row is
    /// the northern edge.
    /// </summary>
    public sealed class FarmMap
    {
        readonly Tile[,] _tiles;

        public int Width { get; }
        public int Height { get; }

        /// <summary>The tile in front of the house door where bots live.</summary>
        public Cell Home { get; internal set; }

        public FarmMap(int width, int height)
        {
            Width = width;
            Height = height;
            _tiles = new Tile[width, height];
            for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                _tiles[x, y] = new Tile();
        }

        public Tile this[Cell c] => _tiles[c.X, c.Y];

        public bool InBounds(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

        public bool IsWalkable(Cell c) => InBounds(c) && this[c].IsWalkable;

        public Tile TileAt(Cell c) => InBounds(c) ? this[c] : null;

        /// <summary>Is there a <paramref name="s"/> (or a water source, for Well) next to this cell?</summary>
        public bool IsNextTo(Cell c, Structure s)
        {
            for (int f = 0; f < 4; f++)
            {
                var n = c.Step((Facing)f);
                if (!InBounds(n)) continue;
                var t = this[n];
                if (s == Structure.Well ? t.IsWaterSource : t.Structure == s) return true;
            }
            return false;
        }

        public IEnumerable<Cell> AllCells()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new Cell(x, y);
        }

        public FarmMap Clone()
        {
            var m = new FarmMap(Width, Height) { Home = Home };
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                m._tiles[x, y].CopyFrom(_tiles[x, y]);
            return m;
        }

        /// <summary>
        /// Builds a map from text rows (first row = north). Legend:
        /// <code>
        ///   g grass      . farmland (dirt)   = path      ~ pond (water source)
        ///   t tilled     p tilled + parsnip seed
        ///   H house      D door (path tile; bots' home)
        ///   W well       B shipping bin      S seed shed
        ///   # fence      T tree              R rock
        /// </code>
        /// </summary>
        public static FarmMap Parse(IReadOnlyList<string> rows)
        {
            int height = rows.Count;
            int width = rows[0].Length;
            var map = new FarmMap(width, height);
            bool hasHome = false;

            for (int row = 0; row < height; row++)
            {
                if (rows[row].Length != width) throw new ArgumentException($"Row {row} is {rows[row].Length} wide, expected {width}");
                for (int x = 0; x < width; x++)
                {
                    var t = map._tiles[x, height - 1 - row];
                    char ch = rows[row][x];
                    switch (ch)
                    {
                        case 'g': t.Ground = Ground.Grass; break;
                        case '.': t.Ground = Ground.Dirt; break;
                        case '=': t.Ground = Ground.Path; break;
                        case '~': t.Ground = Ground.Water; break;
                        case 't': t.Ground = Ground.Dirt; t.Tilled = true; break;
                        case 'p': t.Ground = Ground.Dirt; t.Tilled = true; t.Crop = Crops.Parsnip; break;
                        case 'D':
                            t.Ground = Ground.Path;
                            map.Home = new Cell(x, height - 1 - row);
                            hasHome = true;
                            break;
                        case 'H': t.Ground = Ground.Grass; t.Structure = Structure.House; break;
                        case 'W': t.Ground = Ground.Grass; t.Structure = Structure.Well; break;
                        case 'B': t.Ground = Ground.Grass; t.Structure = Structure.Bin; break;
                        case 'S': t.Ground = Ground.Grass; t.Structure = Structure.Shed; break;
                        case '#': t.Ground = Ground.Grass; t.Structure = Structure.Fence; break;
                        case 'T': t.Ground = Ground.Grass; t.Structure = Structure.Tree; break;
                        case 'R': t.Ground = Ground.Grass; t.Structure = Structure.Rock; break;
                        default: throw new ArgumentException($"Unknown map character '{ch}' at row {row}, column {x}");
                    }
                }
            }
            if (!hasHome) throw new ArgumentException("The map needs a door ('D') for the bots' home");
            return map;
        }
    }

    /// <summary>Shortest walking routes. Bots move one tile per tick, never diagonally.</summary>
    public static class Pathing
    {
        /// <summary>Cells that count as "arrived" for a Go to command.</summary>
        public static List<Cell> Targets(FarmMap map, GoTarget target, Cell tile)
        {
            var list = new List<Cell>();
            switch (target)
            {
                case GoTarget.Home:
                    list.Add(map.Home);
                    break;
                case GoTarget.Tile:
                    if (map.IsWalkable(tile)) list.Add(tile);
                    break;
                default:
                    var s = target == GoTarget.Well ? Structure.Well : target == GoTarget.Bin ? Structure.Bin : Structure.Shed;
                    foreach (var c in map.AllCells())
                        if (map.IsWalkable(c) && map.IsNextTo(c, s)) list.Add(c);
                    break;
            }
            return list;
        }

        /// <summary>Walking steps from every cell to the nearest target (-1 = can't get there).</summary>
        public static int[,] DistanceField(FarmMap map, List<Cell> targets)
        {
            var dist = new int[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
                dist[x, y] = -1;

            var queue = new Queue<Cell>();
            foreach (var t in targets)
            {
                dist[t.X, t.Y] = 0;
                queue.Enqueue(t);
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (int f = 0; f < 4; f++)
                {
                    var n = c.Step((Facing)f);
                    if (!map.IsWalkable(n) || dist[n.X, n.Y] >= 0) continue;
                    dist[n.X, n.Y] = dist[c.X, c.Y] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>The next step towards the target (deterministic: N, E, S, W preference), or false if stuck.</summary>
        public static bool NextStep(FarmMap map, int[,] dist, Cell from, out Cell next, out Facing dir)
        {
            next = from;
            dir = Facing.North;
            if (!map.InBounds(from)) return false;
            int d = dist[from.X, from.Y];
            if (d <= 0) return false;
            for (int f = 0; f < 4; f++)
            {
                var n = from.Step((Facing)f);
                if (map.InBounds(n) && dist[n.X, n.Y] == d - 1)
                {
                    next = n;
                    dir = (Facing)f;
                    return true;
                }
            }
            return false;
        }
    }
}
