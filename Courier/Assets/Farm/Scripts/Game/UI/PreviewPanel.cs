using System.Collections.Generic;
using System.Text;
using Farm.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Farm.Game
{
    /// <summary>
    /// Bottom-centre card shown while previewing: play/pause, a timeline scrubber, and a summary of the
    /// selected bot's day (tasks done, what sells tonight, thirsty crops, and problems you can click to
    /// jump to).
    /// </summary>
    public sealed class PreviewPanel : MonoBehaviour
    {
        const int MaxProblemRows = 5;

        GameController _game;
        RoutineEditor _editor;
        PreviewController Preview => _game.Preview;

        RectTransform _panel;
        Text _title;
        Text _playText;
        Slider _slider;
        Text _tickText;
        Text _summary;
        RectTransform _problemList;

        int _shownVersion = -1;
        Bot _shownBot;
        bool _namesChanged;
        bool _syncing;
        int _shownTick = int.MinValue;
        bool _shownPlaying;

        /// <summary>Height of the card when visible (0 when hidden), so the info card can sit above it.</summary>
        public float VisibleHeight => _panel != null && _panel.gameObject.activeSelf ? _panel.rect.height : 0f;

        public void Build(GameController game, RoutineEditor editor)
        {
            _game = game;
            _editor = editor;

            var bg = UIFactory.Panel("Preview", transform, Palette.Panel);
            _panel = bg.rectTransform;
            UIFactory.Place(_panel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-50, 12), new Vector2(700, 0));
            UIFactory.VStack(bg, 6, 12);
            UIFactory.FitHeight(bg);

            var top = UIFactory.Rect("Top", bg.transform);
            UIFactory.Size(top, 26, -1, -1, 0);
            UIFactory.HStack(top, 8).childForceExpandHeight = false;
            _title = UIFactory.Label("Title", top, "", 16, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_title, 24, -1, 1);
            var close = UIFactory.Button("Close", top, "Close (P)", Palette.Button, Palette.Ink, 13, () => Preview.Close(), out _);
            UIFactory.Size(close, 24, 84);

            var transport = UIFactory.Rect("Transport", bg.transform);
            UIFactory.Size(transport, 28, -1, -1, 0);
            UIFactory.HStack(transport, 10).childForceExpandHeight = false;
            var play = UIFactory.Button("Play", transport, "Pause", Palette.Accent, Color.white, 14, () => Preview.TogglePlay(), out _playText);
            UIFactory.Size(play, 28, 76);
            _slider = MakeSlider(transport);
            UIFactory.Size(_slider, 28, -1, 1);
            _slider.onValueChanged.AddListener(v =>
            {
                if (!_syncing) Preview.SetTick(v); // ignore clamps caused by us changing the range
            });
            _tickText = UIFactory.Label("Tick", transport, "", 13, Palette.InkSoft, TextAnchor.MiddleRight);
            UIFactory.Size(_tickText, 24, 104);

            _summary = UIFactory.Label("Summary", bg.transform, "", 13, Palette.Ink);
            _summary.lineSpacing = 1.1f;

            _problemList = UIFactory.Rect("Problems", bg.transform);
            UIFactory.VStack(_problemList, 3);

            _panel.gameObject.SetActive(false);
            _game.World.Library.Changed += OnLibraryChanged;
        }

        void OnDestroy()
        {
            if (_game != null && _game.World != null) _game.World.Library.Changed -= OnLibraryChanged;
        }

        void OnLibraryChanged() => _namesChanged = true;

        static Slider MakeSlider(Transform parent)
        {
            var root = UIFactory.Rect("Timeline", parent);
            var slider = root.gameObject.AddComponent<Slider>();

            var track = UIFactory.Panel("Track", root, Palette.PanelSunken, true, 0.4f);
            UIFactory.Stretch(track.rectTransform, 0, 0, 9, 9);

            var fillArea = UIFactory.Rect("Fill Area", root);
            UIFactory.Stretch(fillArea, 0, 0, 9, 9);
            var fill = UIFactory.Panel("Fill", fillArea, Palette.AccentSoft, true, 0.4f);
            UIFactory.Stretch(fill.rectTransform);

            var handleArea = UIFactory.Rect("Handle Area", root);
            UIFactory.Stretch(handleArea, 6, 6, 0, 0);
            var handle = UIFactory.Panel("Handle", handleArea, Palette.Accent, true, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(12, 0);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            return slider;
        }

        void Update()
        {
            var preview = Preview;
            bool show = preview != null && preview.IsOpen && preview.Result != null;
            if (_panel.gameObject.activeSelf != show) _panel.gameObject.SetActive(show);
            if (!show) return;

            var result = preview.Result;
            var c = _game.SelectedBot;

            bool stale = preview.Version != _shownVersion || c != _shownBot || _namesChanged;
            // Don't destroy buttons while the mouse is down: the press may be on one of them.
            bool pressing = Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (stale && !pressing)
            {
                _shownVersion = preview.Version;
                _shownBot = c;
                _namesChanged = false;
                _syncing = true;
                _slider.minValue = result.StartTick;
                _slider.maxValue = Mathf.Max(result.StartTick + 1, result.EndTick);
                _syncing = false;
                RebuildSummary(result, c);
                _shownTick = int.MinValue;
            }

            _slider.SetValueWithoutNotify(preview.PlayTick);
            int tick = Mathf.CeilToInt(preview.PlayTick - 0.0001f);
            if (tick != _shownTick)
            {
                _shownTick = tick;
                _tickText.text = $"tick {tick} / {result.EndTick}";
            }
            if (preview.Playing != _shownPlaying || _shownTick == tick)
            {
                _shownPlaying = preview.Playing;
                _playText.text = preview.Playing ? "Pause" : "Play";
            }
        }

        void RebuildSummary(DayPreview result, Bot c)
        {
            UIFactory.DestroyChildren(_problemList);

            string when = result.StartTick > 0 ? $"rest of day {result.Day} (from tick {result.StartTick})" : $"day {result.Day}";
            _title.text = c != null ? $"Preview: {c.Name}'s {when}" : $"Preview of {when}";

            var sb = new StringBuilder();
            sb.Append(UIFactory.Colour("Nothing here really happens. Edits update the preview instantly.", Palette.InkSoft));
            if (c == null)
            {
                _summary.text = sb.ToString();
                return;
            }

            // What this bot gets done
            var parts = new List<string>();
            void Count(CommandType t, string verb)
            {
                int n = result.TasksDone(c.Id, t);
                if (n > 0) parts.Add($"{verb} {n}");
            }
            Count(CommandType.Till, "tills");
            Count(CommandType.Plant, "plants");
            Count(CommandType.Water, "waters");
            Count(CommandType.Harvest, "harvests");
            Count(CommandType.Ship, "ships");
            sb.Append($"\n<b>{c.Name}</b> " + (parts.Count == 0 ? "gets nothing done" : string.Join(", ", parts)) + ".");

            var emptied = result.EmptiedAtDusk(c.Id);
            if (result.DuskTick >= 0)
                sb.Append($"\n<b>Dusk</b> (tick {result.DuskTick}): walks home" + (emptied.IsEmpty ? " empty-handed." : $" and unloads {emptied}."));

            // The whole farm overnight
            var night = result.Night;
            if (night != null)
            {
                string sales = night.GoldEarned > 0 ? $"sells {night.Sold} for <b>{night.GoldEarned}g</b>" : "sells nothing";
                string dry = night.CropsDry > 0 ? UIFactory.Colour($"{night.CropsDry} crop(s) go thirsty", Palette.Warning) : UIFactory.Colour("every crop gets watered", Palette.Good);
                sb.Append($"\n<b>Tonight</b> the farm {sales}; {night.CropsWatered} crop(s) grow, {dry}.");
                if (night.CropsRipe > 0) sb.Append($" {night.CropsRipe} will be ripe tomorrow.");
            }

            var problems = result.ProblemsFor(c.Id);
            sb.Append(problems.Count == 0
                ? "\n" + UIFactory.Colour("No problems: every command did something.", Palette.Good)
                : "\n<b>Problems</b> (click to jump there):");
            _summary.text = sb.ToString();

            for (int i = 0; i < problems.Count && i < MaxProblemRows; i++) AddProblemRow(problems[i]);
            if (problems.Count > MaxProblemRows)
            {
                var more = UIFactory.Label("More", _problemList, $"...and {problems.Count - MaxProblemRows} more", 12, Palette.InkSoft);
                UIFactory.Size(more, 16);
            }
        }

        void AddProblemRow(PreviewProblem p)
        {
            var library = _game.World.Library;
            string where = p.Point.IsMain
                ? $"line {p.Point.Index + 1}"
                : $"{(library.Get(p.Point.MacroId)?.Name ?? "command")} line {p.Point.Index + 1}";
            string what = CommandInfo.Label(p.Command, library);
            var color = CommandInfo.IsHardProblem(p.Result) ? Palette.Error : Palette.Warning;
            string text = $"{UIFactory.Colour("•", color)} {where} {what}: {CommandInfo.Describe(p.Result)}  x{p.Count}  <size=11>(first at tick {p.FirstTick})</size>";

            var problem = p;
            var btn = UIFactory.Button("Problem", _problemList, text, Palette.PanelSunken, Palette.Ink, 12, () => JumpTo(problem), out var label);
            label.alignment = TextAnchor.MiddleLeft;
            UIFactory.Size(btn, 24);
        }

        void JumpTo(PreviewProblem p)
        {
            Preview.SetTick(p.FirstTick);
            if (p.Point.IsMain) _editor.ShowBotRoutine();
            else _editor.OpenMacro(p.Point.MacroId);
        }
    }
}
