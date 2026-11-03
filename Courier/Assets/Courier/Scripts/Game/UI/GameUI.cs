using System;
using System.Collections.Generic;
using System.Text;
using Courier.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Courier.Game
{
    /// <summary>
    /// The HUD: top bar (day, clock, controls, stockpile), left panel (couriers, build, expand),
    /// right panel (schedule editor), an info card for whatever is under the mouse, and toasts.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        const float SidePanelTop = 84f;
        const float LeftWidth = 250f;
        const float RightWidth = 364f;

        GameController _game;
        Town Town => _game.Town;
        RectTransform _root;

        // top bar
        Text _dayText;
        RectTransform _dayFill;
        Text _primaryText;
        Text _speedText;
        Text _autoText;
        readonly Dictionary<Resource, Text> _stockTexts = new Dictionary<Resource, Text>();

        // left panel
        RectTransform _botList;
        readonly List<(CourierBot bot, Image bg, Text text)> _botButtons = new List<(CourierBot, Image, Text)>();
        Button _buyBotButton;
        Text _buyBotText;
        readonly List<(BuildingType type, Button button)> _buildButtons = new List<(BuildingType, Button)>();
        Button _expandButton;
        Text _expandText;

        // info + toast
        Image _infoPanel;
        Text _infoText;
        CanvasGroup _toastGroup;
        Text _toastText;
        float _toastUntil;

        bool _dirty = true;
        float _nextInfoRefresh;

        public void Init(GameController game)
        {
            _game = game;
            BuildCanvas();
            BuildTopBar();
            BuildLeftPanel();
            BuildRightPanel();
            BuildInfoAndToast();

            Town.Ticked += MarkDirty;
            Town.DayStarted += MarkDirty;
            Town.DayEnded += OnDayEnded;
            Town.BotAdded += OnBotAdded;
            Town.Stockpile.Changed += MarkDirty;
            Town.Grid.Expanded += MarkDirty;
            _game.StateChanged += MarkDirty;
            _game.SelectionChanged += MarkDirty;
            _game.PlacementChanged += MarkDirty;
            _game.Toast += ShowToast;

            RebuildBotList();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            Town.Ticked -= MarkDirty;
            Town.DayStarted -= MarkDirty;
            Town.DayEnded -= OnDayEnded;
            Town.BotAdded -= OnBotAdded;
            Town.Stockpile.Changed -= MarkDirty;
            Town.Grid.Expanded -= MarkDirty;
            _game.StateChanged -= MarkDirty;
            _game.SelectionChanged -= MarkDirty;
            _game.PlacementChanged -= MarkDirty;
            _game.Toast -= ShowToast;
        }

        void MarkDirty() => _dirty = true;
        void OnDayEnded(DayReport _) => _dirty = true;

        void OnBotAdded(CourierBot _)
        {
            RebuildBotList();
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
            var h = UIFactory.HStack(bar, 12, 10);
            h.padding = new RectOffset(16, 16, 10, 10);

            _dayText = UIFactory.Label("Day", bar.transform, "", 20, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_dayText, -1, 200);

            var track = UIFactory.Panel("Clock", bar.transform, Palette.PanelSunken, true, 0.5f);
            UIFactory.Size(track, -1, 220);
            var fill = UIFactory.Panel("Fill", track.transform, Palette.Accent, true, 0.5f);
            _dayFill = fill.rectTransform;
            UIFactory.Stretch(_dayFill, 0, 0, 12, 12);
            _dayFill.anchorMax = new Vector2(0, 1);

            var primary = UIFactory.Button("Start", bar.transform, "Start Day", Palette.Accent, Color.white, 16, _game.PrimaryAction, out _primaryText);
            _primaryText.fontStyle = FontStyle.Bold;
            UIFactory.Size(primary, -1, 140);
            var speed = UIFactory.Button("Speed", bar.transform, "Speed 1x", Palette.Button, Palette.Ink, 15, _game.CycleSpeed, out _speedText);
            UIFactory.Size(speed, -1, 100);
            var auto = UIFactory.Button("Auto", bar.transform, "Auto-run: off", Palette.Button, Palette.Ink, 15, _game.ToggleAutoRun, out _autoText);
            UIFactory.Size(auto, -1, 130);

            var spacer = UIFactory.Rect("Spacer", bar.transform);
            UIFactory.Size(spacer, -1, -1, 1);

            var stockLabel = UIFactory.Label("Stock Label", bar.transform, "Town Hall:", 15, Palette.InkSoft, TextAnchor.MiddleRight);
            UIFactory.Size(stockLabel, -1, 90);

            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                if (r == Resource.None) continue;
                var chip = UIFactory.Rect("Chip " + r, bar.transform);
                UIFactory.Size(chip, -1, 96, 0);
                var ch = UIFactory.HStack(chip, 6);
                ch.childForceExpandHeight = false;
                ch.childAlignment = TextAnchor.MiddleLeft;
                var sw = UIFactory.Panel("Swatch", chip, Palette.For(r), true, 0.4f);
                UIFactory.Size(sw, 16, 16);
                var t = UIFactory.Label("Text", chip, "", 15, Palette.Ink);
                UIFactory.Size(t, 24, -1, 1);
                _stockTexts[r] = t;
            }
        }

        void BuildLeftPanel()
        {
            var panel = UIFactory.Panel("Left Panel", _root, Palette.Panel);
            UIFactory.Place(panel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, -SidePanelTop), new Vector2(LeftWidth, 0));
            UIFactory.VStack(panel, 6, 12);
            UIFactory.FitHeight(panel);

            SectionTitle(panel.transform, "Couriers");
            _botList = UIFactory.Rect("Bot List", panel.transform);
            UIFactory.VStack(_botList, 4);

            _buyBotButton = UIFactory.Button("Buy Bot", panel.transform, "", Palette.Button, Palette.Ink, 14, _game.BuyBot, out _buyBotText);
            UIFactory.Size(_buyBotButton, 42);

            SectionTitle(panel.transform, "Build");
            foreach (var type in Catalogue.Buildable)
            {
                var t = type;
                var cost = ResourceAmount.Describe(t.BuildCost);
                var btn = UIFactory.Button(t.Id, panel.transform, $"{t.DisplayName}\n<size=12>{cost}</size>", Palette.Button, Palette.Ink, 15,
                    () => _game.BeginPlacement(t), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(22, label.rectTransform.offsetMin.y);
                UIFactory.Size(btn, 42);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.For(t), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(8, 24));
                _buildButtons.Add((t, btn));
            }

            SectionTitle(panel.transform, "Town");
            _expandButton = UIFactory.Button("Expand", panel.transform, "", Palette.Button, Palette.Ink, 14, _game.Expand, out _expandText);
            UIFactory.Size(_expandButton, 42);

            var help = UIFactory.Label("Help", panel.transform,
                "Space start/pause - F speed - Tab next courier\nRight-drag or WASD pan - Scroll zoom - Esc cancel", 12, Palette.InkSoft);
            help.lineSpacing = 1.1f;
        }

        static void SectionTitle(Transform parent, string text)
        {
            var t = UIFactory.Label("Title " + text, parent, text.ToUpperInvariant(), 13, Palette.InkSoft, TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Size(t, 24);
        }

        void BuildRightPanel()
        {
            var holder = UIFactory.Rect("Schedule Editor", _root);
            holder.anchorMin = new Vector2(1, 0);
            holder.anchorMax = new Vector2(1, 1);
            holder.pivot = new Vector2(1, 1);
            holder.offsetMin = new Vector2(-12 - RightWidth, 12);
            holder.offsetMax = new Vector2(-12, -SidePanelTop);
            holder.gameObject.AddComponent<ScheduleEditor>().Build(_game, _root);
        }

        void BuildInfoAndToast()
        {
            _infoPanel = UIFactory.Panel("Info", _root, Palette.Panel);
            UIFactory.Place(_infoPanel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-50, 12), new Vector2(560, 0));
            UIFactory.VStack(_infoPanel, 0, 12);
            UIFactory.FitHeight(_infoPanel);
            _infoPanel.raycastTarget = false; // otherwise hovering under it flickers and blocks map clicks
            _infoText = UIFactory.Label("Text", _infoPanel.transform, "", 15, Palette.Ink);
            _infoPanel.gameObject.SetActive(false);

            var toast = UIFactory.Panel("Toast", _root, Palette.Ink);
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-50, -SidePanelTop), new Vector2(620, 44));
            toast.raycastTarget = false;
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            _toastText = UIFactory.Label("Text", toast.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_toastText.rectTransform, 12, 12, 4, 4);
        }

        void RebuildBotList()
        {
            UIFactory.DestroyChildren(_botList);
            _botButtons.Clear();
            foreach (var bot in Town.Bots)
            {
                var b = bot;
                var btn = UIFactory.Button(b.Name, _botList, "", Palette.Button, Palette.Ink, 15, () => _game.SelectBot(b), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(24, label.rectTransform.offsetMin.y);
                UIFactory.Size(btn, 38);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.Bot(b.Id), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(10, 20));
                _botButtons.Add((b, (Image)btn.targetGraphic, label));
            }
            _dirty = true;
        }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (_toastGroup.alpha > 0f)
            {
                float remaining = _toastUntil - Time.unscaledTime;
                _toastGroup.alpha = Mathf.Clamp01(remaining / 0.5f);
            }

            if (_dirty)
            {
                _dirty = false;
                RefreshTopBar();
                RefreshLeftPanel();
            }

            if (Time.unscaledTime >= _nextInfoRefresh)
            {
                _nextInfoRefresh = Time.unscaledTime + 0.1f;
                RefreshInfo();
            }
        }

        void RefreshTopBar()
        {
            string phase;
            if (Town.Phase == DayPhase.Planning) phase = "planning";
            else if (_game.Paused) phase = "paused";
            else phase = "running";
            _dayText.text = $"Day {Town.Day}  <size=14><color={UIFactory.Hex(Palette.InkSoft)}>{phase}</color></size>";

            _dayFill.anchorMax = new Vector2(Town.Phase == DayPhase.Running ? Town.DayProgress01 : 0f, 1f);

            if (Town.Phase == DayPhase.Planning) _primaryText.text = "Start Day";
            else _primaryText.text = _game.Paused ? "Resume" : "Pause";
            _speedText.text = $"Speed {_game.Speed:0}x";
            _autoText.text = _game.AutoRunDays ? "Auto-run: on" : "Auto-run: off";

            foreach (var kv in _stockTexts) kv.Value.text = Town.Stockpile.Get(kv.Key).ToString();
        }

        void RefreshLeftPanel()
        {
            foreach (var (bot, bg, text) in _botButtons)
            {
                string cargo = bot.IsCarrying ? $"{bot.CargoCount} {bot.Cargo}" : "empty";
                text.text = $"{bot.Name}  <size=12><color={UIFactory.Hex(Palette.InkSoft)}>{cargo}</color></size>";
                bg.color = bot == _game.SelectedBot ? Palette.Hex("#D6E3FA") : Palette.Button;
            }

            var botCost = Town.NextBotCost;
            _buyBotText.text = $"+ Buy courier\n<size=12>{ResourceAmount.Describe(botCost)}</size>";
            _buyBotButton.interactable = Town.CanAfford(botCost);

            foreach (var (type, button) in _buildButtons)
            {
                button.interactable = Town.CanAfford(type.BuildCost);
                ((Image)button.targetGraphic).color = _game.PlacingType == type ? Palette.Hex("#D6E3FA") : Palette.Button;
            }

            if (Town.Grid.CanExpand)
            {
                var cost = Town.NextExpansionCost;
                _expandText.text = $"Expand town\n<size=12>{ResourceAmount.Describe(cost)}</size>";
                _expandButton.interactable = Town.CanAfford(cost);
            }
            else
            {
                _expandText.text = "Town fully expanded";
                _expandButton.interactable = false;
            }
        }

        void RefreshInfo()
        {
            string text = null;

            if (_game.PlacingType != null)
            {
                var t = _game.PlacingType;
                text = $"<b>Placing {t.DisplayName}</b> - click an empty lot beside a road. Right-click or Esc to cancel.";
                if (_game.HasHover)
                {
                    var check = Town.CheckPlacement(t, _game.HoveredCell);
                    if (check != PlacementCheck.Ok)
                        text += $"\n<color={UIFactory.Hex(Palette.Error)}>{GameController.Explain(check)}</color>";
                }
            }
            else
            {
                var b = _game.HoveredBuilding ?? _game.SelectedBuilding;
                if (b != null) text = Describe(b);
                else if (_game.HasHover)
                {
                    var bot = _game.BotAt(_game.HoveredCell);
                    if (bot != null) text = Describe(bot);
                }
            }

            _infoPanel.gameObject.SetActive(text != null);
            if (text != null && _infoText.text != text) _infoText.text = text;
        }

        string Describe(Building b)
        {
            var t = b.Type;
            var sb = new StringBuilder();
            if (t.IsTownHall)
            {
                sb.Append("<b>Town Hall</b> - the stockpile. Couriers deliver goods here to fund new buildings, couriers and land.\n");
                sb.Append($"Delivered today: {Town.DeliveredToday}");
                return sb.ToString();
            }

            sb.Append($"<b>{t.DisplayName}</b>  <color={UIFactory.Hex(Palette.For(b.Status))}>{StatusText(b.Status)}</color>");
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
            sb.Append($"\nMade today: {b.ProducedToday}");
            return sb.ToString();
        }

        static string Describe(CourierBot bot)
        {
            string cargo = bot.IsCarrying ? $"carrying {bot.CargoCount} {bot.Cargo}" : "empty";
            string last = bot.LastExecutedIndex >= 0
                ? $"line {bot.LastExecutedIndex + 1}: {CommandInfo.Describe(bot.LastResult)}"
                : "waiting at the depot";
            return $"<b>{bot.Name}</b> - {cargo} - {last}\nDelivered to town hall today: {bot.DeliveredToday}";
        }

        static string StatusText(BuildingStatus s)
        {
            switch (s)
            {
                case BuildingStatus.Working: return "working";
                case BuildingStatus.NeedsInput: return "needs input";
                case BuildingStatus.OutputFull: return "output full - send a courier";
                default: return "idle";
            }
        }

        void ShowToast(string message)
        {
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastUntil = Time.unscaledTime + 3.5f;
        }
    }
}
