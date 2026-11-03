using System.Collections.Generic;
using Courier.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Courier.Game
{
    /// <summary>
    /// Draws the selected courier's route: a line through every cell it will visit on one pass of
    /// its schedule, green/orange markers where it picks up/drops off, and red where a move is blocked.
    /// </summary>
    public sealed class RoutePreview : MonoBehaviour
    {
        const float Y = 0.1f;

        GameController _game;
        LineRenderer _line;
        CourierBot _bot;
        bool _dirty = true;
        readonly List<GameObject> _markers = new List<GameObject>();
        int _markersUsed;
        readonly List<Vector3> _points = new List<Vector3>();

        public void Init(GameController game)
        {
            _game = game;
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.widthMultiplier = 0.09f;
            _line.numCornerVertices = 2;
            _line.numCapVertices = 2;
            _line.alignment = LineAlignment.TransformZ;
            transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lay the ribbon flat on the ground
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;

            _game.SelectionChanged += OnSelectionChanged;
            _game.Town.Grid.Expanded += MarkDirty;
            OnSelectionChanged();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            _game.SelectionChanged -= OnSelectionChanged;
            _game.Town.Grid.Expanded -= MarkDirty;
            if (_bot != null) _bot.Schedule.Changed -= MarkDirty;
        }

        void OnSelectionChanged()
        {
            if (_bot == _game.SelectedBot) return;
            if (_bot != null) _bot.Schedule.Changed -= MarkDirty;
            _bot = _game.SelectedBot;
            if (_bot != null) _bot.Schedule.Changed += MarkDirty;
            MarkDirty();
        }

        void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            Rebuild();
        }

        void Rebuild()
        {
            _markersUsed = 0;
            _points.Clear();

            if (_bot == null || _bot.Schedule.Count == 0)
            {
                _line.positionCount = 0;
                HideUnusedMarkers();
                return;
            }

            var trace = ScheduleTracer.Trace(_game.Town.Grid, _bot);
            var color = Palette.Bot(_bot.Id);
            _line.sharedMaterial = Shapes.Unlit(color);

            var last = trace.Start;
            _points.Add(TownView.CellToWorld(last, Y));
            Marker(trace.Start, color, 0.22f, 0.02f); // home pad

            foreach (var step in trace.Steps)
            {
                if (step.Position != last)
                {
                    _points.Add(TownView.CellToWorld(step.Position, Y));
                    last = step.Position;
                }

                if (step.Blocked)
                {
                    Marker(step.Position.Step(step.Facing), Palette.Invalid, 0.3f, 0.3f);
                }
                else if (step.Command.Type == CommandType.PickUp)
                {
                    Marker(step.Position, Palette.For(CommandType.PickUp), 0.14f, 0.14f, step.Facing, -0.12f);
                }
                else if (step.Command.Type == CommandType.DropOff)
                {
                    Marker(step.Position, Palette.For(CommandType.DropOff), 0.14f, 0.14f, step.Facing, 0.12f);
                }
            }

            if (!trace.ReturnsToStart) Marker(trace.End, Palette.Warning, 0.18f, 0.25f);

            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());
            HideUnusedMarkers();
        }

        void Marker(Cell cell, Color color, float size, float height, Facing facing = Facing.North, float sideOffset = 0f)
        {
            GameObject m;
            if (_markersUsed < _markers.Count)
            {
                m = _markers[_markersUsed];
            }
            else
            {
                m = Shapes.Box("Marker", null, Vector3.zero, Vector3.one, color, false);
                m.transform.SetParent(transform.parent, false);
                _markers.Add(m);
            }
            _markersUsed++;

            // Offset pick-up and drop-off markers sideways so both are visible on the same cell.
            var side = TownView.CellToWorld(facing.TurnRight().ToOffset()) * sideOffset;
            m.SetActive(true);
            m.transform.localPosition = TownView.CellToWorld(cell, Y + height * 0.5f) + side;
            m.transform.localScale = new Vector3(size, height, size);
            Shapes.Recolor(m, color);
        }

        void HideUnusedMarkers()
        {
            for (int i = _markersUsed; i < _markers.Count; i++) _markers[i].SetActive(false);
        }
    }
}
