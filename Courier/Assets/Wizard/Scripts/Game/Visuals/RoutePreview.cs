using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wizard.Game
{
    /// <summary>
    /// Draws the selected construct's route: a line through every cell it will visit on one pass of
    /// its routine (spells expanded), green/orange markers where it picks up/drops off, and red where a move is blocked.
    /// </summary>
    public sealed class RoutePreview : MonoBehaviour
    {
        const float Y = 0.1f;

        GameController _game;
        LineRenderer _line;
        Construct _bot;
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
            _game.World.Grid.Expanded += MarkDirty;
            _game.World.Spellbook.Changed += MarkDirty;
            _game.World.Spellbook.SpellEdited += OnSpellEdited;
            OnSelectionChanged();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            _game.SelectionChanged -= OnSelectionChanged;
            _game.World.Grid.Expanded -= MarkDirty;
            _game.World.Spellbook.Changed -= MarkDirty;
            _game.World.Spellbook.SpellEdited -= OnSpellEdited;
            if (_bot != null) _bot.Routine.Changed -= MarkDirty;
        }

        void OnSelectionChanged()
        {
            if (_bot == _game.SelectedConstruct) return;
            if (_bot != null) _bot.Routine.Changed -= MarkDirty;
            _bot = _game.SelectedConstruct;
            if (_bot != null) _bot.Routine.Changed += MarkDirty;
            MarkDirty();
        }

        void MarkDirty() => _dirty = true;
        void OnSpellEdited(Spell _) => _dirty = true;

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
            var color = Palette.Construct(_bot.Id);
            _line.sharedMaterial = Shapes.Unlit(color);

            var last = trace.Start;
            _points.Add(WorldView.CellToWorld(last, Y));
            Marker(trace.Start, color, 0.22f, 0.02f); // home pad

            foreach (var step in trace.Steps)
            {
                if (step.Position != last)
                {
                    _points.Add(WorldView.CellToWorld(step.Position, Y));
                    last = step.Position;
                }

                if (step.Blocked)
                {
                    Marker(step.Position.Step(step.Facing), Palette.Invalid, 0.3f, 0.3f);
                }
                else if (step.Command.Type == CommandType.PickUp)
                {
                    Marker(step.Position, Palette.For(Command.PickUp()), 0.14f, 0.14f, step.Facing, -0.12f);
                }
                else if (step.Command.Type == CommandType.DropOff)
                {
                    Marker(step.Position, Palette.For(Command.DropOff()), 0.14f, 0.14f, step.Facing, 0.12f);
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
            var side = WorldView.CellToWorld(facing.TurnRight().ToOffset()) * sideOffset;
            m.SetActive(true);
            m.transform.localPosition = WorldView.CellToWorld(cell, Y + height * 0.5f) + side;
            m.transform.localScale = new Vector3(size, height, size);
            Shapes.Recolor(m, color);
        }

        void HideUnusedMarkers()
        {
            for (int i = _markersUsed; i < _markers.Count; i++) _markers[i].SetActive(false);
        }
    }
}
