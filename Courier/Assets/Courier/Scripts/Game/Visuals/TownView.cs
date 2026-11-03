using System.Collections.Generic;
using Courier.Sim;
using UnityEngine;

namespace Courier.Game
{
    /// <summary>
    /// Mirrors the sim in the scene: ground tiles, buildings, bots, cursors and the route preview.
    /// Grid cell (x, y) maps to world (x, 0, y) with 1 unit per cell.
    /// </summary>
    public sealed class TownView : MonoBehaviour
    {
        const float RoadHeight = 0.08f;
        const float LotHeight = 0.16f;
        public const float LotTop = LotHeight - RoadHeight; // lots sit slightly above the road surface

        GameController _game;
        Town Town => _game.Town;

        Transform _tilesRoot;
        Transform _buildingsRoot;
        Transform _botsRoot;
        GameObject[,] _tiles;

        readonly Dictionary<Building, BuildingView> _buildingViews = new Dictionary<Building, BuildingView>();
        readonly Dictionary<CourierBot, BotView> _botViews = new Dictionary<CourierBot, BotView>();
        readonly Dictionary<Cell, int> _botsPerCell = new Dictionary<Cell, int>();
        readonly Dictionary<Cell, int> _botsPlacedPerCell = new Dictionary<Cell, int>();

        CellFrame _hoverCursor;
        CellFrame _selectionCursor;
        GameObject _ghost;
        RoutePreview _route;

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

        public void Init(GameController game)
        {
            _game = game;
            _tilesRoot = NewChild("Tiles");
            _buildingsRoot = NewChild("Buildings");
            _botsRoot = NewChild("Bots");

            BuildTiles();
            foreach (var b in Town.Buildings) OnBuildingAdded(b);
            foreach (var bot in Town.Bots) OnBotAdded(bot);

            _hoverCursor = new CellFrame(NewChild("Hover Cursor"), Palette.Cursor, 0.04f);
            _selectionCursor = new CellFrame(NewChild("Selection Cursor"), Palette.Selection, 0.06f);
            _ghost = Shapes.Box("Placement Ghost", transform, Vector3.zero, new Vector3(0.7f, 0.35f, 0.7f), Palette.Valid, false);
            _ghost.SetActive(false);

            _route = NewChild("Route Preview").gameObject.AddComponent<RoutePreview>();
            _route.Init(game);

            Town.BuildingAdded += OnBuildingAdded;
            Town.BotAdded += OnBotAdded;
            Town.Ticked += OnTicked;
            Town.DayStarted += SnapAll;
            Town.DayEnded += OnDayEnded;
            Town.Grid.Expanded += RefreshTiles;
        }

        void OnDestroy()
        {
            if (_game == null || Town == null) return;
            Town.BuildingAdded -= OnBuildingAdded;
            Town.BotAdded -= OnBotAdded;
            Town.Ticked -= OnTicked;
            Town.DayStarted -= SnapAll;
            Town.DayEnded -= OnDayEnded;
            Town.Grid.Expanded -= RefreshTiles;
        }

        Transform NewChild(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        // ------------------------------------------------------------------ tiles

        void BuildTiles()
        {
            var grid = Town.Grid;
            _tiles = new GameObject[grid.Width, grid.Height];

            // A big slab under everything so the locked map edge doesn't float in space.
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
            var grid = Town.Grid;
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

        // ------------------------------------------------------------------ buildings & bots

        void OnBuildingAdded(Building b)
        {
            var go = new GameObject(b.ToString());
            go.transform.SetParent(_buildingsRoot, false);
            go.transform.localPosition = CellToWorld(b.Cell, LotTop);
            var view = go.AddComponent<BuildingView>();
            view.Init(b);
            _buildingViews[b] = view;
        }

        void OnBotAdded(CourierBot bot)
        {
            var go = new GameObject(bot.Name);
            go.transform.SetParent(_botsRoot, false);
            var view = go.AddComponent<BotView>();
            view.Init(bot, _game);
            _botViews[bot] = view;
            SnapAll();
        }

        void OnTicked()
        {
            UpdateBotOffsets();
            foreach (var v in _botViews.Values) v.OnTick();
            foreach (var v in _buildingViews.Values) v.Refresh();
        }

        void OnDayEnded(DayReport _) => SnapAll();

        void SnapAll()
        {
            UpdateBotOffsets();
            foreach (var v in _botViews.Values) v.Snap();
            foreach (var v in _buildingViews.Values) v.Refresh();
        }

        /// <summary>Fans out bots that share a cell so they don't render on top of each other.</summary>
        void UpdateBotOffsets()
        {
            _botsPerCell.Clear();
            _botsPlacedPerCell.Clear();
            foreach (var bot in Town.Bots)
            {
                _botsPerCell.TryGetValue(bot.Position, out int n);
                _botsPerCell[bot.Position] = n + 1;
            }
            foreach (var kv in _botViews)
            {
                var pos = kv.Key.Position;
                int count = _botsPerCell[pos];
                _botsPlacedPerCell.TryGetValue(pos, out int index);
                _botsPlacedPerCell[pos] = index + 1;

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
                var check = Town.CheckPlacement(_game.PlacingType, cell);
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
