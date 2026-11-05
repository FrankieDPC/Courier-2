using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bunker.Game
{
    /// <summary>
    /// Draws the selected bot's route: a line through every tile it walks on one pass of its routine
    /// (custom commands expanded), pins on tiles picked for "Go to", and red where it would get stuck.
    /// </summary>
    public sealed class RoutePreview : MonoBehaviour
    {
        const float Y = 0.1f;

        GameController _game;
        LineRenderer _line;
        Bot _bot;
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
            _game.StateChanged += MarkDirty;
            _game.World.Library.Changed += MarkDirty;
            _game.World.Library.MacroEdited += OnSpellEdited;
            _game.World.GroundChanged += OnGroundChanged;
            OnSelectionChanged();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            _game.SelectionChanged -= OnSelectionChanged;
            _game.StateChanged -= MarkDirty;
            _game.World.Library.Changed -= MarkDirty;
            _game.World.Library.MacroEdited -= OnSpellEdited;
            _game.World.GroundChanged -= OnGroundChanged;
            if (_bot != null) _bot.Routine.Changed -= MarkDirty;
        }

        void OnSelectionChanged()
        {
            if (_bot == _game.SelectedBot) return;
            if (_bot != null) _bot.Routine.Changed -= MarkDirty;
            _bot = _game.SelectedBot;
            if (_bot != null) _bot.Routine.Changed += MarkDirty;
            MarkDirty();
        }

        void MarkDirty() => _dirty = true;
        void OnSpellEdited(Macro _) => _dirty = true;
        void OnGroundChanged(Cell _) => _dirty = true; // a cleared tile may open up a route

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

            if (_bot == null || _bot.Routine.Count == 0)
            {
                _line.positionCount = 0;
                HideUnusedMarkers();
                return;
            }

            var trace = RoutineTracer.Trace(_game.World, _bot);
            var color = Palette.Bot(_bot.Id);
            _line.sharedMaterial = Shapes.Unlit(color);

            var last = trace.Start;
            _points.Add(SurfaceView.CellToWorld(last, Y));
            Marker(trace.Start, color, 0.22f, 0.02f); // home pad

            foreach (var step in trace.Steps)
            {
                if (step.Position != last)
                {
                    _points.Add(SurfaceView.CellToWorld(step.Position, Y));
                    last = step.Position;
                }
                if (step.Blocked) Marker(step.Position.Step(step.Facing), Palette.Invalid, 0.3f, 0.3f);
            }

            // Pins on the tiles picked for "Go to tile".
            foreach (var tile in trace.PickedTiles)
            {
                Marker(tile, color, 0.06f, 0.6f);
                Marker(tile, Color.white, 0.18f, 0.12f, 0.6f);
            }

            if (!trace.ReturnsToStart) Marker(trace.End, Palette.Warning, 0.18f, 0.25f); // where the next loop starts from

            _line.positionCount = _points.Count;
            _line.SetPositions(_points.ToArray());
            HideUnusedMarkers();
        }

        void Marker(Cell cell, Color color, float size, float height, float lift = 0f)
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
            m.SetActive(true);
            m.transform.localPosition = SurfaceView.CellToWorld(cell, Y + lift + height * 0.5f);
            m.transform.localScale = new Vector3(size, height, size);
            Shapes.Recolor(m, color);
        }

        void HideUnusedMarkers()
        {
            for (int i = _markersUsed; i < _markers.Count; i++) _markers[i].SetActive(false);
        }
    }
}
