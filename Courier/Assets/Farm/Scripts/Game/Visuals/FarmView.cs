using System.Collections.Generic;
using Farm.Sim;
using UnityEngine;

namespace Farm.Game
{
    /// <summary>
    /// Mirrors the farm in the scene with placeholder boxes: ground, soil, crops, buildings, fences,
    /// trees, bots, cursors and the route preview. Cell (x, y) maps to world (x, 0, y), 1 unit per tile.
    /// </summary>
    public sealed class FarmView : MonoBehaviour
    {
        public const float GroundTop = 0f;

        GameController _game;
        World World => _game.World;
        FarmMap Map => _game.World.Map;

        Transform _groundRoot, _cropRoot, _propsRoot, _botsRoot;
        GameObject[,] _soil;
        Transform[,] _crops;
        int[,] _cropKey;

        readonly Dictionary<Bot, BotView> _botViews = new Dictionary<Bot, BotView>();
        readonly Dictionary<Cell, int> _perCell = new Dictionary<Cell, int>();
        readonly Dictionary<Cell, int> _placedPerCell = new Dictionary<Cell, int>();

        CellFrame _hoverCursor;
        CellFrame _selectionCursor;
        RoutePreview _route;

        public static Vector3 CellToWorld(Cell c, float y = 0f) => new Vector3(c.X, y, c.Y);

        public static bool ScreenToCell(Camera cam, Vector2 screenPos, FarmMap map, out Cell cell)
        {
            cell = default;
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float dist)) return false;
            var p = ray.GetPoint(dist);
            cell = new Cell(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            return map.InBounds(cell);
        }

        public void Init(GameController game)
        {
            _game = game;
            _groundRoot = NewChild("Ground");
            _cropRoot = NewChild("Crops");
            _propsRoot = NewChild("Props");
            _botsRoot = NewChild("Bots");

            BuildGround();
            BuildProps();
            foreach (var c in Map.AllCells()) RefreshTile(c);
            foreach (var b in World.Bots) OnBotAdded(b);

            _hoverCursor = new CellFrame(NewChild("Hover Cursor"), Palette.Cursor, 0.04f);
            _selectionCursor = new CellFrame(NewChild("Selection Cursor"), Palette.Selection, 0.06f);
            _route = NewChild("Route Preview").gameObject.AddComponent<RoutePreview>();
            _route.Init(game);

            World.BotAdded += OnBotAdded;
            World.Ticked += OnTicked;
            World.DayStarted += SnapAll;
            World.DayEnded += OnDayEnded;
            World.TileChanged += RefreshTile;
        }

        void OnDestroy()
        {
            if (_game == null || World == null) return;
            World.BotAdded -= OnBotAdded;
            World.Ticked -= OnTicked;
            World.DayStarted -= SnapAll;
            World.DayEnded -= OnDayEnded;
            World.TileChanged -= RefreshTile;
        }

        Transform NewChild(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        // ------------------------------------------------------------------ ground

        void BuildGround()
        {
            _soil = new GameObject[Map.Width, Map.Height];
            _crops = new Transform[Map.Width, Map.Height];
            _cropKey = new int[Map.Width, Map.Height];
            for (int x = 0; x < Map.Width; x++)
            for (int y = 0; y < Map.Height; y++)
                _cropKey[x, y] = -1;

            Shapes.Box("Underlay", _groundRoot, new Vector3((Map.Width - 1) * 0.5f, -0.35f, (Map.Height - 1) * 0.5f),
                new Vector3(Map.Width + 8, 0.5f, Map.Height + 8), Palette.Shade(Palette.Grass, 0.7f), false);

            foreach (var c in Map.AllCells())
            {
                var t = Map[c];
                Color color;
                float h = 0.1f;
                float y = -0.05f;
                switch (t.Ground)
                {
                    case Ground.Dirt: color = Palette.Dirt; break;
                    case Ground.Path: color = Palette.Path; break;
                    case Ground.Water: color = Palette.Water; y = -0.12f; break;
                    default: color = (c.X + c.Y) % 2 == 0 ? Palette.Grass : Palette.GrassAlt; break;
                }
                Shapes.Box($"Ground {c.X},{c.Y}", _groundRoot, new Vector3(c.X, y, c.Y), new Vector3(1f, h, 1f), color, false);

                if (t.Ground == Ground.Dirt)
                {
                    _soil[c.X, c.Y] = Shapes.Box("Soil", _groundRoot, new Vector3(c.X, 0.015f, c.Y), new Vector3(0.88f, 0.04f, 0.88f), Palette.Tilled, false);
                    _soil[c.X, c.Y].SetActive(false);
                }
            }
        }

        void RefreshTile(Cell c)
        {
            var t = Map[c];
            var soil = _soil[c.X, c.Y];
            if (soil != null)
            {
                soil.SetActive(t.Tilled);
                if (t.Tilled) Shapes.Recolor(soil, t.Watered ? Palette.TilledWet : Palette.Tilled);
            }

            // Rebuild the crop model only when its look changes (watering alone doesn't).
            int key = t.HasCrop ? IndexOf(t.Crop) * 1000 + (t.IsRipe ? 999 : t.Growth) : -1;
            if (key == _cropKey[c.X, c.Y]) return;
            _cropKey[c.X, c.Y] = key;
            if (_crops[c.X, c.Y] != null)
            {
                Destroy(_crops[c.X, c.Y].gameObject);
                _crops[c.X, c.Y] = null;
            }
            if (t.HasCrop) _crops[c.X, c.Y] = BuildCrop(c, t);
        }

        static int IndexOf(CropType crop)
        {
            for (int i = 0; i < Crops.All.Count; i++)
                if (Crops.All[i] == crop) return i;
            return 0;
        }

        Transform BuildCrop(Cell c, Tile t)
        {
            var root = new GameObject("Crop").transform;
            root.SetParent(_cropRoot, false);
            root.localPosition = CellToWorld(c, 0.03f);

            if (t.IsRipe)
            {
                var produce = Palette.For(t.Crop);
                float size = t.Crop == Crops.Cauliflower ? 0.42f : t.Crop == Crops.Potato ? 0.3f : 0.26f;
                Shapes.Box("Leaves", root, new Vector3(0f, 0.14f, 0f), new Vector3(0.6f, 0.2f, 0.6f), Palette.Leaf, false);
                Shapes.Box("Produce", root, new Vector3(0f, 0.3f, 0f), new Vector3(size, size * 0.8f, size), produce);
                Shapes.Box("Top", root, new Vector3(0f, 0.3f + size * 0.5f, 0f), new Vector3(0.12f, 0.12f, 0.12f), Palette.LeafDark, false);
            }
            else if (t.Growth == 0)
            {
                Shapes.Box("Seed", root, new Vector3(0f, 0.03f, 0f), new Vector3(0.18f, 0.06f, 0.18f), Palette.Shade(Palette.Tilled, 1.3f), false);
                Shapes.Box("Sprout", root, new Vector3(0f, 0.08f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Palette.Sprout, false);
            }
            else
            {
                float k = t.Growth / (float)t.Crop.DaysToGrow;
                float h = 0.12f + 0.3f * k;
                Shapes.Box("Stem", root, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.08f, h, 0.08f), Palette.Sprout, false);
                Shapes.Box("Leaf L", root, new Vector3(-0.12f, h * 0.6f, 0f), new Vector3(0.22f, 0.05f, 0.14f), Palette.Leaf, false);
                Shapes.Box("Leaf R", root, new Vector3(0.12f, h * 0.8f, 0f), new Vector3(0.22f, 0.05f, 0.14f), Palette.Leaf, false);
            }
            return root;
        }

        // ------------------------------------------------------------------ buildings and scenery

        void BuildProps()
        {
            var done = new HashSet<Cell>();
            foreach (var c in Map.AllCells())
            {
                var s = Map[c].Structure;
                if (s == Structure.None || done.Contains(c)) continue;

                switch (s)
                {
                    case Structure.Fence: BuildFence(c); done.Add(c); break;
                    case Structure.Tree: BuildTree(c); done.Add(c); break;
                    case Structure.Rock:
                        Shapes.Box("Rock", _propsRoot, CellToWorld(c, 0.15f), new Vector3(0.6f, 0.3f, 0.5f), Palette.Stone);
                        done.Add(c);
                        break;
                    default:
                        var group = FloodFill(c, s, done);
                        BuildBuilding(s, group);
                        break;
                }
            }
        }

        List<Cell> FloodFill(Cell start, Structure s, HashSet<Cell> done)
        {
            var group = new List<Cell>();
            var stack = new Stack<Cell>();
            stack.Push(start);
            done.Add(start);
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                group.Add(c);
                for (int f = 0; f < 4; f++)
                {
                    var n = c.Step((Facing)f);
                    if (!Map.InBounds(n) || done.Contains(n) || Map[n].Structure != s) continue;
                    done.Add(n);
                    stack.Push(n);
                }
            }
            return group;
        }

        void BuildBuilding(Structure s, List<Cell> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in cells)
            {
                minX = Mathf.Min(minX, c.X);
                minY = Mathf.Min(minY, c.Y);
                maxX = Mathf.Max(maxX, c.X);
                maxY = Mathf.Max(maxY, c.Y);
            }
            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minY + maxY) * 0.5f);
            var size = new Vector3(maxX - minX + 1, 0f, maxY - minY + 1);
            var root = new GameObject(s.ToString()).transform;
            root.SetParent(_propsRoot, false);
            root.localPosition = center;
            string label = null;

            switch (s)
            {
                case Structure.House:
                    Shapes.Box("Walls", root, new Vector3(0f, 0.7f, 0f), new Vector3(size.x - 0.1f, 1.4f, size.z - 0.1f), Palette.Wall);
                    Shapes.Box("Roof", root, new Vector3(0f, 1.55f, 0f), new Vector3(size.x + 0.2f, 0.35f, size.z + 0.2f), Palette.Roof);
                    Shapes.Box("Ridge", root, new Vector3(0f, 1.85f, 0f), new Vector3(size.x - 0.6f, 0.3f, size.z * 0.5f), Palette.Shade(Palette.Roof, 0.85f));
                    Shapes.Box("Door", root, new Vector3(0f, 0.45f, -size.z * 0.5f), new Vector3(0.5f, 0.8f, 0.06f), Palette.Wood);
                    break;
                case Structure.Well:
                    Shapes.Box("Stones", root, new Vector3(0f, 0.35f, 0f), new Vector3(size.x - 0.3f, 0.7f, size.z - 0.3f), Palette.Stone);
                    Shapes.Box("Water", root, new Vector3(0f, 0.68f, 0f), new Vector3(size.x - 0.7f, 0.04f, size.z - 0.7f), Palette.Water, false);
                    Shapes.Box("Post L", root, new Vector3(-size.x * 0.35f, 1.0f, 0f), new Vector3(0.1f, 0.8f, 0.1f), Palette.Wood);
                    Shapes.Box("Post R", root, new Vector3(size.x * 0.35f, 1.0f, 0f), new Vector3(0.1f, 0.8f, 0.1f), Palette.Wood);
                    Shapes.Box("Roof", root, new Vector3(0f, 1.45f, 0f), new Vector3(size.x, 0.18f, size.z * 0.8f), Palette.Roof);
                    label = "Well";
                    break;
                case Structure.Bin:
                    Shapes.Box("Box", root, new Vector3(0f, 0.3f, 0f), new Vector3(0.85f, 0.6f, 0.75f), Palette.WoodLight);
                    Shapes.Box("Lid", root, new Vector3(0f, 0.63f, 0f), new Vector3(0.9f, 0.08f, 0.8f), Palette.Wood);
                    label = "Shipping bin";
                    break;
                case Structure.Shed:
                    Shapes.Box("Hut", root, new Vector3(0f, 0.4f, 0f), new Vector3(0.85f, 0.8f, 0.85f), Palette.WoodLight);
                    Shapes.Box("Roof", root, new Vector3(0f, 0.88f, 0f), new Vector3(1f, 0.16f, 1f), Palette.Shade(Palette.Roof, 0.9f));
                    label = "Seed shed";
                    break;
            }

            if (label != null)
            {
                var tm = Shapes.Label("Label", root, new Vector3(0f, 1.9f, 0f), label, Color.white, 0.04f);
                tm.gameObject.AddComponent<Billboard>();
            }
        }

        void BuildFence(Cell c)
        {
            var root = new GameObject("Fence").transform;
            root.SetParent(_propsRoot, false);
            root.localPosition = CellToWorld(c);
            Shapes.Box("Post", root, new Vector3(0f, 0.3f, 0f), new Vector3(0.14f, 0.6f, 0.14f), Palette.Wood);
            // Rails to fence neighbours to the east and north (each pair drawn once).
            if (IsFence(c.Step(Facing.East)))
                Shapes.Box("Rail E", root, new Vector3(0.5f, 0.4f, 0f), new Vector3(1f, 0.08f, 0.06f), Palette.WoodLight);
            if (IsFence(c.Step(Facing.North)))
                Shapes.Box("Rail N", root, new Vector3(0f, 0.4f, 0.5f), new Vector3(0.06f, 0.08f, 1f), Palette.WoodLight);
        }

        bool IsFence(Cell c) => Map.InBounds(c) && Map[c].Structure == Structure.Fence;

        void BuildTree(Cell c)
        {
            var root = new GameObject("Tree").transform;
            root.SetParent(_propsRoot, false);
            root.localPosition = CellToWorld(c);
            float k = 0.85f + 0.3f * Mathf.Repeat(c.X * 0.37f + c.Y * 0.71f, 1f);
            Shapes.Box("Trunk", root, new Vector3(0f, 0.4f * k, 0f), new Vector3(0.22f, 0.8f * k, 0.22f), Palette.Wood);
            Shapes.Box("Canopy", root, new Vector3(0f, 1.05f * k, 0f), new Vector3(0.9f, 0.7f * k, 0.9f), Palette.Leaf);
            Shapes.Box("Top", root, new Vector3(0f, 1.5f * k, 0f), new Vector3(0.55f, 0.35f * k, 0.55f), Palette.LeafDark);
        }

        // ------------------------------------------------------------------ bots

        void OnBotAdded(Bot b)
        {
            var go = new GameObject(b.Name);
            go.transform.SetParent(_botsRoot, false);
            var view = go.AddComponent<BotView>();
            view.Init(b, _game);
            _botViews[b] = view;
            SnapAll();
        }

        void OnTicked()
        {
            UpdateOffsets();
            foreach (var v in _botViews.Values) v.OnTick();
        }

        void OnDayEnded(DayReport _) => SnapAll();

        void SnapAll()
        {
            UpdateOffsets();
            foreach (var v in _botViews.Values) v.Snap();
        }

        /// <summary>Fans out bots that share a tile so they don't render on top of each other.</summary>
        void UpdateOffsets()
        {
            _perCell.Clear();
            _placedPerCell.Clear();
            foreach (var b in World.Bots)
            {
                _perCell.TryGetValue(b.Position, out int n);
                _perCell[b.Position] = n + 1;
            }
            foreach (var kv in _botViews)
            {
                var pos = kv.Key.Position;
                int count = _perCell[pos];
                _placedPerCell.TryGetValue(pos, out int index);
                _placedPerCell[pos] = index + 1;
                var offset = Vector3.zero;
                if (count > 1)
                {
                    float angle = index * Mathf.PI * 2f / count + Mathf.PI * 0.25f;
                    offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.2f;
                }
                kv.Value.Offset = offset;
            }
        }

        // ------------------------------------------------------------------ cursors

        void LateUpdate()
        {
            if (_game.HasHover)
            {
                var color = Palette.Cursor;
                if (_game.IsPicking) color = Map.IsWalkable(_game.HoveredCell) ? Palette.Pick : Palette.Invalid;
                _hoverCursor.Show(_game.HoveredCell, color);
            }
            else _hoverCursor.Hide();

            if (_game.HasSelectedTile) _selectionCursor.Show(_game.SelectedTile, Palette.Selection);
            else _selectionCursor.Hide();
        }

        /// <summary>Four thin bars outlining one tile.</summary>
        sealed class CellFrame
        {
            readonly Transform _root;
            readonly GameObject[] _bars = new GameObject[4];

            public CellFrame(Transform root, Color color, float thickness)
            {
                _root = root;
                const float y = 0.12f;
                _bars[0] = Shapes.Box("N", root, new Vector3(0f, y, 0.5f), new Vector3(1f, 0.04f, thickness), color, false);
                _bars[1] = Shapes.Box("S", root, new Vector3(0f, y, -0.5f), new Vector3(1f, 0.04f, thickness), color, false);
                _bars[2] = Shapes.Box("E", root, new Vector3(0.5f, y, 0f), new Vector3(thickness, 0.04f, 1f), color, false);
                _bars[3] = Shapes.Box("W", root, new Vector3(-0.5f, y, 0f), new Vector3(thickness, 0.04f, 1f), color, false);
                Hide();
            }

            public void Show(Cell c, Color color)
            {
                _root.gameObject.SetActive(true);
                _root.localPosition = CellToWorld(c);
                foreach (var b in _bars) Shapes.Recolor(b, color);
            }

            public void Hide() => _root.gameObject.SetActive(false);
        }
    }
}
