using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wizard.Game
{
    /// <summary>
    /// Mirrors the sim in the scene: ground, buildings, constructs, monsters, doom gates, ward beams,
    /// cursors and the route preview. Grid cell (x, y) maps to world (x, 0, y), 1 unit per cell.
    /// </summary>
    public sealed class WorldView : MonoBehaviour
    {
        const float RoadHeight = 0.08f;
        const float LotHeight = 0.16f;
        public const float LotTop = LotHeight - RoadHeight;

        GameController _game;
        Camera _cam;
        World World => _game.World;

        Transform _tilesRoot, _buildingsRoot, _constructsRoot, _monstersRoot, _fxRoot;
        GameObject[,] _tiles;

        readonly Dictionary<Building, BuildingView> _buildingViews = new Dictionary<Building, BuildingView>();
        readonly Dictionary<Construct, ConstructView> _constructViews = new Dictionary<Construct, ConstructView>();
        readonly Dictionary<Monster, MonsterView> _monsterViews = new Dictionary<Monster, MonsterView>();
        readonly Dictionary<Cell, int> _perCell = new Dictionary<Cell, int>();
        readonly Dictionary<Cell, int> _placedPerCell = new Dictionary<Cell, int>();

        CellFrame _hoverCursor;
        CellFrame _selectionCursor;
        GameObject _ghost;
        RoutePreview _route;
        readonly List<Beam> _beams = new List<Beam>();

        public static Vector3 CellToWorld(Cell c, float y = 0f) => new Vector3(c.X, y, c.Y);

        /// <summary>Ray-casts the screen point onto the ground plane and returns the cell under it.</summary>
        public static bool ScreenToCell(Camera cam, Vector2 screenPos, TownGrid grid, out Cell cell)
        {
            cell = default;
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float dist)) return false;
            var p = ray.GetPoint(dist);
            cell = new Cell(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z));
            return grid.InBounds(cell);
        }

        public void Init(GameController game, Camera cam)
        {
            _game = game;
            _cam = cam;
            _tilesRoot = NewChild("Tiles");
            _buildingsRoot = NewChild("Buildings");
            _constructsRoot = NewChild("Constructs");
            _monstersRoot = NewChild("Monsters");
            _fxRoot = NewChild("FX");

            BuildTiles();
            BuildGates();
            foreach (var b in World.Buildings) OnBuildingAdded(b);
            foreach (var c in World.Constructs) OnConstructAdded(c);

            _hoverCursor = new CellFrame(NewChild("Hover Cursor"), Palette.Cursor, 0.04f);
            _selectionCursor = new CellFrame(NewChild("Selection Cursor"), Palette.Selection, 0.06f);
            _ghost = Shapes.Box("Placement Ghost", transform, Vector3.zero, new Vector3(0.7f, 0.35f, 0.7f), Palette.Valid, false);
            _ghost.SetActive(false);

            _route = NewChild("Route Preview").gameObject.AddComponent<RoutePreview>();
            _route.Init(game);

            World.BuildingAdded += OnBuildingAdded;
            World.ConstructAdded += OnConstructAdded;
            World.Ticked += OnTicked;
            World.DayStarted += OnDayStarted;
            World.DayEnded += OnDayEnded;
            World.GameOver += OnGameOver;
            World.MonsterSpawned += OnMonsterSpawned;
            World.MonsterRemoved += OnMonsterRemoved;
            World.Shot += OnShot;
            World.TowerDamaged += OnTowerDamaged;
            World.Grid.Expanded += RefreshTiles;
        }

        void OnDestroy()
        {
            if (_game == null || World == null) return;
            World.BuildingAdded -= OnBuildingAdded;
            World.ConstructAdded -= OnConstructAdded;
            World.Ticked -= OnTicked;
            World.DayStarted -= OnDayStarted;
            World.DayEnded -= OnDayEnded;
            World.GameOver -= OnGameOver;
            World.MonsterSpawned -= OnMonsterSpawned;
            World.MonsterRemoved -= OnMonsterRemoved;
            World.Shot -= OnShot;
            World.TowerDamaged -= OnTowerDamaged;
            World.Grid.Expanded -= RefreshTiles;
        }

        Transform NewChild(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        // ------------------------------------------------------------------ ground & gates

        void BuildTiles()
        {
            var grid = World.Grid;
            _tiles = new GameObject[grid.Width, grid.Height];

            var center = new Vector3((grid.Width - 1) * 0.5f, -RoadHeight - 0.1f, (grid.Height - 1) * 0.5f);
            Shapes.Box("Ground", _tilesRoot, center, new Vector3(grid.Width + 6, 0.2f, grid.Height + 6), Palette.Ground, false);

            for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var c = new Cell(x, y);
                bool road = grid.KindAt(c) == CellKind.Road;
                float h = road ? RoadHeight : LotHeight;
                var size = road ? new Vector3(1f, h, 1f) : new Vector3(0.94f, h, 0.94f);
                _tiles[x, y] = Shapes.Box($"Tile {x},{y}", _tilesRoot, new Vector3(x, h * 0.5f - RoadHeight, y), size, Palette.Road, false);
            }
            RefreshTiles();
        }

        void RefreshTiles()
        {
            var grid = World.Grid;
            for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var c = new Cell(x, y);
                bool road = grid.KindAt(c) == CellKind.Road;
                bool open = grid.IsUnlocked(c);
                var color = road ? (open ? Palette.Road : Palette.RoadLocked) : (open ? Palette.Lot : Palette.LotLocked);
                Shapes.Recolor(_tiles[x, y], color);
            }
        }

        void BuildGates()
        {
            foreach (var gate in World.Doom.Gates)
            {
                var cell = World.GateCell(gate);
                var root = NewChild("Doom Gate " + gate);
                root.localPosition = CellToWorld(cell);
                var side = CellToWorld(gate.TurnRight().ToOffset()) * 0.42f;
                Shapes.Box("Pillar L", root, side + Vector3.up * 0.6f, new Vector3(0.18f, 1.2f, 0.18f), Palette.Doom);
                Shapes.Box("Pillar R", root, -side + Vector3.up * 0.6f, new Vector3(0.18f, 1.2f, 0.18f), Palette.Doom);
                var lintel = gate == Facing.North || gate == Facing.South ? new Vector3(1.0f, 0.16f, 0.18f) : new Vector3(0.18f, 0.16f, 1.0f);
                Shapes.Box("Lintel", root, Vector3.up * 1.25f, lintel, Palette.DoomGlow);
                var label = Shapes.Label("Label", root, Vector3.up * 1.5f, "Doom Gate", Palette.Doom, 0.045f);
                label.gameObject.AddComponent<Billboard>();
            }
        }

        // ------------------------------------------------------------------ entities

        void OnBuildingAdded(Building b)
        {
            var go = new GameObject(b.ToString());
            go.transform.SetParent(_buildingsRoot, false);
            go.transform.localPosition = CellToWorld(b.Cell, LotTop);
            var view = go.AddComponent<BuildingView>();
            view.Init(b, _game);
            _buildingViews[b] = view;
        }

        void OnConstructAdded(Construct c)
        {
            var go = new GameObject(c.Name);
            go.transform.SetParent(_constructsRoot, false);
            var view = go.AddComponent<ConstructView>();
            view.Init(c, _game);
            _constructViews[c] = view;
            SnapAll();
        }

        void OnMonsterSpawned(Monster m)
        {
            var go = new GameObject(m.ToString());
            go.transform.SetParent(_monstersRoot, false);
            var view = go.AddComponent<MonsterView>();
            view.Init(m, _game);
            _monsterViews[m] = view;
        }

        void OnMonsterRemoved(Monster m, bool killed)
        {
            if (!_monsterViews.TryGetValue(m, out var view)) return;
            _monsterViews.Remove(m);
            view.Die(killed);
        }

        void OnShot(Building shooter, Monster target)
        {
            float height = shooter.Type.IsTower ? 1.9f : 1.1f;
            var from = CellToWorld(shooter.Cell, LotTop + height);
            var to = _monsterViews.TryGetValue(target, out var mv) ? mv.transform.position + Vector3.up * 0.3f : CellToWorld(target.Cell, 0.3f);
            GetBeam().Fire(from, to);
        }

        void OnTowerDamaged(int amount)
        {
            if (_buildingViews.TryGetValue(World.Tower, out var v)) v.Flash();
        }

        Beam GetBeam()
        {
            foreach (var b in _beams)
                if (!b.Busy) return b;
            var beam = new GameObject("Beam").AddComponent<Beam>();
            beam.transform.SetParent(_fxRoot, false);
            _beams.Add(beam);
            return beam;
        }

        void OnTicked()
        {
            UpdateConstructOffsets();
            foreach (var v in _constructViews.Values) v.OnTick();
            foreach (var v in _buildingViews.Values) v.Refresh();
            foreach (var v in _monsterViews.Values) v.OnTick();
        }

        void OnDayStarted()
        {
            if (World.IsDoomDay) GameBootstrap.SetupLighting(doom: true);
            SnapAll();
        }

        void OnDayEnded(DayReport _) => SnapAll();

        void OnGameOver(Outcome outcome)
        {
            GameBootstrap.SetupLighting(doom: outcome == Outcome.Defeat);
            foreach (var v in _monsterViews.Values) v.Die(false);
            _monsterViews.Clear();
            SnapAll();
        }

        void SnapAll()
        {
            UpdateConstructOffsets();
            foreach (var v in _constructViews.Values) v.Snap();
            foreach (var v in _buildingViews.Values) v.Refresh();
        }

        /// <summary>Fans out constructs that share a cell so they don't render on top of each other.</summary>
        void UpdateConstructOffsets()
        {
            _perCell.Clear();
            _placedPerCell.Clear();
            foreach (var c in World.Constructs)
            {
                _perCell.TryGetValue(c.Position, out int n);
                _perCell[c.Position] = n + 1;
            }
            foreach (var kv in _constructViews)
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
            bool hover = _game.HasHover;
            var cell = _game.HoveredCell;

            if (_game.PlacingType != null && hover)
            {
                var check = World.CheckPlacement(_game.PlacingType, cell);
                _ghost.SetActive(true);
                _ghost.transform.localPosition = CellToWorld(cell, LotTop + 0.2f);
                Shapes.Recolor(_ghost, check == PlacementCheck.Ok ? Palette.For(_game.PlacingType) : Palette.Invalid);
                _hoverCursor.Show(cell, check == PlacementCheck.Ok ? Palette.Valid : Palette.Invalid);
            }
            else
            {
                _ghost.SetActive(false);
                if (hover) _hoverCursor.Show(cell, Palette.Cursor);
                else _hoverCursor.Hide();
            }

            var sel = _game.SelectedBuilding;
            if (sel != null) _selectionCursor.Show(sel.Cell, Palette.Selection);
            else _selectionCursor.Hide();
        }

        /// <summary>Four thin bars outlining one cell.</summary>
        sealed class CellFrame
        {
            readonly Transform _root;
            readonly GameObject[] _bars = new GameObject[4];

            public CellFrame(Transform root, Color color, float thickness)
            {
                _root = root;
                const float y = 0.2f;
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
