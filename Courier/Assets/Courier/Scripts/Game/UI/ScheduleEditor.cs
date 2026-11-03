using System.Collections.Generic;
using Courier.Sim;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Courier.Game
{
    /// <summary>
    /// The drag-and-drop programming panel for the selected courier.
    ///   Palette -> list   drag to insert (or click to append)
    ///   List -> list      drag to reorder
    ///   List -> outside   drag out to delete (or right-click)
    ///   Click a Pick Up / Drop Off row to cycle its side: auto, ahead, left, right
    /// Editing is allowed while planning or while the day is paused.
    /// </summary>
    public sealed class ScheduleEditor : MonoBehaviour
    {
        const float RowHeight = 34f;

        GameController _game;
        RectTransform _canvasRoot;
        CourierBot _bot;

        Text _title;
        Image _titleSwatch;
        Text _hint;
        Text _loopText;
        Button _loopButton;
        Button _clearButton;
        CanvasGroup _paletteGroup;
        RectTransform _listBox;
        ScrollRect _scroll;
        RectTransform _content;
        RectTransform _marker;
        Text _emptyHint;
        Text _status;
        Text _footer;

        readonly List<CommandBlockView> _rows = new List<CommandBlockView>();

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

        public bool CanEdit => _bot != null && (_game.IsPlanning || _game.Paused);

        // ------------------------------------------------------------------ construction

        public void Build(GameController game, RectTransform canvasRoot)
        {
            _game = game;
            _canvasRoot = canvasRoot;

            var panel = UIFactory.Panel("Panel", transform, Palette.Panel);
            UIFactory.Stretch(panel.rectTransform);
            var stack = UIFactory.VStack(panel, 8, 14);

            // Header: swatch + title + loop + clear
            var header = UIFactory.Rect("Header", panel.transform);
            UIFactory.Size(header, 34, -1, -1, 0);
            var headerRow = UIFactory.HStack(header, 8);
            headerRow.childForceExpandHeight = false;
            _titleSwatch = UIFactory.Panel("Swatch", header, Color.white, true, 0.5f);
            UIFactory.Size(_titleSwatch, 18, 18);
            _title = UIFactory.Label("Title", header, "", 20, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_title, 30, -1, 1);
            _loopButton = UIFactory.Button("Loop", header, "Loop: on", Palette.Button, Palette.Ink, 14, ToggleLoop, out _loopText);
            UIFactory.Size(_loopButton, 30, 88);
            _clearButton = UIFactory.Button("Clear", header, "Clear", Palette.Button, Palette.Ink, 14, ClearAll, out _);
            UIFactory.Size(_clearButton, 30, 62);

            _hint = UIFactory.Label("Hint", panel.transform, "", 13, Palette.InkSoft);
            UIFactory.Size(_hint, 36);

            // Palette
            var palette = UIFactory.Rect("Palette", panel.transform);
            var grid = palette.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160, RowHeight);
            grid.spacing = new Vector2(8, 6);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            int paletteRows = (CommandInfo.PaletteOrder.Length + 1) / 2;
            UIFactory.Size(palette, paletteRows * RowHeight + (paletteRows - 1) * 6);
            _paletteGroup = palette.gameObject.AddComponent<CanvasGroup>();
            foreach (var t in CommandInfo.PaletteOrder)
                CommandBlockView.Create(this, palette, new Command(t), true, -1);

            // "Schedule" caption + live status
            var caption = UIFactory.Rect("Caption", panel.transform);
            UIFactory.Size(caption, 22, -1, -1, 0);
            UIFactory.HStack(caption, 6).childForceExpandHeight = false;
            var capText = UIFactory.Label("Label", caption, "Daily schedule", 15, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(capText, -1, 130);
            _status = UIFactory.Label("Status", caption, "", 13, Palette.InkSoft, TextAnchor.MiddleRight);
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = 9;
            _status.resizeTextMaxSize = 13;
            _status.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.Size(_status, 22, -1, 1);

            // Scrollable list (also the drop zone)
            var box = UIFactory.Panel("List", panel.transform, Palette.PanelSunken);
            _listBox = box.rectTransform;
            UIFactory.Size(box, -1, -1, -1, 1);

            var viewport = UIFactory.Rect("Viewport", box.transform);
            UIFactory.Stretch(viewport, 6, 6, 6, 6);
            viewport.gameObject.AddComponent<RectMask2D>();
            // Invisible graphic so the whole viewport receives scroll-wheel events.
            var hit = viewport.gameObject.AddComponent<Image>();
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

            _footer = UIFactory.Label("Footer", panel.transform, "", 13, Palette.InkSoft);
            UIFactory.Size(_footer, 58);

            _game.SelectionChanged += OnSelectionChanged;
            _game.StateChanged += OnStateChanged;
            _game.Town.Grid.Expanded += RefreshFooter;
            OnSelectionChanged();
            Refresh();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            _game.SelectionChanged -= OnSelectionChanged;
            _game.StateChanged -= OnStateChanged;
            _game.Town.Grid.Expanded -= RefreshFooter;
            if (_bot != null) _bot.Schedule.Changed -= Refresh;
        }

        void OnSelectionChanged()
        {
            if (_bot == _game.SelectedBot && _rows.Count == (_bot == null ? 0 : _bot.Schedule.Count)) return;
            CancelDrag();
            if (_bot != null) _bot.Schedule.Changed -= Refresh;
            _bot = _game.SelectedBot;
            if (_bot != null) _bot.Schedule.Changed += Refresh;
            Refresh();
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
            _shownIndex = int.MinValue; // force highlight refresh
        }

        void RebuildRows()
        {
            foreach (var r in _rows)
            {
                r.gameObject.SetActive(false);
                Destroy(r.gameObject);
            }
            _rows.Clear();

            if (_bot != null)
            {
                for (int i = 0; i < _bot.Schedule.Count; i++)
                {
                    var row = CommandBlockView.Create(this, _content, _bot.Schedule[i], false, i);
                    UIFactory.Size(row, RowHeight);
                    _rows.Add(row);
                }
            }
            _marker.SetAsLastSibling();
            _emptyHint.gameObject.SetActive(_bot != null && _bot.Schedule.Count == 0);
        }

        void RefreshHeader()
        {
            bool hasBot = _bot != null;
            _titleSwatch.gameObject.SetActive(hasBot);
            _loopButton.gameObject.SetActive(hasBot);
            _clearButton.gameObject.SetActive(hasBot);

            if (!hasBot)
            {
                _title.text = "No courier selected";
                _hint.text = "Click a courier on the map or in the list on the left.";
                _paletteGroup.alpha = 0.4f;
                return;
            }

            _titleSwatch.color = Palette.Bot(_bot.Id);
            _title.text = _bot.Name;
            _loopText.text = _bot.Loop ? "Loop: on" : "Loop: off";

            bool editable = CanEdit;
            _shownEditable = editable;
            _paletteGroup.alpha = editable ? 1f : 0.4f;
            _clearButton.interactable = editable;
            _hint.text = editable
                ? "Drag blocks into the schedule. Click Pick Up / Drop Off rows to change side. Drag a row out to delete."
                : "Locked while the day runs. Pause (Space) to make changes.";
        }

        void RefreshFooter()
        {
            if (_bot == null)
            {
                _footer.text = "";
                return;
            }

            int n = _bot.Schedule.Count;
            if (n == 0)
            {
                _footer.text = "An empty schedule means this courier stays at the depot all day.";
                return;
            }

            var trace = ScheduleTracer.Trace(_game.Town.Grid, _bot);
            var lines = new List<string>();
            string perDay = _bot.Loop ? $" - about {_game.Town.TicksPerDay / n} loops a day" : "";
            lines.Add($"{n} commands = {n} ticks{perDay}.");

            if (trace.BlockedCount > 0)
                lines.Add(Colour($"{trace.BlockedCount} move(s) hit a dead end (red marker).", Palette.Error));

            if (!_bot.Loop)
                lines.Add("Runs once, then waits where it finishes.");
            else if (trace.ReturnsToStart)
                lines.Add(Colour("Ends where it starts, so it loops cleanly.", Palette.Working));
            else
                lines.Add(Colour($"Ends at {trace.End} facing {trace.EndFacing}, so each loop starts somewhere new.", Palette.Warning));

            _footer.text = string.Join("\n", lines);
        }

        static string Colour(string s, Color c) => $"<color={UIFactory.Hex(c)}>{s}</color>";

        void Update()
        {
            if (_bot == null) return;

            if (CanEdit != _shownEditable) RefreshHeader();

            bool running = !_game.IsPlanning;
            int idx = running ? _bot.LastExecutedIndex : -1;
            if (idx != _shownIndex || _bot.LastResult != _shownResult || _game.Town.Tick != _shownTick)
            {
                _shownIndex = idx;
                _shownResult = _bot.LastResult;
                _shownTick = _game.Town.Tick;
                for (int i = 0; i < _rows.Count; i++) _rows[i].SetState(i == idx, _bot.LastResult);

                string cargo = _bot.IsCarrying ? $"carrying {_bot.CargoCount} {_bot.Cargo}" : "empty";
                if (running && idx >= 0)
                    _status.text = $"{cargo} - line {idx + 1}: {CommandInfo.Describe(_bot.LastResult)}";
                else if (running)
                    _status.text = $"{cargo} - idle";
                else
                    _status.text = cargo;
            }

            if (_dragging) AutoScroll();
        }

        // ------------------------------------------------------------------ editing (called by blocks)

        public void Append(Command c) => Insert(_bot == null ? 0 : _bot.Schedule.Count, c);

        void Insert(int index, Command c)
        {
            if (!CanEdit) return;
            if (!_bot.Schedule.Insert(index, c))
            {
                _game.ShowToast($"A schedule holds at most {Schedule.MaxLength} commands");
                return;
            }
            // Keep the newly added row in view.
            if (index >= _bot.Schedule.Count - 1) Canvas.ForceUpdateCanvases();
            if (index >= _bot.Schedule.Count - 1) _scroll.verticalNormalizedPosition = 0f;
        }

        public void RemoveAt(int index)
        {
            if (!CanEdit) return;
            _bot.Schedule.RemoveAt(index);
        }

        public void CycleSide(int index)
        {
            if (!CanEdit) return;
            _bot.Schedule.Set(index, _bot.Schedule[index].WithNextSide());
        }

        void ToggleLoop()
        {
            if (_bot == null) return;
            _bot.Loop = !_bot.Loop;
            OnStateChanged();
        }

        void ClearAll()
        {
            if (!CanEdit) return;
            _bot.Schedule.Clear();
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
            _ghostText = UIFactory.Label("Label", _ghost, CommandInfo.Label(source.Command), 16, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
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
            _ghostText.text = willDelete ? "Remove" : CommandInfo.Label(_dragSource.Command);
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
                _bot.Schedule.MoveToInsertionPoint(source.Index, insertAt);
            }
            else
            {
                _bot.Schedule.RemoveAt(source.Index);
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
            float edge = 28f;
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
