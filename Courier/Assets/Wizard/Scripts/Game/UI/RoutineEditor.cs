using System.Collections.Generic;
using Wizard.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Wizard.Game
{
    /// <summary>
    /// The drag-and-drop programming panel. Edits either the selected construct's daily routine or a
    /// spell from the shared spellbook.
    ///   Palette / spellbook -> list   drag to insert (or click to append)
    ///   List -> list                  drag to reorder
    ///   List -> outside               drag out to delete (or right-click)
    ///   Click a Pick Up / Drop Off row to cycle its side; click a Cast row to open that spell.
    /// Editing is allowed while planning or while the day is paused.
    /// </summary>
    public sealed class RoutineEditor : MonoBehaviour
    {
        const float RowHeight = 34f;
        const float CellWidth = 160f;

        GameController _game;
        World World => _game.World;
        Spellbook Book => _game.World.Spellbook;
        RectTransform _canvasRoot;

        Construct _construct;
        Spell _spell; // null = editing the construct's own routine

        // header (construct mode)
        RectTransform _constructHeader;
        Image _titleSwatch;
        Text _title;
        Text _loopText;
        Button _clearButton;
        // header (spell mode)
        RectTransform _spellHeader;
        InputField _nameField;
        Text _deleteText;
        float _deleteArmedUntil;

        Text _hint;
        CanvasGroup _sourcesGroup;
        RectTransform _spellGrid;
        LayoutElement _spellGridSize;
        Text _noSpells;
        Text _listCaption;
        Text _status;
        RectTransform _listBox;
        ScrollRect _scroll;
        RectTransform _content;
        RectTransform _marker;
        Text _emptyHint;
        Text _footer;
        Text _previewText;

        readonly List<CommandBlockView> _rows = new List<CommandBlockView>();
        readonly List<CommandBlockView> _spellBlocks = new List<CommandBlockView>();
        readonly List<int> _spellIds = new List<int>();

        bool _dragging;
        CommandBlockView _dragSource;
        RectTransform _ghost;
        Image _ghostBg;
        Text _ghostText;
        Vector2 _lastPointer;

        int _shownIndex = int.MinValue;
        CommandResult _shownResult;
        int _shownTick = -1;
        bool _shownEditable;

        Routine Current => _spell != null ? _spell.Routine : _construct?.Routine;

        public bool CanEdit => Current != null && !World.IsGameOver && (_game.IsPlanning || _game.Paused);

        // ------------------------------------------------------------------ construction

        public void Build(GameController game, RectTransform canvasRoot)
        {
            _game = game;
            _canvasRoot = canvasRoot;

            var panel = UIFactory.Panel("Panel", transform, Palette.Panel);
            UIFactory.Stretch(panel.rectTransform);
            UIFactory.VStack(panel, 8, 14);

            BuildConstructHeader(panel.transform);
            BuildSpellHeader(panel.transform);

            _hint = UIFactory.Label("Hint", panel.transform, "", 13, Palette.InkSoft);
            UIFactory.Size(_hint, 36);

            // Everything you can drag from: commands + spellbook.
            var sources = UIFactory.Rect("Sources", panel.transform);
            UIFactory.VStack(sources, 6);
            _sourcesGroup = sources.gameObject.AddComponent<CanvasGroup>();

            Caption(sources, "COMMANDS");
            var palette = UIFactory.Rect("Palette", sources);
            UIFactory.Grid(palette, new Vector2(CellWidth, RowHeight), new Vector2(8, 6), 2);
            int paletteRows = (CommandInfo.PaletteOrder.Length + 1) / 2;
            UIFactory.Size(palette, paletteRows * RowHeight + (paletteRows - 1) * 6);
            foreach (var t in CommandInfo.PaletteOrder)
            {
                var cmd = new Command(t);
                CommandBlockView.Create(this, palette, cmd, true, -1, CommandBlockView.Describe(cmd, Book));
            }

            var spellRow = UIFactory.Rect("Spellbook Caption", sources);
            UIFactory.Size(spellRow, 26, -1, -1, 0);
            UIFactory.HStack(spellRow, 6).childForceExpandHeight = false;
            var capText = UIFactory.Label("Label", spellRow, "SPELLBOOK", 13, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(capText, 22, -1, 1);
            var newSpell = UIFactory.Button("New Spell", spellRow, "+ New spell", Palette.AccentSoft, Palette.Accent, 13, NewSpell, out _);
            UIFactory.Size(newSpell, 24, 104);

            _spellGrid = UIFactory.Rect("Spells", sources);
            UIFactory.Grid(_spellGrid, new Vector2(CellWidth, RowHeight), new Vector2(8, 6), 2);
            _spellGridSize = UIFactory.Size(_spellGrid, RowHeight);
            _noSpells = UIFactory.Label("None", sources, "No spells yet. Spells are reusable chunks of commands.", 12, Palette.InkSoft);
            UIFactory.Size(_noSpells, 18);

            // Routine caption + live status
            var caption = UIFactory.Rect("Caption", panel.transform);
            UIFactory.Size(caption, 22, -1, -1, 0);
            UIFactory.HStack(caption, 6).childForceExpandHeight = false;
            _listCaption = UIFactory.Label("Label", caption, "DAILY ROUTINE", 13, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_listCaption, 22, 150);
            _status = UIFactory.Label("Status", caption, "", 13, Palette.InkSoft, TextAnchor.MiddleRight);
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 9;
            _status.resizeTextMaxSize = 13;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Size(_status, 22, -1, 1);

            BuildList(panel.transform);

            var preview = UIFactory.Button("Preview", panel.transform, "", Palette.AccentSoft, Palette.Accent, 14, TogglePreview, out _previewText);
            _previewText.fontStyle = FontStyle.Bold;
            UIFactory.Size(preview, 32);

            _footer = UIFactory.Label("Footer", panel.transform, "", 13, Palette.InkSoft);

            _game.SelectionChanged += OnSelectionChanged;
            _game.StateChanged += OnStateChanged;
            World.Grid.Expanded += RefreshFooter;
            Book.Changed += OnSpellbookChanged;
            Book.SpellEdited += OnSpellEdited;
            OnSelectionChanged();
            RebuildSpellbook();
            Refresh();
        }

        void BuildConstructHeader(Transform parent)
        {
            _constructHeader = UIFactory.Rect("Construct Header", parent);
            UIFactory.Size(_constructHeader, 34, -1, -1, 0);
            UIFactory.HStack(_constructHeader, 8).childForceExpandHeight = false;
            _titleSwatch = UIFactory.Panel("Swatch", _constructHeader, Color.white, true, 0.5f);
            UIFactory.Size(_titleSwatch, 18, 18);
            _title = UIFactory.Label("Title", _constructHeader, "", 20, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_title, 30, -1, 1);
            var loop = UIFactory.Button("Loop", _constructHeader, "Loop: on", Palette.Button, Palette.Ink, 14, ToggleLoop, out _loopText);
            UIFactory.Size(loop, 30, 88);
            _clearButton = UIFactory.Button("Clear", _constructHeader, "Clear", Palette.Button, Palette.Ink, 14, ClearAll, out _);
            UIFactory.Size(_clearButton, 30, 62);
        }

        void BuildSpellHeader(Transform parent)
        {
            _spellHeader = UIFactory.Rect("Spell Header", parent);
            UIFactory.Size(_spellHeader, 34, -1, -1, 0);
            UIFactory.HStack(_spellHeader, 8).childForceExpandHeight = false;
            var back = UIFactory.Button("Back", _spellHeader, "< Back", Palette.Button, Palette.Ink, 14, CloseSpell, out _);
            UIFactory.Size(back, 30, 70);
            _nameField = UIFactory.TextField("Name", _spellHeader, "", 18, Spellbook.MaxNameLength, "Spell name", OnNameEdited);
            UIFactory.Size(_nameField, 30, -1, 1);
            var del = UIFactory.Button("Delete", _spellHeader, "Delete", Palette.Button, Palette.Error, 14, DeleteSpell, out _deleteText);
            UIFactory.Size(del, 30, 72);
        }

        void BuildList(Transform parent)
        {
            var box = UIFactory.Panel("List", parent, Palette.PanelSunken);
            _listBox = box.rectTransform;
            UIFactory.Size(box, -1, -1, -1, 1);

            var viewport = UIFactory.Rect("Viewport", box.transform);
            UIFactory.Stretch(viewport, 6, 6, 6, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); // so the whole area takes the scroll wheel
            hit.color = new Color(0, 0, 0, 0);

            _content = UIFactory.Rect("Content", viewport);
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.offsetMin = Vector2.zero;
            _content.offsetMax = Vector2.zero;
            var vs = UIFactory.VStack(_content, 4);
            vs.padding = new RectOffset(0, 0, 2, 2);
            UIFactory.FitHeight(_content);

            _scroll = box.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 24f;

            _emptyHint = UIFactory.Label("Empty", box.transform, "Drag commands or spells here", 15, Palette.InkSoft, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_emptyHint.rectTransform);

            _marker = UIFactory.Panel("Insert Marker", _content, Palette.Accent, false).rectTransform;
            UIFactory.Size(_marker, 4);
            _marker.gameObject.SetActive(false);
        }

        static void Caption(Transform parent, string text)
        {
            var t = UIFactory.Label("Caption", parent, text, 13, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t, 18);
        }

        void OnDestroy()
        {
            if (_game == null) return;
            _game.SelectionChanged -= OnSelectionChanged;
            _game.StateChanged -= OnStateChanged;
            World.Grid.Expanded -= RefreshFooter;
            Book.Changed -= OnSpellbookChanged;
            Book.SpellEdited -= OnSpellEdited;
            if (_construct != null) _construct.Routine.Changed -= Refresh;
        }

        // ------------------------------------------------------------------ navigation

        void OnSelectionChanged()
        {
            if (_construct == _game.SelectedConstruct) return;
            CancelDrag();
            if (_construct != null) _construct.Routine.Changed -= Refresh;
            _construct = _game.SelectedConstruct;
            if (_construct != null) _construct.Routine.Changed += Refresh;
            if (_spell == null) Refresh();
            else RefreshFooter();
            _shownIndex = int.MinValue;
        }

        public void OpenSpell(int spellId)
        {
            var spell = Book.Get(spellId);
            if (spell == null) return;
            CancelDrag();
            _spell = spell;
            DisarmDelete();
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Show the selected construct's own routine (leaving any open spell).</summary>
        public void ShowConstructRoutine()
        {
            if (_spell != null) CloseSpell();
        }

        void TogglePreview()
        {
            if (_game.Preview != null) _game.Preview.Toggle();
        }

        void CloseSpell()
        {
            CancelDrag();
            _spell = null;
            DisarmDelete();
            Refresh();
        }

        void DisarmDelete()
        {
            _deleteArmedUntil = 0f;
            if (_deleteText != null) _deleteText.text = "Delete";
        }

        void OnSpellbookChanged()
        {
            if (_spell != null && Book.Get(_spell.Id) == null) _spell = null; // deleted

            if (SameSpells())
            {
                // Just a rename: relabel in place. Rebuilding here would destroy whatever the player
                // is clicking on (the rename fires when the name field loses focus to that click).
                for (int i = 0; i < _spellBlocks.Count; i++)
                    _spellBlocks[i].SetLabel(CommandBlockView.Describe(_spellBlocks[i].Command, Book));
                foreach (var row in _rows)
                    if (row.Command.IsCast) row.SetLabel(CommandBlockView.Describe(row.Command, Book));
                RefreshHeader();
                RefreshFooter();
                return;
            }

            RebuildSpellbook();
            Refresh(); // Cast rows show spell names
        }

        bool SameSpells()
        {
            if (_spellIds.Count != Book.Spells.Count) return false;
            for (int i = 0; i < _spellIds.Count; i++)
                if (_spellIds[i] != Book.Spells[i].Id) return false;
            return true;
        }

        void OnSpellEdited(Spell spell)
        {
            if (spell == _spell) Refresh();
            else RefreshFooter(); // the construct's route may have changed
        }

        // ------------------------------------------------------------------ refresh

        void OnStateChanged()
        {
            RefreshHeader();
            RefreshFooter();
            _shownIndex = int.MinValue;
        }

        void Refresh()
        {
            if (_dragging) CancelDrag();
            RebuildRows();
            RefreshHeader();
            RefreshFooter();
            _shownIndex = int.MinValue;
        }

        void RebuildSpellbook()
        {
            if (_dragging && _dragSource != null && _spellBlocks.Contains(_dragSource)) CancelDrag();
            UIFactory.DestroyChildren(_spellGrid);
            _spellBlocks.Clear();
            _spellIds.Clear();
            foreach (var spell in Book.Spells)
            {
                var cmd = Command.Cast(spell);
                _spellBlocks.Add(CommandBlockView.Create(this, _spellGrid, cmd, true, -1, CommandBlockView.Describe(cmd, Book)));
                _spellIds.Add(spell.Id);
            }
            int rows = (Book.Spells.Count + 1) / 2;
            _spellGrid.gameObject.SetActive(rows > 0);
            _spellGridSize.preferredHeight = _spellGridSize.minHeight = Mathf.Max(0, rows * RowHeight + (rows - 1) * 6);
            _noSpells.gameObject.SetActive(rows == 0);
        }

        void RebuildRows()
        {
            foreach (var r in _rows)
            {
                r.gameObject.SetActive(false);
                Destroy(r.gameObject);
            }
            _rows.Clear();

            var routine = Current;
            if (routine != null)
            {
                for (int i = 0; i < routine.Count; i++)
                {
                    var row = CommandBlockView.Create(this, _content, routine[i], false, i, CommandBlockView.Describe(routine[i], Book));
                    UIFactory.Size(row, RowHeight);
                    _rows.Add(row);
                }
            }
            _marker.SetAsLastSibling();
            _emptyHint.gameObject.SetActive(routine != null && routine.Count == 0);
        }

        void RefreshHeader()
        {
            bool editingSpell = _spell != null;
            _constructHeader.gameObject.SetActive(!editingSpell);
            _spellHeader.gameObject.SetActive(editingSpell);

            bool editable = CanEdit;
            _shownEditable = editable;
            _sourcesGroup.alpha = editable ? 1f : 0.4f;

            if (editingSpell)
            {
                if (!_nameField.isFocused) _nameField.text = _spell.Name;
                _listCaption.text = "SPELL";
                _listCaption.color = Palette.Spell(_spell.Id);
                _hint.text = editable
                    ? "Editing a shared spell: every routine that casts it changes too. Spells can cast other spells."
                    : "Locked while the day runs. Pause (Space) to make changes.";
                return;
            }

            _listCaption.text = "DAILY ROUTINE";
            _listCaption.color = Palette.Ink;

            if (_construct == null)
            {
                _titleSwatch.gameObject.SetActive(false);
                _title.text = "No construct selected";
                _hint.text = "Click a construct on the map or in the list on the left.";
                return;
            }

            _titleSwatch.gameObject.SetActive(true);
            _titleSwatch.color = Palette.Construct(_construct.Id);
            _title.text = _construct.Name;
            _loopText.text = _construct.Loop ? "Loop: on" : "Loop: off";
            _clearButton.interactable = editable;
            _hint.text = editable
                ? "Drag blocks into the routine. Click Pick Up/Drop Off rows to change side, Cast rows to open the spell. Drag a row out to delete."
                : "Locked while the day runs. Pause (Space) to make changes.";
        }

        void RefreshFooter()
        {
            if (_spell != null)
            {
                int ticks = RoutineTracer.Trace(World.Grid, Book, new Cell(0, 0), Facing.North, _spell.Routine).Ticks;
                int users = 0;
                foreach (var c in World.Constructs)
                    if (c.Routine.Casts(_spell.Id)) users++;
                foreach (var s in Book.Spells)
                    if (s != _spell && s.Routine.Casts(_spell.Id)) users++;
                _footer.text = $"{_spell.Routine.Count} commands, {ticks} ticks when cast (casting itself is free).\n" +
                               (users == 0 ? "Nothing casts this spell yet." : $"Cast directly by {users} routine(s)/spell(s).");
                return;
            }

            if (_construct == null)
            {
                _footer.text = "";
                return;
            }

            if (_construct.Routine.Count == 0)
            {
                _footer.text = "An empty routine means this construct stays at the tower all day.";
                return;
            }

            var trace = RoutineTracer.Trace(World, _construct);
            var lines = new List<string>();
            string perDay = _construct.Loop && trace.Ticks > 0 ? $" - about {World.TicksPerDay / trace.Ticks} loops a day" : "";
            lines.Add($"One pass takes {trace.Ticks} ticks{perDay}.");

            if (trace.HitDepthLimit)
                lines.Add(UIFactory.Colour("Spells nest too deeply.", Palette.Error));
            if (trace.BlockedCount > 0)
                lines.Add(UIFactory.Colour($"{trace.BlockedCount} move(s) hit a dead end (red marker).", Palette.Error));

            if (!_construct.Loop)
                lines.Add("Runs once, then waits where it finishes.");
            else if (trace.ReturnsToStart)
                lines.Add(UIFactory.Colour("Ends where it starts, so it loops cleanly.", Palette.Good));
            else
                lines.Add(UIFactory.Colour($"Ends at {trace.End} facing {trace.EndFacing}; each loop starts somewhere new.", Palette.Warning));

            _footer.text = string.Join("\n", lines);
        }

        void Update()
        {
            if (_deleteArmedUntil > 0f && Time.unscaledTime > _deleteArmedUntil)
            {
                _deleteArmedUntil = 0f;
                _deleteText.text = "Delete";
            }

            if (CanEdit != _shownEditable) RefreshHeader();
            if (_dragging) AutoScroll();

            var preview = _game.Preview;
            bool previewOpen = preview != null && preview.IsOpen;
            _previewText.text = previewOpen ? "Close preview  (P)" : "Preview day  (P)";
            if (_construct == null) return;

            // While previewing, highlight the previewed line instead of the live one.
            if (previewOpen && preview.TryGetFrame(_construct, out var frame))
            {
                int pIdx = frame.IndexIn(_spell != null ? _spell.Id : -1);
                int stamp = -(preview.Version * 100000 + frame.Tick + 1);
                if (pIdx != _shownIndex || frame.Result != _shownResult || stamp != _shownTick)
                {
                    _shownIndex = pIdx;
                    _shownResult = frame.Result;
                    _shownTick = stamp;
                    for (int i = 0; i < _rows.Count; i++) _rows[i].SetState(i == pIdx, frame.Result);
                    string cargo = frame.CargoCount > 0 ? $"carrying {frame.CargoCount} {frame.Cargo}" : "empty";
                    _status.text = pIdx >= 0
                        ? $"preview tick {frame.Tick}, line {pIdx + 1}: {CommandInfo.Describe(frame.Result)}"
                        : $"preview tick {frame.Tick}: {cargo}";
                }
                return;
            }

            bool running = !_game.IsPlanning;
            int idx = -1;
            if (running) idx = _spell != null ? _construct.LastIndexIn(_spell.Id) : _construct.LastIndexIn(-1);

            if (idx != _shownIndex || _construct.LastResult != _shownResult || World.Tick != _shownTick)
            {
                _shownIndex = idx;
                _shownResult = _construct.LastResult;
                _shownTick = World.Tick;
                for (int i = 0; i < _rows.Count; i++) _rows[i].SetState(i == idx, _construct.LastResult);

                string who = _spell != null ? _construct.Name + ": " : "";
                string cargo = _construct.IsCarrying ? $"carrying {_construct.CargoCount} {_construct.Cargo}" : "empty";
                if (running && idx >= 0)
                    _status.text = $"{who}line {idx + 1}: {CommandInfo.Describe(_construct.LastResult)}";
                else if (running)
                    _status.text = $"{who}{cargo}";
                else
                    _status.text = _spell != null ? "" : cargo;
            }
        }

        // ------------------------------------------------------------------ editing (called by blocks)

        public void Append(Command c) => Insert(Current == null ? 0 : Current.Count, c);

        void Insert(int index, Command c)
        {
            if (!CanEdit) return;
            if (c.IsCast && !Book.CanCastFrom(_spell, c.SpellId))
            {
                _game.ShowToast("A spell can't cast itself, even through another spell");
                return;
            }
            var routine = Current;
            if (!routine.Insert(index, c))
            {
                _game.ShowToast($"A routine holds at most {Routine.MaxLength} commands");
                return;
            }
            if (index >= routine.Count - 1)
            {
                Canvas.ForceUpdateCanvases();
                _scroll.verticalNormalizedPosition = 0f;
            }
        }

        public void RemoveAt(int index)
        {
            if (CanEdit) Current.RemoveAt(index);
        }

        public void CycleSide(int index)
        {
            if (CanEdit) Current.Set(index, Current[index].WithNextSide());
        }

        void ToggleLoop()
        {
            if (_construct == null) return;
            _construct.Loop = !_construct.Loop;
            if (_game.Preview != null) _game.Preview.MarkDirty();
            OnStateChanged();
        }

        void ClearAll()
        {
            if (CanEdit) Current.Clear();
        }

        void NewSpell()
        {
            if (World.IsGameOver) return;
            var spell = Book.Create();
            if (spell == null)
            {
                _game.ShowToast($"The spellbook holds {Spellbook.MaxSpells} spells");
                return;
            }
            OpenSpell(spell.Id);
        }

        void OnNameEdited(string name)
        {
            if (_spell == null) return;
            Book.Rename(_spell, name);
            _nameField.text = _spell.Name;
        }

        void DeleteSpell()
        {
            if (_spell == null || !CanEdit) return;
            if (_deleteArmedUntil <= 0f)
            {
                _deleteArmedUntil = Time.unscaledTime + 3f;
                _deleteText.text = "Sure?";
                return;
            }
            var spell = _spell;
            DisarmDelete();
            _spell = null;
            World.DeleteSpell(spell); // also strips its casts; raises Spellbook.Changed
            _game.ShowToast($"Deleted '{spell.Name}' and every cast of it");
        }

        // ------------------------------------------------------------------ drag and drop

        public void BeginDrag(CommandBlockView source, PointerEventData e)
        {
            if (!CanEdit || _dragging) return;
            _dragging = true;
            _dragSource = source;

            _ghostBg = UIFactory.Panel("Drag Ghost", _canvasRoot, source.BaseColor, true, 0.7f);
            _ghost = _ghostBg.rectTransform;
            _ghost.sizeDelta = new Vector2(190, RowHeight);
            _ghost.pivot = new Vector2(0.15f, 0.5f);
            var group = _ghost.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.alpha = 0.9f;
            _ghostText = UIFactory.Label("Label", _ghost, CommandBlockView.Describe(source.Command, Book), 15, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(_ghostText.rectTransform, 12, 8, 0, 0);
            _ghost.SetAsLastSibling();

            if (!source.FromPalette) source.SetDimmed(true);
            Drag(e);
        }

        public void Drag(PointerEventData e)
        {
            if (!_dragging) return;
            _lastPointer = e.position;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRoot, e.position, null, out var local);
            _ghost.anchoredPosition = local;

            bool over = IsOverList(e.position);
            if (over) ShowMarker(InsertionIndex(e.position));
            else _marker.gameObject.SetActive(false);

            bool willDelete = !over && !_dragSource.FromPalette;
            _ghostBg.color = willDelete ? Palette.Error : _dragSource.BaseColor;
            _ghostText.text = willDelete ? "Remove" : CommandBlockView.Describe(_dragSource.Command, Book);
        }

        public void EndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            var source = _dragSource;
            bool over = IsOverList(e.position);
            int insertAt = over ? InsertionIndex(e.position) : -1;
            CancelDrag();
            if (!CanEdit) return; // the day started mid-drag

            if (source.FromPalette)
            {
                if (over) Insert(insertAt, source.Command);
            }
            else if (over)
            {
                Current.MoveToInsertionPoint(source.Index, insertAt);
            }
            else
            {
                Current.RemoveAt(source.Index);
            }
        }

        void CancelDrag()
        {
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            if (_dragSource != null) _dragSource.SetDimmed(false);
            _dragSource = null;
            _dragging = false;
            if (_marker != null) _marker.gameObject.SetActive(false);
        }

        bool IsOverList(Vector2 screenPos) => RectTransformUtility.RectangleContainsScreenPoint(_listBox, screenPos, null);

        /// <summary>0 = above the first row ... Count = below the last row.</summary>
        int InsertionIndex(Vector2 screenPos)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var rt = (RectTransform)_rows[i].transform;
                var center = rt.TransformPoint(rt.rect.center);
                if (screenPos.y > center.y) return i;
            }
            return _rows.Count;
        }

        void ShowMarker(int insertAt)
        {
            _marker.gameObject.SetActive(true);
            _marker.SetAsLastSibling(); // rows are now 0..Count-1
            _marker.SetSiblingIndex(insertAt);
        }

        void AutoScroll()
        {
            if (!IsOverList(_lastPointer)) return;
            var corners = new Vector3[4];
            _listBox.GetWorldCorners(corners); // overlay canvas: world == screen
            const float edge = 28f;
            float speed = 1.5f * Time.unscaledDeltaTime;
            float before = _scroll.verticalNormalizedPosition;
            if (_lastPointer.y > corners[1].y - edge)
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(before + speed);
            else if (_lastPointer.y < corners[0].y + edge)
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(before - speed);
            if (!Mathf.Approximately(before, _scroll.verticalNormalizedPosition))
                ShowMarker(InsertionIndex(_lastPointer));
        }
    }
}
