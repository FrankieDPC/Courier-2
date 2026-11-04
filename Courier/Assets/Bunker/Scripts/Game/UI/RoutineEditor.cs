using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bunker.Game
{
    /// <summary>
    /// A bot's programming interface: opened by clicking a bot (time stops while it's open), closed with
    /// Done or Esc. Edits either the bot's routine, which always loops, or a custom command from the
    /// shared library. Bots whose routine changed start again from line 1 when time runs again.
    ///   Palette / my commands -> list   drag to insert (or click to append)
    ///   List -> list                    drag to reorder
    ///   List -> outside                 drag out to delete (or right-click)
    ///   Click a row to change its option (where to go, which way, what to grab or plant); "-"/"+" set
    ///   Grab amounts; "pick" chooses a tile on the map; click a Run row to open that custom command.
    /// Editing is allowed whenever time is stopped (the interface stops it while open).
    /// </summary>
    public sealed class RoutineEditor : MonoBehaviour
    {
        const float RowHeight = 34f;
        const float CellWidth = 160f;

        /// <summary>"My commands" grows up to this many rows, then scrolls.</summary>
        const int LibraryRowsShown = 4;

        GameController _game;
        World World => _game.World;
        CommandLibrary Book => _game.World.Library;
        RectTransform _canvasRoot;

        Bot _bot;
        Macro _macro; // null = editing the bot's own routine

        // header (bot mode)
        RectTransform _botHeader;
        Image _titleSwatch;
        Text _title;
        Button _clearButton;
        // header (custom command mode)
        RectTransform _macroHeader;
        InputField _nameField;
        Text _deleteText;
        float _deleteArmedUntil;

        Text _hint;
        CanvasGroup _sourcesGroup;
        Text _libraryCaption;
        RectTransform _libraryBox;
        ScrollRect _libraryScroll;
        RectTransform _macroGrid;
        LayoutElement _macroGridSize;
        Text _noMacros;
        bool _scrollLibraryToEnd;
        bool _footerDirty;
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
        readonly List<CommandBlockView> _paletteBlocks = new List<CommandBlockView>();
        readonly List<CommandBlockView> _macroBlocks = new List<CommandBlockView>();
        readonly List<int> _macroIds = new List<int>();

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

        Routine Current => _macro != null ? _macro.Routine : _bot?.Routine;

        public bool CanEdit => Current != null && !World.IsGameOver && _game.Paused;

        // ------------------------------------------------------------------ construction

        public void Build(GameController game, RectTransform canvasRoot)
        {
            _game = game;
            _canvasRoot = canvasRoot;

            var panel = UIFactory.Panel("Panel", transform, Palette.Panel);
            UIFactory.Stretch(panel.rectTransform);
            UIFactory.VStack(panel, 8, 14);

            BuildBotHeader(panel.transform);
            BuildMacroHeader(panel.transform);

            _hint = UIFactory.Label("Hint", panel.transform, "", 13, Palette.InkSoft);
            UIFactory.Size(_hint, 62);

            // Everything you can drag from: commands + my commands.
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
                var cmd = Command.Default(t);
                _paletteBlocks.Add(CommandBlockView.Create(this, palette, cmd, true, -1, CommandBlockView.Describe(cmd, Book)));
            }

            var macroRow = UIFactory.Rect("My Commands Caption", sources);
            UIFactory.Size(macroRow, 26, -1, -1, 0);
            UIFactory.HStack(macroRow, 6).childForceExpandHeight = false;
            _libraryCaption = UIFactory.Label("Label", macroRow, "MY COMMANDS", 13, Palette.InkSoft, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_libraryCaption, 22, -1, 1);
            var newMacro = UIFactory.Button("New Command", macroRow, "+ New command", Palette.AccentSoft, Palette.Accent, 13, NewMacro, out _);
            UIFactory.Size(newMacro, 24, 124);

            BuildLibrary(sources);
            _noMacros = UIFactory.Label("None", sources, "None yet. Custom commands are reusable chunks of commands.", 12, Palette.InkSoft);
            UIFactory.Size(_noMacros, 18);

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

            var actions = UIFactory.Rect("Actions", panel.transform);
            UIFactory.Size(actions, 32, -1, -1, 0);
            UIFactory.HStack(actions, 8);
            var preview = UIFactory.Button("Preview", actions, "", Palette.AccentSoft, Palette.Accent, 14, TogglePreview, out _previewText);
            _previewText.fontStyle = FontStyle.Bold;
            UIFactory.Size(preview, -1, -1, 1);
            var save = UIFactory.Button("Save As Command", actions, "Save as command", Palette.AccentSoft, Palette.Accent, 14, SaveAsMacro, out var saveText);
            saveText.fontStyle = FontStyle.Bold;
            UIFactory.Size(save, -1, 132);

            _footer = UIFactory.Label("Footer", panel.transform, "", 13, Palette.InkSoft);

            _game.SelectionChanged += OnSelectionChanged;
            _game.StateChanged += OnStateChanged;
            Book.Changed += OnLibraryChanged;
            Book.MacroEdited += OnMacroEdited;
            World.TileChanged += OnTileChanged;
            OnSelectionChanged();
            RebuildLibrary();
            Refresh();
        }

        void BuildBotHeader(Transform parent)
        {
            _botHeader = UIFactory.Rect("Bot Header", parent);
            UIFactory.Size(_botHeader, 34, -1, -1, 0);
            UIFactory.HStack(_botHeader, 8).childForceExpandHeight = false;
            _titleSwatch = UIFactory.Panel("Swatch", _botHeader, Color.white, true, 0.5f);
            UIFactory.Size(_titleSwatch, 18, 18);
            _title = UIFactory.Label("Title", _botHeader, "", 20, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_title, 30, -1, 1);
            _clearButton = UIFactory.Button("Clear", _botHeader, "Clear", Palette.Button, Palette.Ink, 14, ClearAll, out _);
            UIFactory.Size(_clearButton, 30, 62);
            var done = UIFactory.Button("Done", _botHeader, "Done", Palette.Accent, Color.white, 14, _game.CloseEditor, out var doneText);
            doneText.fontStyle = FontStyle.Bold;
            UIFactory.Size(done, 30, 70);
        }

        void BuildMacroHeader(Transform parent)
        {
            _macroHeader = UIFactory.Rect("Command Header", parent);
            UIFactory.Size(_macroHeader, 34, -1, -1, 0);
            UIFactory.HStack(_macroHeader, 8).childForceExpandHeight = false;
            var back = UIFactory.Button("Back", _macroHeader, "< Back", Palette.Button, Palette.Ink, 14, CloseMacro, out _);
            UIFactory.Size(back, 30, 70);
            _nameField = UIFactory.TextField("Name", _macroHeader, "", 18, CommandLibrary.MaxNameLength, "Command name", OnNameEdited);
            UIFactory.Size(_nameField, 30, -1, 1);
            var del = UIFactory.Button("Delete", _macroHeader, "Delete", Palette.Button, Palette.Error, 14, DeleteMacroClicked, out _deleteText);
            UIFactory.Size(del, 30, 72);
        }

        /// <summary>The "my commands" grid: grows to <see cref="LibraryRowsShown"/> rows, then scrolls.</summary>
        void BuildLibrary(Transform parent)
        {
            _libraryBox = UIFactory.Rect("My Commands", parent);
            _macroGridSize = UIFactory.Size(_libraryBox, RowHeight);

            var viewport = UIFactory.Rect("Viewport", _libraryBox);
            UIFactory.Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); // so the wheel also works between blocks
            hit.color = new Color(0, 0, 0, 0);

            _macroGrid = UIFactory.Rect("Grid", viewport);
            _macroGrid.anchorMin = new Vector2(0, 1);
            _macroGrid.anchorMax = new Vector2(1, 1);
            _macroGrid.pivot = new Vector2(0.5f, 1);
            _macroGrid.offsetMin = Vector2.zero;
            _macroGrid.offsetMax = Vector2.zero;
            UIFactory.Grid(_macroGrid, new Vector2(CellWidth, RowHeight), new Vector2(8, 6), 2);
            UIFactory.FitHeight(_macroGrid);

            _libraryScroll = _libraryBox.gameObject.AddComponent<ScrollRect>();
            _libraryScroll.viewport = viewport;
            _libraryScroll.content = _macroGrid;
            _libraryScroll.horizontal = false;
            _libraryScroll.vertical = true;
            _libraryScroll.movementType = ScrollRect.MovementType.Clamped;
            _libraryScroll.scrollSensitivity = 24f;

            // A thin scrollbar in the gap right of the two columns, shown only when there's more to see.
            var bar = UIFactory.Rect("Scrollbar", _libraryBox);
            bar.anchorMin = new Vector2(1, 0);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(1, 0.5f);
            bar.sizeDelta = new Vector2(5, 0);
            bar.anchoredPosition = Vector2.zero;
            var track = bar.gameObject.AddComponent<Image>();
            track.color = Palette.PanelSunken;
            var area = UIFactory.Rect("Sliding Area", bar);
            UIFactory.Stretch(area);
            var handle = UIFactory.Panel("Handle", area, Palette.InkSoft, true, 0.25f);
            UIFactory.Stretch(handle.rectTransform);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None }; // arrow keys pan the camera, not this
            _libraryScroll.verticalScrollbar = scrollbar;
            _libraryScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
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

            _emptyHint = UIFactory.Label("Empty", box.transform, "Drag commands here", 15, Palette.InkSoft, TextAnchor.MiddleCenter);
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
            Book.Changed -= OnLibraryChanged;
            Book.MacroEdited -= OnMacroEdited;
            World.TileChanged -= OnTileChanged;
            if (_bot != null) _bot.Routine.Changed -= Refresh;
        }

        // ------------------------------------------------------------------ navigation

        void OnSelectionChanged()
        {
            if (_bot == _game.SelectedBot) return;
            CancelDrag();
            _game.CancelPick(); // a pending pick belongs to the old bot's routine
            if (_bot != null) _bot.Routine.Changed -= Refresh;
            _bot = _game.SelectedBot;
            if (_bot != null) _bot.Routine.Changed += Refresh;
            _macro = null; // opening (or switching) always starts on the bot's own routine
            DisarmDelete();
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
            _shownIndex = int.MinValue;
        }

        public void OpenMacro(int macroId)
        {
            var macro = Book.Get(macroId);
            if (macro == null) return;
            CancelDrag();
            _game.CancelPick();
            _macro = macro;
            DisarmDelete();
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>Show the selected bot's own routine (leaving any open custom command).</summary>
        public void ShowBotRoutine()
        {
            if (_macro != null) CloseMacro();
        }

        void TogglePreview()
        {
            if (_game.Preview != null) _game.Preview.Toggle();
        }

        void CloseMacro()
        {
            CancelDrag();
            _game.CancelPick();
            _macro = null;
            DisarmDelete();
            Refresh();
        }

        void DisarmDelete()
        {
            _deleteArmedUntil = 0f;
            if (_deleteText != null) _deleteText.text = "Delete";
        }

        void OnLibraryChanged()
        {
            if (_macro != null && Book.Get(_macro.Id) == null) _macro = null; // deleted

            if (SameMacros())
            {
                // Just a rename: relabel in place. Rebuilding here would destroy whatever the player
                // is clicking on (the rename fires when the name field loses focus to that click).
                for (int i = 0; i < _macroBlocks.Count; i++)
                    _macroBlocks[i].SetLabel(CommandBlockView.Describe(_macroBlocks[i].Command, Book));
                foreach (var row in _rows)
                    if (row.Command.IsRun) row.SetLabel(CommandBlockView.Describe(row.Command, Book));
                RefreshHeader();
                RefreshFooter();
                return;
            }

            RebuildLibrary();
            Refresh(); // Cast rows show macro names
        }

        bool SameMacros()
        {
            if (_macroIds.Count != Book.Macros.Count) return false;
            for (int i = 0; i < _macroIds.Count; i++)
                if (_macroIds[i] != Book.Macros[i].Id) return false;
            return true;
        }

        /// <summary>The ground changed (something cleared, tilled...): routes may have opened up.</summary>
        void OnTileChanged(Cell _) => _footerDirty = true;

        void OnMacroEdited(Macro macro)
        {
            if (macro == _macro) Refresh();
            else
            {
                RefreshAvailability(); // a custom command may now hold jobs this bot can't do
                RefreshFooter(); // the bot's route may have changed
            }
        }

        // ------------------------------------------------------------------ what this bot can do

        /// <summary>Editing a bot (not a shared custom command) that can't do every job.</summary>
        bool Restricted => _macro == null && _bot != null && _bot.Kind != BotKind.Farmhand;

        bool Allowed(Command c) => !Restricted || BotKinds.CanDoLine(_bot.Kind, Book, c);

        string CantDoMessage(Command c)
        {
            string role = $"{_bot.Name} is a {BotKinds.Name(_bot.Kind).ToLowerInvariant()} ({BotKinds.Duties(_bot.Kind)})";
            if (!c.IsRun) return role;
            var m = Book.Get(c.MacroId);
            return $"'{(m != null ? m.Name : "That command")}' has jobs {_bot.Name} can't do. {role}";
        }

        /// <summary>Fade the blocks and rows the bot being edited can't do.</summary>
        void RefreshAvailability()
        {
            foreach (var b in _paletteBlocks) b.SetUnavailable(!Allowed(b.Command));
            foreach (var b in _macroBlocks) b.SetUnavailable(!Allowed(b.Command));
            foreach (var r in _rows) r.SetUnavailable(!Allowed(r.Command));
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
            _game.CancelPick(); // rows may have moved; PickTile is always started after the edit
            RebuildRows();
            RefreshAvailability();
            RefreshHeader();
            RefreshFooter();
            _shownIndex = int.MinValue;
        }

        void RebuildLibrary()
        {
            if (_dragging && _dragSource != null && _macroBlocks.Contains(_dragSource)) CancelDrag();
            if (Book.Macros.Count > _macroIds.Count) _scrollLibraryToEnd = true; // new ones go at the end
            UIFactory.DestroyChildren(_macroGrid);
            _macroBlocks.Clear();
            _macroIds.Clear();
            foreach (var macro in Book.Macros)
            {
                var cmd = Command.Run(macro);
                _macroBlocks.Add(CommandBlockView.Create(this, _macroGrid, cmd, true, -1, CommandBlockView.Describe(cmd, Book)));
                _macroIds.Add(macro.Id);
            }
            int rows = (Book.Macros.Count + 1) / 2;
            int shown = Mathf.Min(rows, LibraryRowsShown);
            _libraryBox.gameObject.SetActive(rows > 0);
            _macroGridSize.preferredHeight = _macroGridSize.minHeight = Mathf.Max(0, shown * RowHeight + (shown - 1) * 6);
            _noMacros.gameObject.SetActive(rows == 0);
            _libraryCaption.text = $"MY COMMANDS  <size=11>{Book.Macros.Count}/{CommandLibrary.MaxMacros}</size>";
            RefreshAvailability();
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
            bool editingSpell = _macro != null;
            _botHeader.gameObject.SetActive(!editingSpell);
            _macroHeader.gameObject.SetActive(editingSpell);

            bool editable = CanEdit;
            _shownEditable = editable;
            _sourcesGroup.alpha = editable ? 1f : 0.4f;

            if (editingSpell)
            {
                if (!_nameField.isFocused) _nameField.text = _macro.Name;
                _listCaption.text = "CUSTOM COMMAND";
                _listCaption.color = Palette.Macro(_macro.Id);
                _hint.text = editable
                    ? "Editing a shared custom command: every routine that runs it changes too. Jobs work on the tile in front of the bot."
                    : "It's all over - press Play again.";
                return;
            }

            _listCaption.text = "ROUTINE (LOOPS)";
            _listCaption.color = Palette.Ink;

            if (_bot == null)
            {
                _titleSwatch.gameObject.SetActive(false);
                bool anyBots = false;
                foreach (var b in World.Bots)
                    if (!b.IsPlayer) anyBots = true;
                _title.text = anyBots ? "No bot selected" : "No bots yet";
                _hint.text = anyBots
                    ? "Click a bot on the map or in the list on the left. You steer yourself with WASD and E."
                    : "Salvage scrap and circuits from old tech, deliver them to the hatch, then build a bot in the workshop. Its routine goes here.";
                return;
            }

            _titleSwatch.gameObject.SetActive(true);
            _titleSwatch.color = Palette.Bot(_bot.Id);
            _title.text = _bot.Kind == BotKind.Farmhand
                ? _bot.Name
                : $"{_bot.Name}  <size=12><color={UIFactory.Hex(Palette.For(Item.Water))}>{BotKinds.Name(_bot.Kind).ToUpperInvariant()}</color></size>";
            _clearButton.interactable = editable;
            if (!editable)
                _hint.text = "It's all over - press Play again.";
            else if (Restricted)
                _hint.text = $"{_bot.Name} only does water jobs: moving, grabbing water at the stream and watering. Faded blocks are jobs it can't do. Jobs work on the tile in front of it.";
            else
                _hint.text = "Drag blocks in. Click a row to change its option, -/+ for amounts, 'pick' to choose a tile. Drag out to delete. Jobs work on the tile in front of the bot: Face turns it.";
        }

        void RefreshFooter()
        {
            if (_macro != null)
            {
                var macroTrace = RoutineTracer.Trace(World, World.Map.Home, World.Map.HomeFacing, _macro.Routine);
                int users = 0;
                foreach (var b in World.Bots)
                    if (b.Routine.Runs(_macro.Id)) users++;
                foreach (var m in Book.Macros)
                    if (m != _macro && m.Routine.Runs(_macro.Id)) users++;
                _footer.text = $"{_macro.Routine.Count} commands, about {macroTrace.Ticks} ticks when run (running it is free).\n" +
                               (users == 0 ? "Nothing runs this command yet." : $"Used directly by {users} routine(s)/command(s).");
                return;
            }

            if (_bot == null)
            {
                _footer.text = "";
                return;
            }

            if (_bot.Routine.Count == 0)
            {
                _footer.text = "No commands yet: this bot waits by the hatch. Drag commands in; they loop all day.";
                return;
            }

            var trace = RoutineTracer.Trace(World, _bot);
            var lines = new List<string>();
            float realSeconds = trace.Ticks * _game.SecondsPerTick;
            int gameMinutes = trace.Ticks * (Balance.DayEndMinutes - Balance.DayStartMinutes) / Mathf.Max(1, World.TicksPerDay);
            int loops = trace.Ticks > 0 ? World.TicksPerDay / trace.Ticks : 0;
            lines.Add($"One loop takes about {trace.Ticks} ticks ({gameMinutes} in-game minutes, {realSeconds:0}s at 1x): about {loops} loop(s) a day.");

            if (trace.HitDepthLimit)
                lines.Add(UIFactory.Colour("Custom commands nest too deeply.", Palette.Error));
            if (trace.BlockedCount > 0)
                lines.Add(UIFactory.Colour($"{trace.BlockedCount} move(s) are blocked or unreachable (red marker).", Palette.Error));
            if (trace.CantDoCount > 0)
                lines.Add(UIFactory.Colour($"{trace.CantDoCount} job(s) {_bot.Name} can't do will fail (faded rows).", Palette.Error));
            lines.Add("At 2 am it's called home and starts again from line 1. P previews the rest of today.");

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
            if (_footerDirty)
            {
                _footerDirty = false;
                RefreshFooter();
            }
            if (_scrollLibraryToEnd)
            {
                _scrollLibraryToEnd = false;
                Canvas.ForceUpdateCanvases();
                _libraryScroll.verticalNormalizedPosition = 0f;
            }

            var preview = _game.Preview;
            bool previewOpen = preview != null && preview.IsOpen;
            _previewText.text = previewOpen ? "Close preview  (P)" : "Preview day  (P)";
            if (_bot == null) return;

            // While previewing, highlight the previewed line instead of the live one.
            if (previewOpen && preview.TryGetFrame(_bot, out var frame))
            {
                int pIdx = frame.IndexIn(_macro != null ? _macro.Id : -1);
                int stamp = -(preview.Version * 100000 + frame.Tick + 1);
                if (pIdx != _shownIndex || frame.Result != _shownResult || stamp != _shownTick)
                {
                    _shownIndex = pIdx;
                    _shownResult = frame.Result;
                    _shownTick = stamp;
                    for (int i = 0; i < _rows.Count; i++) _rows[i].SetState(i == pIdx, frame.Result);
                    string cargo = frame.LoadWeight > 0 ? $"load {frame.LoadWeight}/{frame.Capacity}" : "empty";
                    _status.text = pIdx >= 0
                        ? $"preview tick {frame.Tick}, line {pIdx + 1}: {CommandInfo.Describe(frame.Result)}"
                        : $"preview tick {frame.Tick}: {cargo}";
                }
                return;
            }

            bool running = true;
            int idx = -1;
            if (running) idx = _macro != null ? _bot.LastIndexIn(_macro.Id) : _bot.LastIndexIn(-1);

            if (idx != _shownIndex || _bot.LastResult != _shownResult || World.Tick != _shownTick)
            {
                _shownIndex = idx;
                _shownResult = _bot.LastResult;
                _shownTick = World.Tick;
                for (int i = 0; i < _rows.Count; i++) _rows[i].SetState(i == idx, _bot.LastResult);

                string who = _macro != null ? _bot.Name + ": " : "";
                string cargo = _bot.IsCarrying ? $"carrying {_bot.Load} ({_bot.Load.Weight}/{_bot.Capacity})" : "empty";
                if (running && idx >= 0)
                    _status.text = $"{who}line {idx + 1}: {CommandInfo.Describe(_bot.LastResult)}";
                else if (running)
                    _status.text = $"{who}{cargo}";
                else
                    _status.text = _macro != null ? "" : cargo;
            }
        }

        // ------------------------------------------------------------------ editing (called by blocks)

        public void Append(Command c) => Insert(Current == null ? 0 : Current.Count, c);

        void Insert(int index, Command c)
        {
            if (!CanEdit) return;
            if (!Allowed(c))
            {
                _game.ShowToast(CantDoMessage(c));
                return;
            }
            if (c.IsRun && !Book.CanRunFrom(_macro, c.MacroId))
            {
                _game.ShowToast("A custom command can't run itself, even through another one");
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

        /// <summary>Click a row: next target / direction / item / seed. Switching Go to onto "tile" asks for one.</summary>
        public void CycleOption(int index)
        {
            if (!CanEdit) return;
            var before = Current[index];
            var after = before.WithNextOption();
            for (int guard = 0; guard < 16 && !Allowed(after) && !after.Equals(before); guard++)
                after = after.WithNextOption(); // e.g. a water bot's Grab skips the seeds
            if (after.Equals(before) || !Allowed(after))
            {
                if (!before.Equals(before.WithNextOption())) _game.ShowToast(CantDoMessage(before.WithNextOption()));
                return;
            }
            if (after.NeedsTile && !before.NeedsTile && _bot != null) after.Tile = _bot.Home;
            Current.Set(index, after);
            if (after.NeedsTile) PickTile(index);
            else if (before.NeedsTile) _game.CancelPick();
        }

        public void AdjustCount(int index, int delta)
        {
            if (!CanEdit || index < 0 || index >= Current.Count) return;
            var c = Current[index];
            Current.Set(index, c.WithCount(c.Count + delta));
        }

        /// <summary>Let the player click the map to choose this row's "Go to" tile.</summary>
        public void PickTile(int index)
        {
            if (!CanEdit || index < 0 || index >= Current.Count) return;
            var routine = Current;
            var original = routine[index];
            _game.BeginPick(cell =>
            {
                // Only apply if the row is still there and unchanged.
                if (index >= routine.Count || !routine[index].Equals(original))
                {
                    _game.ShowToast("That Go to row changed - click 'pick' again");
                    return;
                }
                routine.Set(index, original.WithTile(cell));
            });
            _game.ShowToast("Click a tile on the map for this Go to (Esc to cancel)");
        }

        void ClearAll()
        {
            if (CanEdit) Current.Clear();
        }

        void NewMacro()
        {
            if (World.IsGameOver) return;
            var macro = Book.Create();
            if (macro == null)
            {
                _game.ShowToast($"You can keep {CommandLibrary.MaxMacros} custom commands");
                return;
            }
            OpenMacro(macro.Id);
        }

        /// <summary>Copies the routine on screen into a brand-new custom command and opens it, ready to name.</summary>
        void SaveAsMacro()
        {
            if (World.IsGameOver) return;
            var routine = Current;
            if (routine == null || routine.Count == 0)
            {
                _game.ShowToast("Nothing to save yet - add some commands first");
                return;
            }
            if (Book.IsFull)
            {
                _game.ShowToast($"You can keep {CommandLibrary.MaxMacros} custom commands - delete one first");
                return;
            }

            string name = _macro != null ? _macro.Name + " copy" : (_bot != null ? _bot.Name + "'s Day" : null);
            var macro = Book.Create(name, routine);
            OpenMacro(macro.Id);
            _nameField.ActivateInputField(); // selects the name so typing replaces it
            _game.ShowToast($"Saved {macro.Routine.Count} commands as a custom command. Name it, then drag it into any routine.");
        }

        void OnNameEdited(string name)
        {
            if (_macro == null) return;
            Book.Rename(_macro, name);
            _nameField.text = _macro.Name;
        }

        void DeleteMacroClicked()
        {
            if (_macro == null || !CanEdit) return;
            if (_deleteArmedUntil <= 0f)
            {
                _deleteArmedUntil = Time.unscaledTime + 3f;
                _deleteText.text = "Sure?";
                return;
            }
            var macro = _macro;
            DisarmDelete();
            _macro = null;
            World.DeleteMacro(macro); // also strips its runs; raises Library.Changed
            _game.ShowToast($"Deleted '{macro.Name}' and every use of it");
        }

        // ------------------------------------------------------------------ drag and drop

        public void BeginDrag(CommandBlockView source, PointerEventData e)
        {
            if (!CanEdit || _dragging) return;
            if (source.FromPalette && !Allowed(source.Command))
            {
                _game.ShowToast(CantDoMessage(source.Command));
                return;
            }
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
