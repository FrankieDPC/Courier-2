using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// Mirrors the surface in the scene with placeholder boxes: ground, overgrowth, ancient tech, the
    /// bunker, the stream, crops, bots, cursors and the route preview. Cell (x, y) maps to world
    /// (x, 0, y), 1 unit per tile.
    /// </summary>
    public sealed class SurfaceView : MonoBehaviour
    {
        GameController _game;
        World World => _game.World;
        SurfaceMap Map => _game.World.Map;

        Transform _groundRoot, _debrisRoot, _cropRoot, _propsRoot, _botsRoot, _popupRoot;
        GameObject[,] _ground;
        GameObject[,] _soil;
        GameObject[,] _debris;
        Transform[,] _crops;
        int[,] _cropKey;

        readonly Dictionary<Bot, BotView> _botViews = new Dictionary<Bot, BotView>();
        readonly Dictionary<Cell, int> _perCell = new Dictionary<Cell, int>();
        readonly Dictionary<Cell, int> _placedPerCell = new Dictionary<Cell, int>();

        CellFrame _hoverCursor;
        CellFrame _selectionCursor;
        CellFrame _aheadCursor;
        RoutePreview _route;

        public static Vector3 CellToWorld(Cell c, float y = 0f) => new Vector3(c.X, y, c.Y);

        public static bool ScreenToCell(Camera cam, Vector2 screenPos, SurfaceMap map, out Cell cell)
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
            _debrisRoot = NewChild("Overgrowth");
            _cropRoot = NewChild("Crops");
            _propsRoot = NewChild("Bunker");
            _botsRoot = NewChild("Bots");
            _popupRoot = NewChild("Popups");

            BuildGround();
            BuildBunker();
            foreach (var c in Map.AllCells()) RefreshTile(c);
            foreach (var b in World.Bots) OnBotAdded(b);

            _hoverCursor = new CellFrame(NewChild("Hover Cursor"), Palette.Cursor, 0.04f);
            _selectionCursor = new CellFrame(NewChild("Selection Cursor"), Palette.Selection, 0.06f);
            _aheadCursor = new CellFrame(NewChild("Ahead Cursor"), Palette.Ahead, 0.05f);
            _route = NewChild("Route Preview").gameObject.AddComponent<RoutePreview>();
            _route.Init(game);

            World.BotAdded += OnBotAdded;
            World.Ticked += OnTicked;
            World.DayStarted += SnapAll;
            World.DayEnded += OnDayEnded;
            World.TileChanged += RefreshTile;
            World.Salvaged += OnSalvaged;
            _game.DataChanged += RefreshAllTiles;
            _game.DrawnCell = b => _botViews.TryGetValue(b, out var v) ? v.DrawnCell : b.Position;
        }

        void RefreshAllTiles()
        {
            foreach (var c in Map.AllCells()) RefreshTile(c);
        }

        void OnDestroy()
        {
            if (_game == null || World == null) return;
            World.BotAdded -= OnBotAdded;
            World.Ticked -= OnTicked;
            World.DayStarted -= SnapAll;
            World.DayEnded -= OnDayEnded;
            World.TileChanged -= RefreshTile;
            World.Salvaged -= OnSalvaged;
            _game.DataChanged -= RefreshAllTiles;
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
            int w = Map.Width, h = Map.Height;
            _ground = new GameObject[w, h];
            _soil = new GameObject[w, h];
            _debris = new GameObject[w, h];
            _crops = new Transform[w, h];
            _cropKey = new int[w, h];
            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                _cropKey[x, y] = int.MinValue;

            Shapes.Box("Underlay", _groundRoot, new Vector3((w - 1) * 0.5f, -0.35f, (h - 1) * 0.5f),
                new Vector3(w + 10, 0.5f, h + 10), Palette.Shade(Palette.Moss, 0.55f), false);

            foreach (var c in Map.AllCells())
            {
                var t = Map[c];
                bool water = t.Ground == Ground.Water;
                _ground[c.X, c.Y] = Shapes.Box($"Ground {c.X},{c.Y}", _groundRoot, new Vector3(c.X, water ? -0.14f : -0.05f, c.Y), new Vector3(1f, 0.1f, 1f), GroundColor(c, t), false);
                if (water)
                {
                    // A faint ripple stripe so the stream reads as water.
                    if ((c.X * 3 + c.Y) % 4 == 0)
                        Shapes.Box("Ripple", _groundRoot, new Vector3(c.X, -0.085f, c.Y + 0.2f), new Vector3(0.5f, 0.01f, 0.06f), Palette.Shade(Palette.Water, 1.25f), false);
                    continue;
                }
                if (t.Ground == Ground.Soil)
                {
                    _soil[c.X, c.Y] = Shapes.Box("Tilled", _groundRoot, new Vector3(c.X, 0.015f, c.Y), new Vector3(0.88f, 0.04f, 0.88f), Palette.Tilled, false);
                    _soil[c.X, c.Y].SetActive(false);
                }
                else if (t.Ground == Ground.Concrete && t.Structure == Structure.None && (c.X * 7 + c.Y * 3) % 5 == 0)
                {
                    // Cracks with moss in them.
                    Shapes.Box("Crack", _groundRoot, new Vector3(c.X + 0.1f, 0.005f, c.Y - 0.15f), new Vector3(0.55f, 0.01f, 0.07f), Palette.MossAlt, false);
                }
            }
        }

        Color GroundColor(Cell c, Tile t)
        {
            bool alt = (c.X + c.Y) % 2 == 0;
            switch (t.Ground)
            {
                case Ground.Water: return (c.X + c.Y) % 3 == 0 ? Palette.WaterDeep : Palette.Water;
                case Ground.Concrete:
                    var concrete = alt ? Palette.Concrete : Palette.ConcreteAlt;
                    return t.IsOvergrown ? Color.Lerp(concrete, Palette.Moss, 0.35f) : concrete; // weeds through the cracks
                default:
                    if (t.IsOvergrown) return alt ? Palette.Moss : Palette.MossAlt;
                    return alt ? Palette.Soil : Palette.SoilAlt;
            }
        }

        void RefreshTile(Cell c)
        {
            var t = Map[c];
            Shapes.Recolor(_ground[c.X, c.Y], GroundColor(c, t));

            var soil = _soil[c.X, c.Y];
            if (soil != null)
            {
                soil.SetActive(t.Tilled && !t.IsOvergrown);
                if (t.Tilled) Shapes.Recolor(soil, SoilColor(t));
            }

            // Overgrowth and old tech.
            if (t.IsOvergrown && _debris[c.X, c.Y] == null) _debris[c.X, c.Y] = BuildDebris(c, t.Debris);
            else if (!t.IsOvergrown && _debris[c.X, c.Y] != null)
            {
                Destroy(_debris[c.X, c.Y]);
                _debris[c.X, c.Y] = null;
            }

            // Rebuild the crop model only when its look changes: a new growth stage (watering alone doesn't).
            int key = t.HasCrop ? IndexOf(t.Crop) * 10 + t.Stage : -1;
            if (key == _cropKey[c.X, c.Y]) return;
            _cropKey[c.X, c.Y] = key;
            if (_crops[c.X, c.Y] != null)
            {
                Destroy(_crops[c.X, c.Y].gameObject);
                _crops[c.X, c.Y] = null;
            }
            if (t.HasCrop) _crops[c.X, c.Y] = BuildCrop(c, t);
        }

        /// <summary>Tilled soil darkens with water: dry, damp (time to water again), wet.</summary>
        static Color SoilColor(Tile t)
        {
            switch (t.WetLevel)
            {
                case 2: return Palette.TilledWet;
                case 1: return Palette.TilledDamp;
                default: return Palette.Tilled;
            }
        }

        static int IndexOf(CropType crop)
        {
            for (int i = 0; i < Crops.All.Count; i++)
                if (Crops.All[i] == crop) return i;
            return 0;
        }

        /// <summary>A stable 0..1 per tile, to vary sizes and rotations.</summary>
        static float Jitter(Cell c, int salt = 0) => Mathf.Repeat(c.X * 0.6180339f + c.Y * 0.4142135f + salt * 0.2718281f, 1f);

        GameObject BuildDebris(Cell c, Debris d)
        {
            var root = new GameObject(d.ToString()).transform;
            root.SetParent(_debrisRoot, false);
            root.localPosition = CellToWorld(c);
            root.localRotation = Quaternion.Euler(0f, (Jitter(c, 1) - 0.5f) * 40f, 0f);
            float k = 0.85f + 0.3f * Jitter(c);

            switch (d)
            {
                case Debris.Weeds:
                    Shapes.Box("Tuft A", root, new Vector3(-0.2f, 0.12f * k, -0.1f), new Vector3(0.12f, 0.24f * k, 0.12f), Palette.Weed, false);
                    Shapes.Box("Tuft B", root, new Vector3(0.15f, 0.16f * k, 0.12f), new Vector3(0.14f, 0.32f * k, 0.12f), Palette.WeedDark, false);
                    Shapes.Box("Tuft C", root, new Vector3(0.05f, 0.1f * k, -0.25f), new Vector3(0.18f, 0.2f * k, 0.1f), Palette.Weed, false);
                    Shapes.Box("Tuft D", root, new Vector3(-0.22f, 0.09f * k, 0.22f), new Vector3(0.1f, 0.18f * k, 0.14f), Palette.WeedDark, false);
                    if (Jitter(c, 2) > 0.6f) Shapes.Box("Flower", root, new Vector3(0.15f, 0.34f * k, 0.12f), new Vector3(0.09f, 0.06f, 0.09f), Palette.Flower, false);
                    break;
                case Debris.Thicket:
                    Shapes.Box("Bush", root, new Vector3(0f, 0.25f * k, 0f), new Vector3(0.85f, 0.5f * k, 0.8f), Palette.Thicket);
                    Shapes.Box("Bush Top", root, new Vector3(0.1f, 0.55f * k, -0.05f), new Vector3(0.55f, 0.3f * k, 0.5f), Palette.ThicketDark);
                    Shapes.Box("Thorns", root, new Vector3(-0.3f, 0.45f * k, 0.25f), new Vector3(0.2f, 0.2f, 0.2f), Palette.WeedDark, false);
                    break;
                case Debris.Sapling:
                    Shapes.Box("Stem", root, new Vector3(0f, 0.35f * k, 0f), new Vector3(0.1f, 0.7f * k, 0.1f), Palette.Trunk);
                    Shapes.Box("Leaves", root, new Vector3(0f, 0.8f * k, 0f), new Vector3(0.5f, 0.4f * k, 0.5f), Palette.Leaf);
                    break;
                case Debris.Tree:
                    Shapes.Box("Trunk", root, new Vector3(0f, 0.5f * k, 0f), new Vector3(0.3f, 1f * k, 0.3f), Palette.Trunk);
                    Shapes.Box("Canopy", root, new Vector3(0f, 1.25f * k, 0f), new Vector3(1.05f, 0.8f * k, 1.05f), Palette.Leaf);
                    Shapes.Box("Crown", root, new Vector3(0.05f, 1.8f * k, -0.05f), new Vector3(0.65f, 0.45f * k, 0.65f), Palette.LeafDark);
                    Shapes.Box("Vine", root, new Vector3(0.16f, 0.4f * k, 0.1f), new Vector3(0.05f, 0.7f * k, 0.12f), Palette.Weed, false);
                    break;
                case Debris.Rubble:
                    Shapes.Box("Slab", root, new Vector3(-0.1f, 0.12f, 0f), new Vector3(0.6f, 0.24f, 0.45f), Palette.ConcreteDark);
                    Shapes.Box("Chunk", root, new Vector3(0.22f, 0.1f, 0.2f), new Vector3(0.3f, 0.2f, 0.3f), Palette.Concrete);
                    Shapes.Box("Rebar A", root, new Vector3(0f, 0.32f, -0.05f), new Vector3(0.04f, 0.3f, 0.04f), Palette.Rust, false);
                    Shapes.Box("Rebar B", root, new Vector3(-0.2f, 0.3f, 0.1f), new Vector3(0.35f, 0.04f, 0.04f), Palette.RustDark, false);
                    Shapes.Box("Moss", root, new Vector3(-0.15f, 0.25f, -0.05f), new Vector3(0.3f, 0.03f, 0.25f), Palette.MossAlt, false);
                    break;
                case Debris.Wreck:
                    // A rusted-out car, half swallowed by vines.
                    Shapes.Box("Body", root, new Vector3(0f, 0.2f, 0f), new Vector3(0.9f, 0.26f, 0.55f), Palette.Rust);
                    Shapes.Box("Cabin", root, new Vector3(-0.05f, 0.42f, 0f), new Vector3(0.5f, 0.2f, 0.5f), Palette.RustDark);
                    Shapes.Box("Windows", root, new Vector3(-0.05f, 0.43f, 0f), new Vector3(0.52f, 0.12f, 0.42f), Palette.Glass, false);
                    Shapes.Box("Wheel F", root, new Vector3(0.28f, 0.07f, 0f), new Vector3(0.18f, 0.14f, 0.6f), Palette.HazardDark, false);
                    Shapes.Box("Wheel B", root, new Vector3(-0.28f, 0.07f, 0f), new Vector3(0.18f, 0.14f, 0.6f), Palette.HazardDark, false);
                    Shapes.Box("Vines", root, new Vector3(0.15f, 0.535f, 0.05f), new Vector3(0.45f, 0.03f, 0.35f), Palette.Weed, false);
                    break;
                case Debris.Terminal:
                    Shapes.Box("Cabinet", root, new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.9f, 0.4f), Palette.MetalDark);
                    Shapes.Box("Screen", root, new Vector3(0f, 0.62f, -0.205f), new Vector3(0.36f, 0.26f, 0.02f), Palette.Shade(Palette.Screen, 0.55f), false);
                    Shapes.Box("Keys", root, new Vector3(0f, 0.38f, -0.25f), new Vector3(0.42f, 0.05f, 0.15f), Palette.Metal, false);
                    Shapes.Box("Moss Cap", root, new Vector3(0.05f, 0.91f, 0f), new Vector3(0.45f, 0.04f, 0.35f), Palette.Moss, false);
                    break;
                case Debris.SeedCache:
                    // An old sealed seed-vault canister.
                    Shapes.Box("Canister", root, new Vector3(0f, 0.25f, 0f), new Vector3(0.45f, 0.5f, 0.45f), Palette.Metal);
                    Shapes.Box("Band", root, new Vector3(0f, 0.3f, 0f), new Vector3(0.47f, 0.08f, 0.47f), Palette.Hazard, false);
                    Shapes.Box("Lid", root, new Vector3(0f, 0.53f, 0f), new Vector3(0.3f, 0.06f, 0.3f), Palette.MetalDark, false);
                    Shapes.Box("Leaf", root, new Vector3(0.18f, 0.12f, -0.22f), new Vector3(0.2f, 0.18f, 0.05f), Palette.Sprout, false);
                    break;
            }
            return root.gameObject;
        }

        Transform BuildCrop(Cell c, Tile t)
        {
            var root = new GameObject("Crop").transform;
            root.SetParent(_cropRoot, false);
            root.localPosition = CellToWorld(c, 0.03f);

            if (t.IsRipe)
            {
                var produce = Palette.For(t.Crop);
                if (t.Crop == Crops.Squash)
                {
                    Shapes.Box("Vine", root, new Vector3(0f, 0.06f, 0f), new Vector3(0.7f, 0.1f, 0.7f), Palette.Leaf, false);
                    Shapes.Box("Squash", root, new Vector3(0.05f, 0.22f, 0f), new Vector3(0.45f, 0.3f, 0.4f), produce);
                    Shapes.Box("Stalk", root, new Vector3(0.05f, 0.4f, 0f), new Vector3(0.06f, 0.08f, 0.06f), Palette.LeafDark, false);
                }
                else
                {
                    float size = t.Crop == Crops.Potato ? 0.3f : 0.22f;
                    Shapes.Box("Leaves", root, new Vector3(0f, 0.14f, 0f), new Vector3(0.55f, 0.2f, 0.55f), Palette.Leaf, false);
                    Shapes.Box("Produce", root, new Vector3(0f, 0.28f, 0f), new Vector3(size, size * 0.85f, size), produce);
                    Shapes.Box("Top", root, new Vector3(0f, 0.28f + size * 0.5f, 0f), new Vector3(0.1f, 0.12f, 0.1f), Palette.LeafDark, false);
                }
            }
            else if (t.Stage <= 0)
            {
                Shapes.Box("Seed", root, new Vector3(0f, 0.03f, 0f), new Vector3(0.18f, 0.06f, 0.18f), Palette.Shade(Palette.Tilled, 1.4f), false);
                Shapes.Box("Sprout", root, new Vector3(0f, 0.08f, 0f), new Vector3(0.05f, 0.1f, 0.05f), Palette.Sprout, false);
            }
            else
            {
                float k = t.Stage / 4f;
                float h = 0.12f + 0.3f * k;
                Shapes.Box("Stem", root, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.08f, h, 0.08f), Palette.Sprout, false);
                Shapes.Box("Leaf L", root, new Vector3(-0.12f, h * 0.6f, 0f), new Vector3(0.22f, 0.05f, 0.14f), Palette.Leaf, false);
                Shapes.Box("Leaf R", root, new Vector3(0.12f, h * 0.8f, 0f), new Vector3(0.22f, 0.05f, 0.14f), Palette.Leaf, false);
            }
            return root;
        }

        // ------------------------------------------------------------------ the bunker

        void BuildBunker()
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            bool any = false;
            foreach (var c in Map.AllCells())
            {
                if (Map[c].Structure == Structure.None) continue;
                any = true;
                minX = Mathf.Min(minX, c.X);
                minY = Mathf.Min(minY, c.Y);
                maxX = Mathf.Max(maxX, c.X);
                maxY = Mathf.Max(maxY, c.Y);
            }
            if (!any) return;

            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minY + maxY) * 0.5f);
            var size = new Vector3(maxX - minX + 1, 0f, maxY - minY + 1);
            var root = new GameObject("Bunker").transform;
            root.SetParent(_propsRoot, false);
            root.localPosition = center;

            // A squat concrete block half buried in the hillside, with moss on the roof.
            Shapes.Box("Walls", root, new Vector3(0f, 0.6f, 0f), new Vector3(size.x - 0.05f, 1.2f, size.z - 0.05f), Palette.ConcreteDark);
            Shapes.Box("Roof", root, new Vector3(0f, 1.27f, 0f), new Vector3(size.x + 0.15f, 0.15f, size.z + 0.15f), Palette.Concrete);
            Shapes.Box("Moss", root, new Vector3(-size.x * 0.15f, 1.36f, size.z * 0.1f), new Vector3(size.x * 0.55f, 0.04f, size.z * 0.6f), Palette.Moss, false);
            Shapes.Box("Vent", root, new Vector3(size.x * 0.32f, 1.6f, size.z * 0.15f), new Vector3(0.3f, 0.5f, 0.3f), Palette.Metal);
            Shapes.Box("Vent Cap", root, new Vector3(size.x * 0.32f, 1.88f, size.z * 0.15f), new Vector3(0.45f, 0.06f, 0.45f), Palette.MetalDark);
            Shapes.Box("Antenna", root, new Vector3(-size.x * 0.38f, 2.1f, size.z * 0.25f), new Vector3(0.05f, 1.6f, 0.05f), Palette.Metal, false);
            Shapes.Box("Vines", root, new Vector3(size.x * 0.48f, 0.6f, 0f), new Vector3(0.06f, 1.1f, size.z * 0.4f), Palette.Weed, false);

            var hatch = Map.Hatch;
            var hatchRoot = new GameObject("Hatch").transform;
            hatchRoot.SetParent(_propsRoot, false);
            hatchRoot.localPosition = CellToWorld(hatch);
            var toHome = Map.HomeFacing.Reverse(); // the hatch faces out towards home
            hatchRoot.localRotation = Quaternion.Euler(0f, toHome.ToYawDegrees(), 0f);
            Shapes.Box("Frame", hatchRoot, new Vector3(0f, 0.55f, 0.47f), new Vector3(0.95f, 1.1f, 0.1f), Palette.HazardDark);
            Shapes.Box("Door", hatchRoot, new Vector3(0f, 0.5f, 0.52f), new Vector3(0.7f, 0.85f, 0.06f), Palette.Metal);
            Shapes.Box("Stripe", hatchRoot, new Vector3(0f, 0.95f, 0.53f), new Vector3(0.95f, 0.1f, 0.06f), Palette.Hazard, false);
            Shapes.Box("Wheel", hatchRoot, new Vector3(0f, 0.5f, 0.56f), new Vector3(0.28f, 0.28f, 0.04f), Palette.MetalDark, false);
            Shapes.Box("Lamp", hatchRoot, new Vector3(0f, 1.15f, 0.53f), new Vector3(0.12f, 0.08f, 0.08f), Palette.Screen, false);

            var label = Shapes.Label("Label", hatchRoot, new Vector3(0f, 1.6f, 0f), "Hatch", Color.white, 0.04f);
            label.gameObject.AddComponent<Billboard>();
        }

        // ------------------------------------------------------------------ bots

        void OnBotAdded(Bot b)
        {
            var go = new GameObject(b.Name);
            go.transform.SetParent(_botsRoot, false);
            var view = go.AddComponent<BotView>();
            view.Init(b, _game);
            _botViews[b] = view;
            UpdateOffsets();
            view.Snap(); // the others keep gliding; they pick up their new fan-out on the next tick
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
                if (b.IsPlayer) continue; // you're drawn wherever you actually are
                _perCell.TryGetValue(b.Position, out int n);
                _perCell[b.Position] = n + 1;
            }
            foreach (var kv in _botViews)
            {
                if (kv.Key.IsPlayer) continue;
                var pos = kv.Key.Position;
                int count = _perCell[pos];
                _placedPerCell.TryGetValue(pos, out int index);
                _placedPerCell[pos] = index + 1;
                var offset = Vector3.zero;
                if (count > 1)
                {
                    float angle = index * Mathf.PI * 2f / count + Mathf.PI * 0.25f;
                    offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.22f;
                }
                kv.Value.Offset = offset;
            }
        }

        /// <summary>"+1 fibre" floating up from what you just cleared.</summary>
        void OnSalvaged(Bot b, Cell cell, List<ItemAmount> yield)
        {
            if (!b.IsPlayer || yield.Count == 0) return;
            for (int i = 0; i < yield.Count; i++)
            {
                var a = yield[i];
                var tm = Shapes.Label("Popup", _popupRoot, CellToWorld(cell, 0.8f + i * 0.32f), $"+{a.Amount} {ItemInfo.Name(a.Item, a.Amount)}", Palette.For(a.Item), 0.035f);
                tm.gameObject.AddComponent<Billboard>();
                tm.gameObject.AddComponent<Popup>();
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

            // The tile in front of you: where E works.
            var p = World.Player;
            if (p != null && !World.IsGameOver && !_game.InNight && Map.InBounds(World.Ahead(p)))
                _aheadCursor.Show(World.Ahead(p), Palette.Ahead);
            else _aheadCursor.Hide();
        }

        /// <summary>Four thin bars outlining one tile.</summary>
        sealed class CellFrame
        {
            readonly Transform _root;
            readonly GameObject[] _bars = new GameObject[4];
            Color _color;

            public CellFrame(Transform root, Color color, float thickness)
            {
                _root = root;
                _color = color;
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
                if (color == _color) return;
                _color = color;
                foreach (var b in _bars) Shapes.Recolor(b, color);
            }

            public void Hide() => _root.gameObject.SetActive(false);
        }
    }

    /// <summary>Floats a world label up and fades it out, then removes it.</summary>
    public sealed class Popup : MonoBehaviour
    {
        const float Life = 1.2f;
        float _age;
        TextMesh _text;
        Color _color;

        void Awake()
        {
            _text = GetComponent<TextMesh>();
            _color = _text != null ? _text.color : Color.white;
        }

        void Update()
        {
            _age += Time.deltaTime;
            transform.localPosition += Vector3.up * (0.6f * Time.deltaTime);
            if (_text != null)
            {
                var c = _color;
                c.a = Mathf.Clamp01(1f - (_age - Life * 0.5f) / (Life * 0.5f));
                _text.color = c;
            }
            if (_age >= Life) Destroy(gameObject);
        }
    }
}
