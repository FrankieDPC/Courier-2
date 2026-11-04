using System;
using System.Collections.Generic;
using System.Text;
using Wizard.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Wizard.Game
{
    /// <summary>
    /// The HUD: top bar (day/doom countdown, clock, tower HP, controls, stores), left panel
    /// (constructs, odd jobs, build, tower), right panel (routine editor), an info card for whatever
    /// is under the mouse, toasts, and the end-of-game card.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        const float SidePanelTop = 84f;
        const float LeftWidth = 258f;
        const float RightWidth = 364f;

        GameController _game;
        World World => _game.World;
        RectTransform _root;

        // top bar
        Text _dayText;
        RectTransform _dayFill;
        Image _dayFillImage;
        Text _hpText;
        RectTransform _hpFill;
        Text _primaryText;
        Text _speedText;
        Text _autoText;
        readonly Dictionary<Resource, Text> _stockTexts = new Dictionary<Resource, Text>();

        // left panel
        RectTransform _constructList;
        readonly List<(Construct c, Image bg, Text text)> _constructButtons = new List<(Construct, Image, Text)>();
        Button _summonButton;
        Text _summonText;
        RectTransform _jobList;
        readonly List<(Job job, Text text)> _jobLabels = new List<(Job, Text)>();
        readonly List<(BuildingType type, Button button)> _buildButtons = new List<(BuildingType, Button)>();
        Button _fortifyButton;
        Text _fortifyText;
        Button _expandButton;
        Text _expandText;
        Text _forecast;

        // info, toast, game over
        Image _infoPanel;
        Text _infoText;
        CanvasGroup _toastGroup;
        Text _toastText;
        float _toastUntil;
        PreviewPanel _previewPanel;
        GameObject _gameOver;
        Text _gameOverTitle;
        Text _gameOverBody;

        bool _dirty = true;
        bool _jobsDirty = true;
        float _nextInfoRefresh;

        public void Init(GameController game)
        {
            _game = game;
            BuildCanvas();
            BuildTopBar();
            BuildLeftPanel();
            var editor = BuildRightPanel();
            BuildInfoAndToast();
            _previewPanel = UIFactory.Rect("Preview Panel", _root).gameObject.AddComponent<PreviewPanel>();
            _previewPanel.Build(_game, editor);
            UIFactory.Stretch((RectTransform)_previewPanel.transform);
            BuildGameOver();

            World.Ticked += MarkDirty;
            World.DayStarted += MarkAllDirty;
            World.DayEnded += OnDayEnded;
            World.ConstructAdded += OnConstructAdded;
            World.Stockpile.Changed += MarkDirty;
            World.Grid.Expanded += MarkDirty;
            World.JobPosted += OnJobChanged;
            World.JobFinished += OnJobChanged;
            World.GameOver += OnGameOver;
            _game.StateChanged += MarkDirty;
            _game.SelectionChanged += MarkDirty;
            _game.PlacementChanged += MarkDirty;
            _game.Toast += ShowToast;

            RebuildConstructList();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            World.Ticked -= MarkDirty;
            World.DayStarted -= MarkAllDirty;
            World.DayEnded -= OnDayEnded;
            World.ConstructAdded -= OnConstructAdded;
            World.Stockpile.Changed -= MarkDirty;
            World.Grid.Expanded -= MarkDirty;
            World.JobPosted -= OnJobChanged;
            World.JobFinished -= OnJobChanged;
            World.GameOver -= OnGameOver;
            _game.StateChanged -= MarkDirty;
            _game.SelectionChanged -= MarkDirty;
            _game.PlacementChanged -= MarkDirty;
            _game.Toast -= ShowToast;
        }

        void MarkDirty() => _dirty = true;

        void MarkAllDirty()
        {
            _dirty = true;
            _jobsDirty = true;
        }

        void OnDayEnded(DayReport _) => MarkAllDirty();
        void OnJobChanged(Job _) => MarkAllDirty();

        void OnConstructAdded(Construct _)
        {
            RebuildConstructList();
            _dirty = true;
        }

        // ------------------------------------------------------------------ layout

        void BuildCanvas()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _root = (RectTransform)transform;
        }

        void BuildTopBar()
        {
            var bar = UIFactory.Panel("Top Bar", _root, Palette.Panel);
            UIFactory.Place(bar.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -12), new Vector2(-24, 60));
            var h = UIFactory.HStack(bar, 10, 10);
            h.padding = new RectOffset(16, 16, 10, 10);

            _dayText = UIFactory.Label("Day", bar.transform, "", 19, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _dayText.lineSpacing = 0.9f;
            UIFactory.Size(_dayText, -1, 220);

            _dayFill = Meter(bar.transform, "Clock", 150, Palette.Accent, out _dayFillImage);

            var hpBox = UIFactory.Rect("Tower HP", bar.transform);
            UIFactory.Size(hpBox, -1, 170, 0);
            var hpStack = UIFactory.VStack(hpBox, 2);
            hpStack.childAlignment = TextAnchor.MiddleLeft;
            _hpText = UIFactory.Label("Text", hpBox, "", 14, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_hpText, 18);
            var hpTrack = UIFactory.Panel("Track", hpBox, Palette.PanelSunken, true, 0.4f);
            UIFactory.Size(hpTrack, 12);
            var hpFill = UIFactory.Panel("Fill", hpTrack.transform, Palette.Good, true, 0.4f);
            _hpFill = hpFill.rectTransform;
            UIFactory.Stretch(_hpFill);

            var primary = UIFactory.Button("Start", bar.transform, "Start Day", Palette.Accent, Color.white, 16, _game.PrimaryAction, out _primaryText);
            _primaryText.fontStyle = FontStyle.Bold;
            UIFactory.Size(primary, -1, 150);
            var speed = UIFactory.Button("Speed", bar.transform, "Speed 1x", Palette.Button, Palette.Ink, 15, _game.CycleSpeed, out _speedText);
            UIFactory.Size(speed, -1, 96);
            var auto = UIFactory.Button("Auto", bar.transform, "Auto-run: off", Palette.Button, Palette.Ink, 15, _game.ToggleAutoRun, out _autoText);
            UIFactory.Size(auto, -1, 126);

            var spacer = UIFactory.Rect("Spacer", bar.transform);
            UIFactory.Size(spacer, -1, -1, 1);

            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                if (r == Resource.None || r == Resource.Parcel) continue;
                var chip = UIFactory.Rect("Chip " + r, bar.transform);
                UIFactory.Size(chip, -1, r == Resource.Gold ? 92 : 74, 0).minWidth = 52; // can squeeze on narrow screens
                var ch = UIFactory.HStack(chip, 5);
                ch.childForceExpandHeight = false;
                ch.childAlignment = TextAnchor.MiddleLeft;
                var sw = UIFactory.Panel("Swatch", chip, Palette.For(r), true, 0.4f);
                UIFactory.Size(sw, 14, 14);
                var t = UIFactory.Label("Text", chip, "", 14, Palette.Ink);
                UIFactory.Size(t, 34, -1, 1);
                _stockTexts[r] = t;
            }
        }

        static RectTransform Meter(Transform parent, string name, float width, Color color, out Image fillImage)
        {
            var track = UIFactory.Panel(name, parent, Palette.PanelSunken, true, 0.5f);
            UIFactory.Size(track, -1, width);
            fillImage = UIFactory.Panel("Fill", track.transform, color, true, 0.5f);
            var fill = fillImage.rectTransform;
            UIFactory.Stretch(fill, 0, 0, 12, 12);
            fill.anchorMax = new Vector2(0, 1);
            return fill;
        }

        void BuildLeftPanel()
        {
            var panel = UIFactory.Panel("Left Panel", _root, Palette.Panel);
            UIFactory.Place(panel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -SidePanelTop), new Vector2(LeftWidth, 0));
            UIFactory.VStack(panel, 5, 12);
            UIFactory.FitHeight(panel);
            const float half = (LeftWidth - 24 - 6) / 2f;

            SectionTitle(panel.transform, "Constructs");
            _constructList = UIFactory.Rect("Constructs", panel.transform);
            UIFactory.Grid(_constructList, new Vector2(half, 34), new Vector2(6, 4), 2);
            _summonButton = UIFactory.Button("Summon", panel.transform, "", Palette.Button, Palette.Ink, 13, _game.SummonConstruct, out _summonText);
            UIFactory.Size(_summonButton, 38);

            SectionTitle(panel.transform, "Odd jobs");
            _jobList = UIFactory.Rect("Jobs", panel.transform);
            UIFactory.VStack(_jobList, 4);

            SectionTitle(panel.transform, "Build");
            var grid = UIFactory.Rect("Build Grid", panel.transform);
            UIFactory.Grid(grid, new Vector2(half, 40), new Vector2(6, 4), 2);
            int count = 0;
            foreach (var type in Catalogue.Buildable)
            {
                var t = type;
                count++;
                var btn = UIFactory.Button(t.Id, grid, $"{t.DisplayName}\n<size=11>{ResourceAmount.Describe(t.BuildCost)}</size>",
                    Palette.Button, Palette.Ink, 13, () => _game.BeginPlacement(t), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.lineSpacing = 0.9f;
                label.rectTransform.offsetMin = new Vector2(16, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.For(t), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(6, 24));
                _buildButtons.Add((t, btn));
            }
            int rows = (count + 1) / 2;
            UIFactory.Size(grid, rows * 40 + (rows - 1) * 4);

            SectionTitle(panel.transform, "Tower");
            var towerRow = UIFactory.Rect("Tower Actions", panel.transform);
            UIFactory.Grid(towerRow, new Vector2(half, 40), new Vector2(6, 4), 2);
            UIFactory.Size(towerRow, 40);
            _fortifyButton = UIFactory.Button("Fortify", towerRow, "", Palette.Button, Palette.Ink, 13, _game.Fortify, out _fortifyText);
            _expandButton = UIFactory.Button("Expand", towerRow, "", Palette.Button, Palette.Ink, 13, _game.Expand, out _expandText);

            _forecast = UIFactory.Label("Forecast", panel.transform, "", 12, Palette.Doom);

            var help = UIFactory.Label("Help", panel.transform,
                "Space start/pause - P preview day - F speed - Tab next\nRight-drag/WASD pan - Scroll zoom - Esc cancel", 11, Palette.InkSoft);
            help.lineSpacing = 1.05f;
        }

        static void SectionTitle(Transform parent, string text)
        {
            var t = UIFactory.Label("Title " + text, parent, text.ToUpperInvariant(), 12, Palette.InkSoft, TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Size(t, 20);
        }

        RoutineEditor BuildRightPanel()
        {
            var holder = UIFactory.Rect("Routine Editor", _root);
            holder.anchorMin = new Vector2(1, 0);
            holder.anchorMax = new Vector2(1, 1);
            holder.pivot = new Vector2(1, 1);
            holder.offsetMin = new Vector2(-12 - RightWidth, 12);
            holder.offsetMax = new Vector2(-12, -SidePanelTop);
            var editor = holder.gameObject.AddComponent<RoutineEditor>();
            editor.Build(_game, _root);
            return editor;
        }

        void BuildInfoAndToast()
        {
            _infoPanel = UIFactory.Panel("Info", _root, Palette.Panel);
            UIFactory.Place(_infoPanel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-50, 12), new Vector2(560, 0));
            _infoPanel.raycastTarget = false; // otherwise hovering under it flickers and blocks map clicks
            UIFactory.VStack(_infoPanel, 0, 12);
            UIFactory.FitHeight(_infoPanel);
            _infoText = UIFactory.Label("Text", _infoPanel.transform, "", 15, Palette.Ink);
            _infoPanel.gameObject.SetActive(false);

            var toast = UIFactory.Panel("Toast", _root, Palette.Ink);
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-50, -SidePanelTop), new Vector2(640, 44));
            toast.raycastTarget = false;
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            _toastText = UIFactory.Label("Text", toast.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_toastText.rectTransform, 12, 12, 4, 4);
        }

        void BuildGameOver()
        {
            var dim = UIFactory.Panel("Game Over", _root, new Color(0.1f, 0.06f, 0.16f, 0.55f), false);
            UIFactory.Stretch(dim.rectTransform);
            _gameOver = dim.gameObject;

            var card = UIFactory.Panel("Card", dim.transform, Palette.Panel);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 0));
            var stack = UIFactory.VStack(card, 12, 24);
            stack.childAlignment = TextAnchor.UpperCenter;
            UIFactory.FitHeight(card);

            _gameOverTitle = UIFactory.Label("Title", card.transform, "", 32, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _gameOverBody = UIFactory.Label("Body", card.transform, "", 16, Palette.InkSoft, TextAnchor.UpperCenter);
            var again = UIFactory.Button("Again", card.transform, "Play again", Palette.Accent, Color.white, 18, _game.Restart, out var againText);
            againText.fontStyle = FontStyle.Bold;
            UIFactory.Size(again, 46);

            _gameOver.SetActive(false);
        }

        void RebuildConstructList()
        {
            UIFactory.DestroyChildren(_constructList);
            _constructButtons.Clear();
            foreach (var construct in World.Constructs)
            {
                var c = construct;
                var btn = UIFactory.Button(c.Name, _constructList, "", Palette.Button, Palette.Ink, 13, () => _game.SelectConstruct(c), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.lineSpacing = 0.9f;
                label.rectTransform.offsetMin = new Vector2(18, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.Construct(c.Id), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(8, 20));
                _constructButtons.Add((c, (Image)btn.targetGraphic, label));
            }
            int rows = (World.Constructs.Count + 1) / 2;
            UIFactory.Size(_constructList, Mathf.Max(0, rows * 34 + (rows - 1) * 4));
            _dirty = true;
        }

        void RebuildJobs()
        {
            UIFactory.DestroyChildren(_jobList);
            _jobLabels.Clear();
            int n = 0;
            foreach (var job in World.OpenJobs)
            {
                var j = job;
                n++;
                var btn = UIFactory.Button("Job " + j.Id, _jobList, JobText(j), Palette.PanelSunken, Palette.Ink, 12, () => _game.SelectBuilding(j.Giver), out var label);
                _jobLabels.Add((j, label));
                label.alignment = TextAnchor.MiddleLeft;
                label.lineSpacing = 0.95f;
                label.rectTransform.offsetMin = new Vector2(16, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.For(j.Resource), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(6, 26));
                UIFactory.Size(btn, 40);
            }
            if (n == 0)
            {
                string msg = World.IsDoomDay ? "No odd jobs today. Only the Doom." : "No jobs on the board right now.";
                var t = UIFactory.Label("None", _jobList, msg, 12, Palette.InkSoft);
                UIFactory.Size(t, 18);
            }
        }

        string JobText(Job j)
        {
            bool dueToday = j.DueDay <= World.Day;
            string due = dueToday ? UIFactory.Colour("due today", Palette.Error) : $"due end of day {j.DueDay}";
            return $"{j.Title}  <b>{j.Progress}/{j.Amount}</b>\n<size=11>{due} - pays {ResourceAmount.Describe(j.Reward)}</size>";
        }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (_toastGroup.alpha > 0f)
                _toastGroup.alpha = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.5f);

            if (_jobsDirty)
            {
                _jobsDirty = false;
                RebuildJobs();
            }

            if (_dirty)
            {
                _dirty = false;
                RefreshTopBar();
                RefreshLeftPanel();
            }

            // Keep the hover card above the preview card when both are showing.
            float lift = _previewPanel.VisibleHeight;
            _infoPanel.rectTransform.anchoredPosition = new Vector2(-50, lift > 0f ? 12 + lift + 8 : 12);

            if (Time.unscaledTime >= _nextInfoRefresh)
            {
                _nextInfoRefresh = Time.unscaledTime + 0.1f;
                RefreshInfo();
            }
        }

        void RefreshTopBar()
        {
            string sub;
            if (World.IsGameOver) sub = World.Outcome == Outcome.Victory ? "the tower stands" : "the tower has fallen";
            else if (World.IsSiege) sub = _game.Paused ? "THE DOOM (paused)" : "THE DOOM IS HERE";
            else if (World.IsDusk) sub = "dusk: constructs head home";
            else if (World.IsDoomDay) sub = "the Doom arrives today";
            else sub = World.DaysUntilDoom == 1 ? "the Doom arrives tomorrow" : $"Doom in {World.DaysUntilDoom} days";
            var subColor = World.IsDoomDay ? Palette.Doom : Palette.InkSoft;
            _dayText.text = $"Day {World.Day}/{World.Doom.Day}\n<size=13><color={UIFactory.Hex(subColor)}>{sub}</color></size>";

            _dayFill.anchorMax = new Vector2(World.Phase != DayPhase.Planning ? World.DayProgress01 : 0f, 1f);
            _dayFillImage.color = World.IsDoomDay ? Palette.Doom : Palette.Accent;

            float hp = World.TowerMaxHp > 0 ? World.TowerHp / (float)World.TowerMaxHp : 0f;
            _hpText.text = $"Tower {World.TowerHp}/{World.TowerMaxHp} HP";
            _hpFill.anchorMax = new Vector2(hp, 1f);
            _hpFill.GetComponent<Image>().color = hp > 0.5f ? Palette.Good : hp > 0.25f ? Palette.Warning : Palette.Error;

            if (World.IsGameOver) _primaryText.text = "Game over";
            else if (World.Phase == DayPhase.Planning) _primaryText.text = World.IsDoomDay ? "Face the Doom" : "Start Day";
            else _primaryText.text = _game.Paused ? "Resume" : "Pause";
            _speedText.text = $"Speed {_game.Speed:0}x";
            _autoText.text = _game.AutoRunDays ? "Auto-run: on" : "Auto-run: off";

            foreach (var kv in _stockTexts) kv.Value.text = World.Stockpile.Get(kv.Key).ToString();
        }

        void RefreshLeftPanel()
        {
            foreach (var (job, text) in _jobLabels) text.text = JobText(job); // progress changes every delivery

            foreach (var (c, bg, text) in _constructButtons)
            {
                string cargo = c.IsCarrying ? $"{c.CargoCount} {c.Cargo}" : "empty";
                text.text = $"{c.Name}\n<size=11><color={UIFactory.Hex(Palette.InkSoft)}>{cargo}</color></size>";
                bg.color = c == _game.SelectedConstruct ? Palette.AccentSoft : Palette.Button;
            }

            bool over = World.IsGameOver;
            var summonCost = World.NextConstructCost;
            _summonText.text = $"+ Summon construct  <size=11>{ResourceAmount.Describe(summonCost)}</size>";
            _summonButton.interactable = !over && World.CanAfford(summonCost);

            foreach (var (type, button) in _buildButtons)
            {
                button.interactable = !over && World.CanAfford(type.BuildCost);
                ((Image)button.targetGraphic).color = _game.PlacingType == type ? Palette.AccentSoft : Palette.Button;
            }

            var fortifyCost = World.NextFortifyCost;
            _fortifyText.text = $"Fortify +{Balance.FortifyHp} HP\n<size=11>{ResourceAmount.Describe(fortifyCost)}</size>";
            _fortifyButton.interactable = !over && World.CanAfford(fortifyCost);

            if (World.Grid.CanExpand)
            {
                var cost = World.NextExpansionCost;
                _expandText.text = $"Expand village\n<size=11>{ResourceAmount.Describe(cost)}</size>";
                _expandButton.interactable = !over && World.CanAfford(cost);
            }
            else
            {
                _expandText.text = "Fully expanded";
                _expandButton.interactable = false;
            }

            var doom = World.Doom;
            var gates = new List<string>();
            foreach (var g in doom.Gates) gates.Add(g.ToString());
            string tally = World.IsSiege || World.IsGameOver
                ? $"Slain {World.MonstersSlain} of {doom.TotalMonsters}."
                : $"{doom.Count(Monsters.Shade)} shades and {doom.Count(Monsters.Brute)} brutes will come through the {string.Join(" and ", gates)} gates.";
            _forecast.text = $"<b>Doom forecast:</b> {tally} Wards and the tower fire Charges; Potions in the tower heal it.";
        }

        void RefreshInfo()
        {
            string text = null;

            if (_game.PlacingType != null)
            {
                var t = _game.PlacingType;
                text = $"<b>Placing {t.DisplayName}</b> - click an empty lot beside a road. Right-click or Esc to cancel.";
                if (t.IsWard) text += $"\nRange {t.Defence.Range}, fires {t.Defence.Ammo} for {t.Defence.Damage} damage. Keep it stocked!";
                if (_game.HasHover)
                {
                    var check = World.CheckPlacement(t, _game.HoveredCell);
                    if (check != PlacementCheck.Ok) text += "\n" + UIFactory.Colour(GameController.Explain(check), Palette.Error);
                }
            }
            else if (_game.HasHover && _game.MonsterAt(_game.HoveredCell) is Monster m)
            {
                text = $"<b>{m.Type.Name}</b> - {m.Hp}/{m.Type.Hp} HP, hits the tower for {m.Type.Damage}";
            }
            else if (_game.HasHover && _game.ConstructAt(_game.HoveredCell) is Construct c)
            {
                text = Describe(c);
            }
            else
            {
                var b = _game.HoveredBuilding ?? _game.SelectedBuilding;
                if (b != null) text = Describe(b);
            }

            _infoPanel.gameObject.SetActive(text != null);
            if (text != null && _infoText.text != text) _infoText.text = text;
        }

        string Describe(Building b)
        {
            var t = b.Type;
            var sb = new StringBuilder();

            switch (t.Role)
            {
                case BuildingRole.Tower:
                    sb.Append($"<b>{b.Name}</b> - {World.TowerHp}/{World.TowerMaxHp} HP. Deliveries here fill your stores.\n");
                    sb.Append($"Fires Charges (range {Balance.TowerDefence.Range}) at the Doom; Potions here heal it {Balance.PotionHeal} HP each.\n");
                    sb.Append($"Delivered today: {World.DeliveredToday}");
                    return sb.ToString();

                case BuildingRole.Cottage:
                    sb.Append($"<b>{b.Name}</b>");
                    bool any = false;
                    foreach (var j in World.OpenJobs)
                    {
                        if (j.Giver != b) continue;
                        any = true;
                        string verb = j.Kind == JobKind.Deliver ? "Drop off here" : "Pick up here, drop off at the tower";
                        sb.Append($"\n{j.Title} ({j.Progress}/{j.Amount}), due end of day {j.DueDay}. {verb}.");
                    }
                    if (!any) sb.Append(" - nothing needed right now.");
                    return sb.ToString();

                case BuildingRole.Ward:
                    var d = t.Defence;
                    sb.Append($"<b>Ward</b> - {World.AmmoOf(b)}/{t.InputCapacity} {d.Ammo}\n");
                    sb.Append($"Range {d.Range}, {d.Damage} damage per shot. Drop off {d.Ammo} here to arm it.");
                    return sb.ToString();
            }

            sb.Append($"<b>{t.DisplayName}</b>  {UIFactory.Colour(StatusText(b.Status), Palette.For(b.Status))}");
            if (b.Status == BuildingStatus.Working) sb.Append($" ({b.CycleProgress01 * 100f:0}%)");
            sb.Append('\n');

            string makes = ResourceAmount.Describe(t.Outputs);
            if (t.Inputs.Count > 0) sb.Append($"Turns {ResourceAmount.Describe(t.Inputs)} into {makes} every {t.CycleTicks} ticks.\n");
            else sb.Append($"Makes {makes} every {t.CycleTicks} ticks.\n");

            if (t.Inputs.Count > 0)
            {
                var r = t.Inputs[0].Resource;
                sb.Append($"Waiting input: {b.Input.Get(r)}/{t.InputCapacity} {r}    ");
            }
            if (t.Outputs.Count > 0)
            {
                var r = t.Outputs[0].Resource;
                sb.Append($"Ready to collect: {b.Output.Get(r)}/{t.OutputCapacity} {r}");
            }
            return sb.ToString();
        }

        static string Describe(Construct c)
        {
            string cargo = c.IsCarrying ? $"carrying {c.CargoCount} {c.Cargo}" : "empty";
            string last = c.LastPath.Count > 0
                ? $"line {c.LastPath[0].Index + 1}: {CommandInfo.Describe(c.LastResult)}"
                : "waiting at the tower";
            return $"<b>{c.Name}</b> - {cargo} - {last}\nDelivered to the tower today: {c.DeliveredToday}";
        }

        static string StatusText(BuildingStatus s)
        {
            switch (s)
            {
                case BuildingStatus.Working: return "working";
                case BuildingStatus.NeedsInput: return "needs input";
                case BuildingStatus.OutputFull: return "output full - send a construct";
                default: return "idle";
            }
        }

        void OnGameOver(Outcome outcome)
        {
            bool won = outcome == Outcome.Victory;
            _gameOverTitle.text = won ? "The tower stands!" : "The tower has fallen";
            _gameOverTitle.color = won ? Palette.Accent : Palette.Doom;
            _gameOverBody.text =
                $"Tower: {World.TowerHp}/{World.TowerMaxHp} HP\n" +
                $"Monsters slain: {World.MonstersSlain} of {World.Doom.TotalMonsters}\n" +
                $"Odd jobs done: {World.JobsCompleted} (missed {World.JobsFailed})\n" +
                $"Constructs: {World.Constructs.Count}   Spells: {World.Spellbook.Spells.Count}";
            _gameOver.SetActive(true);
            MarkAllDirty();
        }

        void ShowToast(string message)
        {
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastUntil = Time.unscaledTime + 3.5f;
        }
    }
}
