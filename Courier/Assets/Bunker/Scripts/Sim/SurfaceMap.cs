using System;
using System.Collections.Generic;

namespace Bunker.Sim
{
    public enum Ground
    {
        /// <summary>Can be farmed once it's clear of overgrowth.</summary>
        Soil,

        /// <summary>Old concrete: walkable, never farmable.</summary>
        Concrete,

        /// <summary>The stream. Not walkable; you fill up with water facing it.</summary>
        Water,
    }

    /// <summary>Built things. Anything but None blocks movement.</summary>
    public enum Structure
    {
        None,
        Bunker,

        /// <summary>The bunker hatch: deliver food and salvage here, take seeds from here.</summary>
        Hatch,
    }

    /// <summary>What a thousand years left lying around. Blocks movement until it's cleared.</summary>
    public enum Debris
    {
        None,
        Weeds,
        Thicket,
        Sapling,
        Tree,
        Rubble,

        // Ancient tech.
        Wreck,
        Terminal,
        SeedCache,
    }

    /// <summary>One tile of the surface and whatever is on it.</summary>
    public sealed class Tile
    {
        public Ground Ground { get; internal set; }
        public Structure Structure { get; internal set; }
        public Debris Debris { get; internal set; }
        public bool Tilled { get; internal set; }
        public bool Watered { get; internal set; }
        public CropType Crop { get; internal set; }

        /// <summary>Watered days this crop has had.</summary>
        public int Growth { get; internal set; }

        public bool IsWalkable => Structure == Structure.None && Debris == Debris.None && Ground != Ground.Water;

        /// <summary>Clear soil: can be tilled (crops are walkable too, so bots can work along rows).</summary>
        public bool IsFarmland => Ground == Ground.Soil && Structure == Structure.None && Debris == Debris.None;

        public bool IsOvergrown => Debris != Debris.None;
        public bool IsWater => Ground == Ground.Water;
        public bool HasCrop => Crop != null;
        public bool IsRipe => Crop != null && Growth >= Crop.DaysToGrow;

        internal void CopyFrom(Tile o)
        {
            Ground = o.Ground;
            Structure = o.Structure;
            Debris = o.Debris;
            Tilled = o.Tilled;
            Watered = o.Watered;
            Crop = o.Crop;
            Growth = o.Growth;
        }
    }

    /// <summary>
    /// The surface around the bunker. Built from an ASCII layout (see <see cref="Parse"/>) so designers
    /// can sketch it in a text editor. Cell (x, y) has y pointing north (up the screen); the first text
    /// row is the northern edge.
    /// </summary>
    public sealed class SurfaceMap
    {
        readonly Tile[,] _tiles;

        public int Width { get; }
        public int Height { get; }

        public Cell Hatch { get; internal set; }

        /// <summary>The tile in front of the hatch, where you and the bots spend the night.</summary>
        public Cell Home { get; internal set; }

        /// <summary>Which way you face at home: towards the hatch.</summary>
        public Facing HomeFacing { get; internal set; }

        public SurfaceMap(int width, int height)
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

        public bool IsWater(Cell c) => InBounds(c) && this[c].IsWater;
        public bool IsHatch(Cell c) => InBounds(c) && this[c].Structure == Structure.Hatch;

        public IEnumerable<Cell> AllCells()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                yield return new Cell(x, y);
        }

        public int Count(Func<Tile, bool> match)
        {
            int n = 0;
            foreach (var t in _tiles)
                if (match(t)) n++;
            return n;
        }

        public SurfaceMap Clone()
        {
            var m = new SurfaceMap(Width, Height) { Hatch = Hatch, Home = Home, HomeFacing = HomeFacing };
            for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                m._tiles[x, y].CopyFrom(_tiles[x, y]);
            return m;
        }

        /// <summary>
        /// Builds a map from text rows (first row = north). Legend:
        /// <code>
        ///   .  clear soil       =  concrete (old pad, bridge)    ~  stream
        ///   t  tilled soil      p  tilled, with a radish planted
        ///   ,  weeds            "  thicket       i  sapling      T  old tree      r  rubble
        ///   C  car wreck        X  dead terminal                 V  seed cache
        ///   B  bunker wall      H  hatch (you and the bots live on the open tile beside it)
        /// </code>
        /// </summary>
        public static SurfaceMap Parse(IReadOnlyList<string> rows)
        {
            int height = rows.Count;
            int width = rows[0].Length;
            var map = new SurfaceMap(width, height);
            bool hasHatch = false;

            for (int row = 0; row < height; row++)
            {
                if (rows[row].Length != width) throw new ArgumentException($"Row {row} is {rows[row].Length} wide, expected {width}");
                for (int x = 0; x < width; x++)
                {
                    var cell = new Cell(x, height - 1 - row);
                    var t = map._tiles[cell.X, cell.Y];
                    t.Ground = Ground.Soil;
                    char ch = rows[row][x];
                    switch (ch)
                    {
                        case '.': break;
                        case '=': t.Ground = Ground.Concrete; break;
                        case '~': t.Ground = Ground.Water; break;
                        case 't': t.Tilled = true; break;
                        case 'p': t.Tilled = true; t.Crop = Crops.Radish; break;
                        case ',': t.Debris = Debris.Weeds; break;
                        case '"': t.Debris = Debris.Thicket; break;
                        case 'i': t.Debris = Debris.Sapling; break;
                        case 'T': t.Debris = Debris.Tree; break;
                        case 'r': t.Debris = Debris.Rubble; break;
                        case 'C': t.Debris = Debris.Wreck; break;
                        case 'X': t.Debris = Debris.Terminal; break;
                        case 'V': t.Debris = Debris.SeedCache; break;
                        case 'B': t.Ground = Ground.Concrete; t.Structure = Structure.Bunker; break;
                        case 'H':
                            t.Ground = Ground.Concrete;
                            t.Structure = Structure.Hatch;
                            map.Hatch = cell;
                            hasHatch = true;
                            break;
                        default: throw new ArgumentException($"Unknown map character '{ch}' at row {row}, column {x}");
                    }
                }
            }
            if (!hasHatch) throw new ArgumentException("The map needs a hatch ('H')");

            // Home: the first open tile beside the hatch (south first), facing the hatch.
            var order = new[] { Facing.South, Facing.East, Facing.West, Facing.North };
            foreach (var f in order)
            {
                var c = map.Hatch.Step(f);
                if (!map.IsWalkable(c)) continue;
                map.Home = c;
                map.HomeFacing = f.Reverse();
                return map;
            }
            throw new ArgumentException("The hatch needs an open tile beside it");
        }
    }

    /// <summary>Shortest walking routes. Bots move one tile per tick, never diagonally.</summary>
    public static class Pathing
    {
        /// <summary>Cells that count as "arrived" for a Go to command.</summary>
        public static List<Cell> Targets(SurfaceMap map, GoTarget target, Cell tile)
        {
            var list = new List<Cell>();
            switch (target)
            {
                case GoTarget.Tile:
                    if (map.IsWalkable(tile)) list.Add(tile);
                    break;
                default:
                    foreach (var c in map.AllCells())
                        if (map.IsWalkable(c) && TryFace(map, c, target, out _)) list.Add(c);
                    break;
            }
            return list;
        }

        /// <summary>
        /// For the stream and the hatch: which way to face from <paramref name="c"/> to look at it
        /// (N, E, S, W preference). False if it isn't next to one.
        /// </summary>
        public static bool TryFace(SurfaceMap map, Cell c, GoTarget target, out Facing dir)
        {
            for (int f = 0; f < 4; f++)
            {
                var n = c.Step((Facing)f);
                bool match = target == GoTarget.Stream ? map.IsWater(n) : target == GoTarget.Hatch && map.IsHatch(n);
                if (!match) continue;
                dir = (Facing)f;
                return true;
            }
            dir = Facing.North;
            return false;
        }

        /// <summary>Walking steps from every cell to the nearest target (-1 = can't get there).</summary>
        public static int[,] DistanceField(SurfaceMap map, List<Cell> targets)
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
        public static bool NextStep(SurfaceMap map, int[,] dist, Cell from, out Cell next, out Facing dir)
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
