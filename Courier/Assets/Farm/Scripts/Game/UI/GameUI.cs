using System.Collections.Generic;
using System.Text;
using Farm.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Farm.Game
{
    /// <summary>
    /// The HUD: top bar (season day, clock, goal, controls, gold & seed stock), left panel (bots, shop,
    /// crop guide), right panel (routine editor), info card for whatever's under the mouse, the preview
    /// card, toasts, and the end-of-season card.
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
        Text _goalText;
        RectTransform _goalFill;
        Text _primaryText;
        Text _speedText;
        Text _autoText;
        Text _goldText;
        readonly Dictionary<Item, Text> _seedTexts = new Dictionary<Item, Text>();

        // left panel
        RectTransform _botList;
        readonly List<(Bot bot, Image bg, Text text)> _botButtons = new List<(Bot, Image, Text)>();
        Button _buyBotButton;
        Text _buyBotText;
        readonly List<(CropType crop, Button button)> _seedButtons = new List<(CropType, Button)>();
        Button _capacityButton;
        Text _capacityText;

        // info, toast, preview, season over
        Image _infoPanel;
        Text _infoText;
        CanvasGroup _toastGroup;
        Text _toastText;
        float _toastUntil;
        PreviewPanel _previewPanel;
        GameObject _seasonOver;
        Text _seasonTitle;
        Text _seasonBody;

        bool _dirty = true;
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
            BuildSeasonOver();

            World.Ticked += MarkDirty;
            World.DayStarted += MarkDirty;
            World.DayEnded += OnDayEnded;
            World.BotAdded += OnBotAdded;
            World.GoldChanged += MarkDirty;
            World.SeedStock.Changed += MarkDirty;
            World.SeasonOver += OnSeasonOver;
            _game.StateChanged += MarkDirty;
            _game.SelectionChanged += MarkDirty;
            _game.Toast += ShowToast;

            RebuildBotList();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            World.Ticked -= MarkDirty;
            World.DayStarted -= MarkDirty;
            World.DayEnded -= OnDayEnded;
            World.BotAdded -= OnBotAdded;
            World.GoldChanged -= MarkDirty;
            World.SeedStock.Changed -= MarkDirty;
            World.SeasonOver -= OnSeasonOver;
            _game.StateChanged -= MarkDirty;
            _game.SelectionChanged -= MarkDirty;
            _game.Toast -= ShowToast;
        }

        void MarkDirty() => _dirty = true;
        void OnDayEnded(DayReport _) => _dirty = true;

        void OnBotAdded(Bot _)
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
            var h = UIFactory.HStack(bar, 10, 10);
            h.padding = new RectOffset(16, 16, 10, 10);

            _dayText = UIFactory.Label("Day", bar.transform, "", 19, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            _dayText.lineSpacing = 0.9f;
            UIFactory.Size(_dayText, -1, 200);

            _dayFill = Meter(bar.transform, "Clock", 140, Palette.Gold, out _);

            var goalBox = UIFactory.Rect("Goal", bar.transform);
            UIFactory.Size(goalBox, -1, 210, 0);
            var goalStack = UIFactory.VStack(goalBox, 2);
            goalStack.childAlignment = TextAnchor.MiddleLeft;
            _goalText = UIFactory.Label("Text", goalBox, "", 14, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_goalText, 18);
            var goalTrack = UIFactory.Panel("Track", goalBox, Palette.PanelSunken, true, 0.4f);
            UIFactory.Size(goalTrack, 12);
            var goalFill = UIFactory.Panel("Fill", goalTrack.transform, Palette.Good, true, 0.4f);
            _goalFill = goalFill.rectTransform;
            UIFactory.Stretch(_goalFill);

            var primary = UIFactory.Button("Start", bar.transform, "Start Day", Palette.Accent, Color.white, 16, _game.PrimaryAction, out _primaryText);
            _primaryText.fontStyle = FontStyle.Bold;
            UIFactory.Size(primary, -1, 140);
            var speed = UIFactory.Button("Speed", bar.transform, "Speed 1x", Palette.Button, Palette.Ink, 15, _game.CycleSpeed, out _speedText);
            UIFactory.Size(speed, -1, 96);
            var auto = UIFactory.Button("Auto", bar.transform, "Auto-run: off", Palette.Button, Palette.Ink, 15, _game.ToggleAutoRun, out _autoText);
            UIFactory.Size(auto, -1, 126);

            var spacer = UIFactory.Rect("Spacer", bar.transform);
            UIFactory.Size(spacer, -1, -1, 1);

            _goldText = Chip(bar.transform, "Gold", Palette.Gold, 110);
            var shedLabel = UIFactory.Label("Shed", bar.transform, "Seed shed:", 14, Palette.InkSoft, TextAnchor.MiddleRight);
            UIFactory.Size(shedLabel, -1, 84, 0);
            foreach (var crop in Crops.All)
                _seedTexts[crop.Seed] = Chip(bar.transform, crop.Name, Palette.For(crop.Seed), 70);
        }

        static Text Chip(Transform parent, string name, Color color, float width)
        {
            var chip = UIFactory.Rect("Chip " + name, parent);
            UIFactory.Size(chip, -1, width, 0).minWidth = 50;
            var ch = UIFactory.HStack(chip, 5);
            ch.childForceExpandHeight = false;
            ch.childAlignment = TextAnchor.MiddleLeft;
            var sw = UIFactory.Panel("Swatch", chip, color, true, 0.4f);
            UIFactory.Size(sw, 14, 14);
            var t = UIFactory.Label("Text", chip, "", 15, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(t, 30, -1, 1);
            return t;
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

            SectionTitle(panel.transform, "Bots");
            _botList = UIFactory.Rect("Bots", panel.transform);
            UIFactory.Grid(_botList, new Vector2(half, 34), new Vector2(6, 4), 2);
            _buyBotButton = UIFactory.Button("Buy Bot", panel.transform, "", Palette.Button, Palette.Ink, 13, _game.BuyBot, out _buyBotText);
            UIFactory.Size(_buyBotButton, 36);

            SectionTitle(panel.transform, "Shop");
            var shop = UIFactory.Rect("Shop", panel.transform);
            UIFactory.Grid(shop, new Vector2(half, 40), new Vector2(6, 4), 2);
            foreach (var crop in Crops.All)
            {
                var c = crop;
                var btn = UIFactory.Button(c.Name, shop, $"5 {c.Name.ToLowerInvariant()} seeds\n<size=11>{c.SeedPrice * 5}g</size>",
                    Palette.Button, Palette.Ink, 13, () => _game.BuySeeds(c, 5), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.lineSpacing = 0.9f;
                label.rectTransform.offsetMin = new Vector2(16, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.For(c.Seed), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(6, 24));
                _seedButtons.Add((c, btn));
            }
            _capacityButton = UIFactory.Button("Capacity", shop, "", Palette.Button, Palette.Ink, 13, _game.UpgradeCapacity, out _capacityText);
            _capacityText.lineSpacing = 0.9f;
            UIFactory.Size(shop, 2 * 40 + 4);

            SectionTitle(panel.transform, "Crops");
            var guide = new StringBuilder();
            foreach (var c in Crops.All)
            {
                if (guide.Length > 0) guide.Append('\n');
                guide.Append($"<b>{c.Name}</b>: {c.DaysToGrow} watered days, sells {c.SellPrice}g, weighs {ItemInfo.Weight(c.Produce)}");
            }
            guide.Append($"\nWater weighs 1 per unit, seeds nothing.");
            var guideText = UIFactory.Label("Guide", panel.transform, guide.ToString(), 12, Palette.Ink);
            guideText.lineSpacing = 1.05f;

            var help = UIFactory.Label("Help", panel.transform,
                "Space start/pause - P preview day - F speed - Tab next bot\nRight-drag/WASD pan - Scroll zoom - Esc cancel", 11, Palette.InkSoft);
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
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-50, -SidePanelTop), new Vector2(660, 44));
            toast.raycastTarget = false;
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            _toastText = UIFactory.Label("Text", toast.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_toastText.rectTransform, 12, 12, 4, 4);
        }

        void BuildSeasonOver()
        {
            var dim = UIFactory.Panel("Season Over", _root, new Color(0.1f, 0.08f, 0.04f, 0.55f), false);
            UIFactory.Stretch(dim.rectTransform);
            _seasonOver = dim.gameObject;

            var card = UIFactory.Panel("Card", dim.transform, Palette.Panel);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 0));
            var stack = UIFactory.VStack(card, 12, 24);
            stack.childAlignment = TextAnchor.UpperCenter;
            UIFactory.FitHeight(card);

            _seasonTitle = UIFactory.Label("Title", card.transform, "", 32, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _seasonBody = UIFactory.Label("Body", card.transform, "", 16, Palette.InkSoft, TextAnchor.UpperCenter);
            var again = UIFactory.Button("Again", card.transform, "Play again", Palette.Accent, Color.white, 18, _game.Restart, out var againText);
            againText.fontStyle = FontStyle.Bold;
            UIFactory.Size(again, 46);
            _seasonOver.SetActive(false);
        }

        void RebuildBotList()
        {
            UIFactory.DestroyChildren(_botList);
            _botButtons.Clear();
            foreach (var bot in World.Bots)
            {
                var b = bot;
                var btn = UIFactory.Button(b.Name, _botList, "", Palette.Button, Palette.Ink, 13, () => _game.SelectBot(b), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 13;
                label.lineSpacing = 0.9f;
                label.rectTransform.offsetMin = new Vector2(18, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.Bot(b.Id), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(8, 20));
                _botButtons.Add((b, (Image)btn.targetGraphic, label));
            }
            int rows = (World.Bots.Count + 1) / 2;
            UIFactory.Size(_botList, Mathf.Max(0, rows * 34 + (rows - 1) * 4));
            _dirty = true;
        }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (_toastGroup.alpha > 0f)
                _toastGroup.alpha = Mathf.Clamp01((_toastUntil - Time.unscaledTime) / 0.5f);

            if (_dirty)
            {
                _dirty = false;
                RefreshTopBar();
                RefreshLeftPanel();
            }

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
            if (World.IsGameOver) sub = "season over";
            else if (World.IsDusk) sub = "dusk: bots head home";
            else if (World.Phase == DayPhase.Running) sub = _game.Paused ? "paused" : "working";
            else sub = World.DaysLeft == 1 ? "last day of the season" : $"{World.DaysLeft} days left";
            _dayText.text = $"Spring {World.Day}/{World.SeasonDays}\n<size=13><color={UIFactory.Hex(Palette.InkSoft)}>{sub}</color></size>";
            _dayFill.anchorMax = new Vector2(World.Phase != DayPhase.Planning ? World.DayProgress01 : 0f, 1f);

            float goal = World.SeasonGoal > 0 ? Mathf.Clamp01(World.Earned / (float)World.SeasonGoal) : 1f;
            _goalText.text = $"Earned {World.Earned} / {World.SeasonGoal}g";
            _goalFill.anchorMax = new Vector2(goal, 1f);

            if (World.IsGameOver) _primaryText.text = "Season over";
            else if (World.Phase == DayPhase.Planning) _primaryText.text = "Start Day";
            else _primaryText.text = _game.Paused ? "Resume" : "Pause";
            _speedText.text = $"Speed {_game.Speed:0}x";
            _autoText.text = _game.AutoRunDays ? "Auto-run: on" : "Auto-run: off";

            _goldText.text = $"{World.Gold}g";
            foreach (var kv in _seedTexts) kv.Value.text = World.SeedStock.Get(kv.Key).ToString();
        }

        void RefreshLeftPanel()
        {
            foreach (var (b, bg, text) in _botButtons)
            {
                string load = b.IsCarrying ? $"{b.Load.Weight}/{b.Capacity} {ShortName(b.Load.Main)}" : "empty";
                string tag = b.Kind == BotKind.Farmhand ? "" : $" <size=10><color={UIFactory.Hex(Palette.For(Item.Water))}>WATER</color></size>";
                text.text = $"{b.Name}{tag}\n<size=11><color={UIFactory.Hex(Palette.InkSoft)}>{load}</color></size>";
                bg.color = b == _game.SelectedBot ? Palette.AccentSoft : Palette.Button;
            }

            bool over = World.IsGameOver;
            _buyBotText.text = $"+ Buy a farmhand  <size=11>{World.NextBotCost}g</size>";
            _buyBotButton.interactable = !over && World.Gold >= World.NextBotCost;

            foreach (var (crop, button) in _seedButtons)
                button.interactable = !over && World.Gold >= crop.SeedPrice * 5;

            _capacityText.text = $"Bigger baskets +{Balance.CapacityUpgrade}\n<size=11>{World.NextCapacityCost}g</size>";
            _capacityButton.interactable = !over && World.Gold >= World.NextCapacityCost;
        }

        void RefreshInfo()
        {
            string text = null;
            if (_game.IsPicking)
            {
                text = "<b>Pick a tile</b> for the Go to command - click anywhere a bot can stand. Esc to cancel.";
            }
            else if (_game.HasHover && _game.BotAt(_game.HoveredCell) is Bot b)
            {
                text = Describe(b);
            }
            else if (_game.HasHover || _game.HasSelectedTile)
            {
                text = DescribeTile(_game.HasHover ? _game.HoveredCell : _game.SelectedTile);
            }

            _infoPanel.gameObject.SetActive(text != null);
            if (text != null && _infoText.text != text) _infoText.text = text;
        }

        string DescribeTile(Cell c)
        {
            var t = World.Map.TileAt(c);
            if (t == null) return null;
            string where = $"<color={UIFactory.Hex(Palette.InkSoft)}>tile {c.X},{c.Y}</color>";

            switch (t.Structure)
            {
                case Structure.Well: return $"<b>Well</b> {where} - stand next to it and Grab water.";
                case Structure.Bin:
                    string bin = World.Bin.IsEmpty ? "empty" : World.Bin.ToString();
                    return $"<b>Shipping bin</b> {where} - stand next to it and Ship. Sold overnight. Today: {bin}.";
                case Structure.Shed: return $"<b>Seed shed</b> {where} - stand next to it and Grab seeds. In stock: {World.SeedStock}.";
                case Structure.House: return $"<b>Farmhouse</b> {where} - the bots live by the door.";
                case Structure.Fence: return $"Fence {where}";
                case Structure.Tree: return $"Tree {where}";
                case Structure.Rock: return $"Rock {where}";
            }

            if (t.Ground == Ground.Water) return $"<b>Pond</b> {where} - stand next to it and Grab water.";
            if (c == World.Map.Home) return $"<b>Front door</b> {where} - where bots start and end each day.";
            if (!t.IsFarmland) return t.Ground == Ground.Path ? $"Path {where}" : $"Grass {where}";

            var sb = new StringBuilder();
            sb.Append(t.Tilled ? "<b>Tilled soil</b> " : "<b>Farmland</b> ").Append(where);
            if (t.HasCrop)
            {
                sb.Append($"\n{t.Crop.Name}: ");
                sb.Append(t.IsRipe ? UIFactory.Colour("ripe - harvest it!", Palette.Good) : $"{t.Growth}/{t.Crop.DaysToGrow} watered days");
            }
            else if (t.Tilled) sb.Append(" - ready to plant.");
            else sb.Append(" - till it before planting.");
            if (t.Tilled) sb.Append(t.Watered ? "  " + UIFactory.Colour("Watered today.", Palette.Water) : "  Dry.");
            return sb.ToString();
        }

        static string ShortName(Item i) =>
            ItemInfo.IsSeed(i) ? "seeds" : i == Item.Water ? "water" : ItemInfo.Name(i, 1);

        static string Describe(Bot b)
        {
            string load = b.IsCarrying ? $"carrying {b.Load} ({b.Load.Weight}/{b.Capacity})" : $"empty (carries up to {b.Capacity})";
            string last = b.LastPath.Count > 0
                ? $"line {b.LastPath[0].Index + 1}: {CommandInfo.Describe(b.LastResult)}"
                : "waiting by the door";
            return $"<b>{b.Name}</b>, {BotKinds.Name(b.Kind).ToLowerInvariant()} ({BotKinds.Duties(b.Kind)}) - {load}\n{last}";
        }

        void OnSeasonOver(Outcome outcome)
        {
            bool met = outcome == Outcome.GoalMet;
            _seasonTitle.text = met ? "What a harvest!" : "Season's over";
            _seasonTitle.color = met ? Palette.Accent : Palette.Ink;
            _seasonBody.text =
                $"Earned {World.Earned}g of the {World.SeasonGoal}g goal" + (met ? "." : " - so close!") + "\n" +
                $"Bots: {World.Bots.Count}   Custom commands: {World.Library.Macros.Count}\n" +
                $"Gold in the bank: {World.Gold}g";
            _seasonOver.SetActive(true);
            _dirty = true;
        }

        void ShowToast(string message)
        {
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastUntil = Time.unscaledTime + 3.5f;
        }
    }
}
