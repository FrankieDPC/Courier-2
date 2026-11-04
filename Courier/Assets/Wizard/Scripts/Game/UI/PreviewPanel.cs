using System.Collections.Generic;
using System.Text;
using Wizard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Wizard.Game
{
    /// <summary>
    /// Bottom-centre card shown while previewing: play/pause, a timeline scrubber, and a summary of the
    /// selected construct's day (drop-offs, jobs finished, end cargo, and problems you can click to
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
        Construct _shownConstruct;
        string _shownSpellNames;

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
            _slider.onValueChanged.AddListener(v => Preview.SetTick(v));
            _tickText = UIFactory.Label("Tick", transport, "", 13, Palette.InkSoft, TextAnchor.MiddleRight);
            UIFactory.Size(_tickText, 24, 104);

            _summary = UIFactory.Label("Summary", bg.transform, "", 13, Palette.Ink);
            _summary.lineSpacing = 1.1f;

            _problemList = UIFactory.Rect("Problems", bg.transform);
            UIFactory.VStack(_problemList, 3);

            _panel.gameObject.SetActive(false);
        }

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
            var c = _game.SelectedConstruct;

            if (preview.Version != _shownVersion || c != _shownConstruct || SpellNames() != _shownSpellNames)
            {
                _shownVersion = preview.Version;
                _shownConstruct = c;
                _shownSpellNames = SpellNames();
                _slider.minValue = result.StartTick;
                _slider.maxValue = Mathf.Max(result.StartTick + 1, result.EndTick);
                RebuildSummary(result, c);
            }

            _slider.SetValueWithoutNotify(preview.PlayTick);
            _playText.text = preview.Playing ? "Pause" : "Play";
            _tickText.text = $"tick {Mathf.CeilToInt(preview.PlayTick - 0.0001f)} / {result.EndTick}";
        }

        string SpellNames()
        {
            var sb = new StringBuilder();
            foreach (var s in _game.World.Spellbook.Spells) sb.Append(s.Name).Append('|');
            return sb.ToString();
        }

        void RebuildSummary(DayPreview result, Construct c)
        {
            UIFactory.DestroyChildren(_problemList);

            string when = result.StartTick > 0 ? $"rest of day {result.Day} (from tick {result.StartTick})" : $"day {result.Day}";
            _title.text = c != null ? $"Preview: {c.Name}'s {when}" : $"Preview of {when}";

            var sb = new StringBuilder();
            sb.Append(UIFactory.Colour("Nothing here really happens. Edits update the preview instantly.", Palette.InkSoft));
            if (result.IsDoomDay) sb.Append("\n" + UIFactory.Colour("Doom day: the siege is simulated too.", Palette.Doom));

            if (c == null)
            {
                _summary.text = sb.ToString();
                return;
            }

            // Drop-offs
            var drops = result.DeliveriesFor(c.Id);
            sb.Append("\n<b>Drops off:</b> ");
            if (drops.Count == 0) sb.Append("nothing");
            for (int i = 0; i < drops.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                sb.Append($"{drops[i].Amount} {drops[i].Resource} at {drops[i].Target}");
            }

            // Jobs (any construct)
            sb.Append("\n<b>Jobs finished:</b> ");
            if (result.JobsDone.Count == 0) sb.Append("none");
            for (int i = 0; i < result.JobsDone.Count; i++)
            {
                var j = result.JobsDone[i];
                if (i > 0) sb.Append(" · ");
                sb.Append($"{j.Title} (tick {j.Tick}, {ResourceAmount.Describe(j.Reward)})");
            }

            // End of the day: dusk walk home (not on the doom day)
            if (result.DuskTick >= 0)
            {
                var banked = result.BankedAtDuskFor(c.Id);
                string cargo = banked.Count == 0 ? "empty-handed" : $"and empties {ResourceAmount.Describe(banked)} into the tower";
                sb.Append($"\n<b>Dusk</b> (tick {result.DuskTick}): walks home {cargo}.");
            }
            else
            {
                var frames = result.FramesFor(c.Id);
                if (frames.Count > 0)
                {
                    var last = frames[frames.Count - 1];
                    sb.Append(last.CargoCount > 0
                        ? $"\n<b>Ends the day</b> carrying {last.CargoCount} {last.Cargo}."
                        : "\n<b>Ends the day</b> empty-handed.");
                }
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
            var book = _game.World.Spellbook;
            string where = p.Point.IsMain
                ? $"line {p.Point.Index + 1}"
                : $"{(book.Get(p.Point.SpellId)?.Name ?? "spell")} line {p.Point.Index + 1}";
            string what = CommandInfo.Name(p.Command.Type);
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
            if (p.Point.IsMain) _editor.ShowConstructRoutine();
            else _editor.OpenSpell(p.Point.SpellId);
        }
    }
}
