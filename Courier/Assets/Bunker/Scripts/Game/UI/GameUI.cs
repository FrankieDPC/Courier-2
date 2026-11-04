using System.Collections.Generic;
using System.Text;
using Bunker.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace Bunker.Game
{
    /// <summary>
    /// The HUD: top bar (day, clock, next ration, strikes, pause/speed, the bunker's stores), left panel
    /// (you, your bots, the workshop, crop guide), a bot's programming interface on the right (only while
    /// it's open), the "E" hint, an info card for whatever's under the mouse, the preview card, the night
    /// fade, toasts, and the end card.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        public const float SidePanelTop = 84f;
        public const float LeftWidth = 270f;
        public const float RightWidth = 364f;

        GameController _game;
        World World => _game.World;
        RectTransform _root;

        // top bar
        Text _dayText;
        RectTransform _dayFill;
        Text _rationText;
        RectTransform _rationFill;
        Image _rationFillImage;
        Text _primaryText;
        Text _speedText;
        readonly Dictionary<Item, Text> _stockTexts = new Dictionary<Item, Text>();
        Text _foodText;

        // left panel
        Text _youText;
        RectTransform _botList;
        Text _noBots;
        readonly List<(Bot bot, Image bg, Text text)> _botButtons = new List<(Bot, Image, Text)>();
        readonly List<(Recipe recipe, Button button, Text text)> _recipeButtons = new List<(Recipe, Button, Text)>();

        RectTransform _editorHolder;
        Image _night;
        Text _nightText;

        // hint, info, toast, preview, end card
        Image _useHint;
        Text _useHintText;
        Image _infoPanel;
        Text _infoText;
        CanvasGroup _toastGroup;
        Text _toastText;
        float _toastUntil;
        PreviewPanel _previewPanel;
        GameObject _endCard;
        Text _endTitle;
        Text _endBody;

        bool _dirty = true;
        float _nextInfoRefresh;
        int _overgrownAtStart;

        public void Init(GameController game)
        {
            _game = game;
            _overgrownAtStart = World.Map.Count(t => t.IsOvergrown);
            BuildCanvas();
            BuildTopBar();
            BuildLeftPanel();
            var editor = BuildRightPanel();
            BuildHintInfoAndToast();
            BuildNight();
            _toastGroup.transform.SetAsLastSibling();
            _previewPanel = UIFactory.Rect("Preview Panel", _root).gameObject.AddComponent<PreviewPanel>();
            _previewPanel.Build(_game, editor);
            UIFactory.Stretch((RectTransform)_previewPanel.transform);
            BuildEndCard();

            World.Ticked += MarkDirty;
            World.DayStarted += MarkDirty;
            World.DayEnded += OnDayEnded;
            World.BotAdded += OnBotAdded;
            World.Stores.Changed += MarkDirty;
            World.GameOver += OnGameOver;
            _game.StateChanged += MarkDirty;
            _game.SelectionChanged += MarkDirty;
            _game.Toast += ShowToast;
            if (_game.Player != null) _game.Player.SeedChoiceChanged += MarkDirty;

            RebuildBotList();
        }

        void OnDestroy()
        {
            if (_game == null) return;
            World.Ticked -= MarkDirty;
            World.DayStarted -= MarkDirty;
            World.DayEnded -= OnDayEnded;
            World.BotAdded -= OnBotAdded;
            World.Stores.Changed -= MarkDirty;
            World.GameOver -= OnGameOver;
            _game.StateChanged -= MarkDirty;
            _game.SelectionChanged -= MarkDirty;
            _game.Toast -= ShowToast;
            if (_game.Player != null) _game.Player.SeedChoiceChanged -= MarkDirty;
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
            UIFactory.Size(_dayText, -1, 250);

            _dayFill = Meter(bar.transform, "Clock", 120, Palette.Hazard, out _);

            var rationBox = UIFactory.Rect("Ration", bar.transform);
            UIFactory.Size(rationBox, -1, 250, 0);
            var rationStack = UIFactory.VStack(rationBox, 2);
            rationStack.childAlignment = TextAnchor.MiddleLeft;
            _rationText = UIFactory.Label("Text", rationBox, "", 14, Palette.Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(_rationText, 18);
            var track = UIFactory.Panel("Track", rationBox, Palette.PanelSunken, true, 0.4f);
            UIFactory.Size(track, 12);
            _rationFillImage = UIFactory.Panel("Fill", track.transform, Palette.Good, true, 0.4f);
            _rationFill = _rationFillImage.rectTransform;
            UIFactory.Stretch(_rationFill);

            var primary = UIFactory.Button("Pause", bar.transform, "Pause", Palette.Accent, Color.white, 16, _game.PrimaryAction, out _primaryText);
            _primaryText.fontStyle = FontStyle.Bold;
            UIFactory.Size(primary, -1, 120);
            var speed = UIFactory.Button("Speed", bar.transform, "Speed 1x", Palette.Button, Palette.Ink, 15, _game.CycleSpeed, out _speedText);
            UIFactory.Size(speed, -1, 92);

            var spacer = UIFactory.Rect("Spacer", bar.transform);
            UIFactory.Size(spacer, -1, -1, 1);

            var storesLabel = UIFactory.Label("Stores", bar.transform, "Stores:", 14, Palette.InkSoft, TextAnchor.MiddleRight);
            UIFactory.Size(storesLabel, -1, 54, 0);
            _foodText = Chip(bar.transform, "Food", Palette.Food, 74);
            foreach (var m in ItemInfo.Materials) _stockTexts[m] = Chip(bar.transform, m.ToString(), Palette.For(m), 64);
            foreach (var crop in Crops.All) _stockTexts[crop.Seed] = Chip(bar.transform, crop.Name, Palette.For(crop.Seed), 56);
        }

        static Text Chip(Transform parent, string name, Color color, float width)
        {
            var chip = UIFactory.Rect("Chip " + name, parent);
            UIFactory.Size(chip, -1, width, 0).minWidth = 44;
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

            SectionTitle(panel.transform, "You");
            _youText = UIFactory.Label("You", panel.transform, "", 13, Palette.Ink);
            _youText.lineSpacing = 1.05f;

            SectionTitle(panel.transform, "Bots");
            _botList = UIFactory.Rect("Bots", panel.transform);
            UIFactory.Grid(_botList, new Vector2(half, 34), new Vector2(6, 4), 2);
            _noBots = UIFactory.Label("No Bots", panel.transform, "None yet. Build one in the workshop, then click it to give it commands.", 12, Palette.InkSoft);
            UIFactory.Size(_noBots, 32);

            SectionTitle(panel.transform, "Workshop");
            foreach (var r in World.AllRecipes)
            {
                var recipe = r;
                var btn = UIFactory.Button(recipe.ToString(), panel.transform, "", Palette.Button, Palette.Ink, 13, () => _game.Build(recipe), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.lineSpacing = 0.9f;
                label.supportRichText = true;
                label.rectTransform.offsetMin = new Vector2(18, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, RecipeColor(recipe), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(6, 24));
                UIFactory.Size(btn, 36);
                _recipeButtons.Add((recipe, btn, label));
            }

            SectionTitle(panel.transform, "Crops");
            var guide = new StringBuilder();
            foreach (var c in Crops.All)
            {
                if (guide.Length > 0) guide.Append('\n');
                guide.Append($"<b>{c.Name}</b>: {c.DaysToGrow} watered days, {c.Food} food, weighs {ItemInfo.Weight(c.Produce)}");
            }
            guide.Append("\nWild seeds hide in weeds and thickets.");
            var guideText = UIFactory.Label("Guide", panel.transform, guide.ToString(), 12, Palette.Ink);
            guideText.lineSpacing = 1.05f;

            var help = UIFactory.Label("Help", panel.transform,
                "WASD move - E use - Q seed - Space pause\nClick a bot (or Tab) to program it - P preview\nF speed - C recentre - Right-drag look - Esc close", 11, Palette.InkSoft);
            help.lineSpacing = 1.05f;
        }

        static Color RecipeColor(Recipe r)
        {
            switch (r)
            {
                case Recipe.WaterBot: return Palette.For(Item.Water);
                case Recipe.Farmhand: return Palette.Bot(3);
                case Recipe.BiggerTanks: return Palette.For(Item.Scrap);
                default: return Palette.For(World.CropFor(r));
            }
        }

        static string RecipeName(Recipe r)
        {
            switch (r)
            {
                case Recipe.WaterBot: return "Build a water bot";
                case Recipe.Farmhand: return "Build a farmhand";
                case Recipe.BiggerTanks: return $"Bigger tanks (+{Balance.CapacityUpgrade} carry)";
                default: return $"Save {World.CropFor(r).Name.ToLowerInvariant()} seeds";
            }
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
            _editorHolder = holder;
            holder.gameObject.SetActive(false); // opens when you click a bot
            return editor;
        }

        /// <summary>The night fade: dark, with the next day's number. Blocks clicks while it shows.</summary>
        void BuildNight()
        {
            _night = UIFactory.Panel("Night", _root, new Color(0.02f, 0.03f, 0.06f, 1f), false);
            UIFactory.Stretch(_night.rectTransform);
            _night.raycastTarget = true; // nothing to click while night falls
            _nightText = UIFactory.Label("Text", _night.transform, "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_nightText.rectTransform, 40, 40, 40, 40);
            _nightText.raycastTarget = false;
            _night.gameObject.SetActive(false);
        }

        void BuildHintInfoAndToast()
        {
            // "[E] Clear weeds" at the bottom of the map while you're out working.
            _useHint = UIFactory.Panel("Use Hint", _root, Palette.Ink);
            UIFactory.Place(_useHint.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-50, 12), new Vector2(420, 34));
            _useHint.raycastTarget = false;
            _useHintText = UIFactory.Label("Text", _useHint.transform, "", 15, Color.white, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_useHintText.rectTransform, 10, 10, 2, 2);
            _useHint.gameObject.SetActive(false);

            _infoPanel = UIFactory.Panel("Info", _root, Palette.Panel);
            UIFactory.Place(_infoPanel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-50, 12), new Vector2(560, 0));
            _infoPanel.raycastTarget = false; // otherwise hovering under it flickers and blocks map clicks
            UIFactory.VStack(_infoPanel, 0, 12);
            UIFactory.FitHeight(_infoPanel);
            _infoText = UIFactory.Label("Text", _infoPanel.transform, "", 15, Palette.Ink);
            _infoPanel.gameObject.SetActive(false);

            var toast = UIFactory.Panel("Toast", _root, Palette.Ink);
            UIFactory.Place(toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-50, -SidePanelTop), new Vector2(720, 52));
            toast.raycastTarget = false;
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.blocksRaycasts = false;
            _toastGroup.alpha = 0f;
            _toastText = UIFactory.Label("Text", toast.transform, "", 16, Color.white, TextAnchor.MiddleCenter);
            _toastText.resizeTextForBestFit = true;
            _toastText.resizeTextMinSize = 11;
            _toastText.resizeTextMaxSize = 16;
            UIFactory.Stretch(_toastText.rectTransform, 12, 12, 4, 4);
        }

        void BuildEndCard()
        {
            var dim = UIFactory.Panel("End", _root, new Color(0.05f, 0.07f, 0.05f, 0.6f), false);
            UIFactory.Stretch(dim.rectTransform);
            _endCard = dim.gameObject;

            var card = UIFactory.Panel("Card", dim.transform, Palette.Panel);
            UIFactory.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(540, 0));
            var stack = UIFactory.VStack(card, 12, 24);
            stack.childAlignment = TextAnchor.UpperCenter;
            UIFactory.FitHeight(card);

            _endTitle = UIFactory.Label("Title", card.transform, "", 32, Palette.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            _endBody = UIFactory.Label("Body", card.transform, "", 16, Palette.InkSoft, TextAnchor.UpperCenter);
            var again = UIFactory.Button("Again", card.transform, "Play again", Palette.Accent, Color.white, 18, _game.Restart, out var againText);
            againText.fontStyle = FontStyle.Bold;
            UIFactory.Size(again, 46);
            _endCard.SetActive(false);
        }

        void RebuildBotList()
        {
            UIFactory.DestroyChildren(_botList);
            _botButtons.Clear();
            int count = 0;
            foreach (var bot in World.Bots)
            {
                if (bot.IsPlayer) continue;
                var b = bot;
                count++;
                var btn = UIFactory.Button(b.Name, _botList, "", Palette.Button, Palette.Ink, 13, () => _game.SelectBot(b), out var label);
                label.alignment = TextAnchor.MiddleLeft;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = 13;
                label.lineSpacing = 0.9f;
                label.rectTransform.offsetMin = new Vector2(18, label.rectTransform.offsetMin.y);
                var swatch = UIFactory.Panel("Swatch", btn.transform, Palette.Bot(b), true, 0.4f);
                UIFactory.Place(swatch.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(8, 20));
                _botButtons.Add((b, (Image)btn.targetGraphic, label));
            }
            int rows = (count + 1) / 2;
            _botList.gameObject.SetActive(count > 0);
            _noBots.gameObject.SetActive(count == 0);
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
                RefreshUseHint();
                if (_editorHolder.gameObject.activeSelf != _game.IsEditorOpen) _editorHolder.gameObject.SetActive(_game.IsEditorOpen);
            }
            RefreshNight();

            float lift = _previewPanel.VisibleHeight;
            float bottom = lift > 0f ? 12 + lift + 8 : 12;
            _useHint.rectTransform.anchoredPosition = new Vector2(-50, bottom);
            if (_useHint.gameObject.activeSelf) bottom += 34 + 8;
            _infoPanel.rectTransform.anchoredPosition = new Vector2(-50, bottom);

            if (Time.unscaledTime >= _nextInfoRefresh)
            {
                _nextInfoRefresh = Time.unscaledTime + 0.1f;
                RefreshInfo();
            }
        }

        void RefreshNight()
        {
            float a = _game.NightFade01;
            bool show = a > 0.001f;
            if (_night.gameObject.activeSelf != show) _night.gameObject.SetActive(show);
            if (!show) return;
            _night.color = new Color(0.02f, 0.03f, 0.06f, 0.92f * a);
            var r = _game.LastNight;
            string ration = "";
            if (r != null && r.RationNight)
                ration = r.RationShort
                    ? $"\n<size=18><color={UIFactory.Hex(Palette.Error)}>The bunker went hungry: strike {r.Strikes} of {World.MaxStrikes}</color></size>"
                    : $"\n<size=18><color={UIFactory.Hex(Palette.Good)}>The bunker ate well</color></size>";
            _nightText.text = $"Night falls...\n<size=40>Day {World.Day}</size>{ration}";
            _nightText.color = new Color(1f, 1f, 1f, a);
        }

        void RefreshTopBar()
        {
            string sub;
            if (World.IsGameOver) sub = World.Outcome == Outcome.DoorsOpen ? "the doors are open" : "the bunker is sealed";
            else if (_game.InNight) sub = "night";
            else if (_game.IsEditorOpen) sub = $"programming {_game.SelectedBot.Name}";
            else if (_game.UserPaused) sub = "paused";
            else sub = World.ClockMinutes >= 24 * 60 ? "late: the day resets at 2 am" : "out working";
            string clock = GameController.FormatClock(World.ClockMinutes);
            _dayText.text = $"Day {World.Day}/{World.FinalDay}  {clock}\n<size=13><color={UIFactory.Hex(Palette.InkSoft)}>{sub}</color></size>";
            _dayFill.anchorMax = new Vector2(World.DayProgress01, 1f);

            int need = World.NextRationNeeded;
            int food = World.Stores.Food;
            string when = World.NextRationDay == World.Day ? "tonight" : $"day {World.NextRationDay}";
            var strikes = new StringBuilder();
            for (int i = 0; i < World.MaxStrikes; i++)
                strikes.Append(UIFactory.Colour(i < World.Strikes ? "X" : "o", i < World.Strikes ? Palette.Error : Palette.InkSoft));
            _rationText.text = $"Ration {when}: {food}/{need} food  {strikes}";
            float fill = need > 0 ? Mathf.Clamp01(food / (float)need) : 1f;
            _rationFill.anchorMax = new Vector2(fill, 1f);
            _rationFillImage.color = fill >= 1f ? Palette.Good : World.NextRationDay == World.Day ? Palette.Error : Palette.Warning;

            if (World.IsGameOver) _primaryText.text = "The end";
            else if (_game.IsEditorOpen) _primaryText.text = "Done";
            else _primaryText.text = _game.Paused ? "Resume" : "Pause";
            _speedText.text = $"Speed {_game.Speed:0}x";

            _foodText.text = food.ToString();
            foreach (var kv in _stockTexts) kv.Value.text = World.Stores.Get(kv.Key).ToString();
        }

        void RefreshLeftPanel()
        {
            var p = World.Player;
            if (p != null)
            {
                string load = p.IsCarrying ? $"carrying {p.Load} ({p.Load.Weight}/{p.Capacity})" : $"empty-handed (carries {p.Capacity})";
                string seed = PlayerController.SeedLabel(_game.Player != null ? _game.Player.SeedChoice : Item.None);
                _youText.text = $"<b>{p.Name}</b>, {load}\n<color={UIFactory.Hex(Palette.InkSoft)}>Planting: {seed} (Q)</color>";
            }

            foreach (var (b, bg, text) in _botButtons)
            {
                string load = b.IsCarrying ? $"{b.Load.Weight}/{b.Capacity} {ShortName(b.Load.Main)}" : "empty";
                string tag = b.Kind == BotKind.WaterBot ? $" <size=10><color={UIFactory.Hex(Palette.For(Item.Water))}>WATER</color></size>" : "";
                text.text = $"{b.Name}{tag}\n<size=11><color={UIFactory.Hex(Palette.InkSoft)}>{load}</color></size>";
                bg.color = b == _game.SelectedBot ? Palette.AccentSoft : Palette.Button;
            }

            foreach (var (recipe, button, text) in _recipeButtons)
            {
                bool can = World.CanAfford(recipe);
                string cost = GameController.CostText(World, recipe);
                string costColor = UIFactory.Hex(can ? Palette.Good : Palette.InkSoft);
                text.text = $"{RecipeName(recipe)}\n<size=11><color={costColor}>{cost}</color></size>";
                button.interactable = can;
            }
        }

        /// <summary>"[E] Clear weeds" for the tile in front of you.</summary>
        void RefreshUseHint()
        {
            var p = World.Player;
            bool show = p != null && !World.IsGameOver && !_game.InNight && !_game.Paused;
            _useHint.gameObject.SetActive(show);
            if (!show) return;
            if (World.PlayerBusy)
            {
                _useHintText.text = "Working...";
                return;
            }
            var seed = _game.Player != null ? _game.Player.SeedChoice : Item.None;
            _useHintText.text = UseText(World, p, seed);
        }

        static string UseText(World w, Bot p, Item seed)
        {
            var ahead = World.Ahead(p);
            var t = w.Map.TileAt(ahead);
            var cmd = w.SuggestUse(p, seed);
            string what;
            switch (cmd.Type)
            {
                case CommandType.Clear:
                    if (t == null || !t.IsOvergrown) return "<b>[E]</b> nothing to do here";
                    var parts = new List<string>();
                    foreach (var a in Balance.Yield(t.Debris, ahead)) parts.Add($"+{a.Amount} {ItemInfo.Name(a.Item, a.Amount)}");
                    what = $"Clear {DebrisName(t.Debris).ToLowerInvariant()}  <size=12>({string.Join(", ", parts)})</size>";
                    break;
                case CommandType.Till: what = "Till soil"; break;
                case CommandType.Plant: what = $"Plant {PlayerController.SeedLabel(seed)}"; break;
                case CommandType.Water: what = t != null && t.Watered ? "Water (already watered)" : $"Water the {t?.Crop?.Name.ToLowerInvariant()}"; break;
                case CommandType.Harvest: what = $"Harvest the {t?.Crop?.Name.ToLowerInvariant()}"; break;
                case CommandType.Deliver: what = "Deliver your load to the bunker"; break;
                case CommandType.Grab:
                    what = cmd.Item == Item.Water ? "Fill up with water"
                        : cmd.Item == Item.None ? "Take seeds" : $"Take {ItemInfo.Name(cmd.Item, 2)}";
                    break;
                default: what = CommandInfo.Name(cmd.Type); break;
            }
            return $"<b>[E]</b> {what}";
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
            else if (_game.HasHover && World.Player != null && World.Player.Position == _game.HoveredCell)
            {
                text = $"<b>{World.Player.Name}</b> - that's you. WASD to move, E to work the tile in front of you.";
            }
            else if (_game.HasHover || _game.HasSelectedTile)
            {
                text = DescribeTile(_game.HasHover ? _game.HoveredCell : _game.SelectedTile);
            }

            _infoPanel.gameObject.SetActive(text != null);
            if (text != null && _infoText.text != text) _infoText.text = text;
        }

        public static string DebrisName(Debris d)
        {
            switch (d)
            {
                case Debris.Weeds: return "Weeds";
                case Debris.Thicket: return "Thicket";
                case Debris.Sapling: return "Sapling";
                case Debris.Tree: return "Old tree";
                case Debris.Rubble: return "Rubble";
                case Debris.Wreck: return "Rusted car";
                case Debris.Terminal: return "Dead terminal";
                case Debris.SeedCache: return "Seed cache";
                default: return "Nothing";
            }
        }

        string DescribeTile(Cell c)
        {
            var t = World.Map.TileAt(c);
            if (t == null) return null;
            string where = $"<color={UIFactory.Hex(Palette.InkSoft)}>tile {c.X},{c.Y}</color>";

            if (t.Structure == Structure.Hatch)
                return $"<b>Bunker hatch</b> {where} - face it to deliver food and salvage, or take seeds. Stores: {World.Stores}.";
            if (t.Structure == Structure.Bunker) return $"<b>The bunker</b> {where} - home. They're counting on you.";
            if (t.IsWater) return $"<b>Stream</b> {where} - face it and Grab water.";

            if (t.IsOvergrown)
            {
                var parts = new List<string>();
                foreach (var a in Balance.Yield(t.Debris, c)) parts.Add(a.ToString());
                return $"<b>{DebrisName(t.Debris)}</b> {where} - clear it ({Balance.ClearTicks(t.Debris)} ticks) for {string.Join(", ", parts)}.";
            }
            if (c == World.Map.Home) return $"<b>Outside the hatch</b> {where} - where you and the bots start and end each day.";
            if (t.Ground == Ground.Concrete) return $"Old concrete {where} - nothing grows here.";

            var sb = new StringBuilder();
            sb.Append(t.Tilled ? "<b>Tilled soil</b> " : "<b>Cleared soil</b> ").Append(where);
            if (t.HasCrop)
            {
                sb.Append($"\n{t.Crop.Name}: ");
                sb.Append(t.IsRipe ? UIFactory.Colour("ripe - harvest it!", Palette.Good) : $"{t.Growth}/{t.Crop.DaysToGrow} watered days");
            }
            else if (t.Tilled) sb.Append(" - ready to plant.");
            else sb.Append(" - till it before planting.");
            if (t.HasCrop) sb.Append(t.Watered ? "  " + UIFactory.Colour("Watered today.", Palette.Water) : "  Dry.");
            return sb.ToString();
        }

        static string ShortName(Item i) =>
            ItemInfo.IsSeed(i) ? "seeds" : i == Item.Water ? "water" : ItemInfo.Name(i, 1);

        static string Describe(Bot b)
        {
            string load = b.IsCarrying ? $"carrying {b.Load} ({b.Load.Weight}/{b.Capacity})" : $"empty (carries up to {b.Capacity})";
            string last = b.LastPath.Count > 0
                ? $"line {b.LastPath[0].Index + 1}: {CommandInfo.Describe(b.LastResult)}"
                : "waiting by the hatch";
            return $"<b>{b.Name}</b>, {BotKinds.Name(b.Kind).ToLowerInvariant()} ({BotKinds.Duties(b.Kind)}) - {load}\n{last}";
        }

        void OnGameOver(Outcome outcome)
        {
            bool won = outcome == Outcome.DoorsOpen;
            int cleared = _overgrownAtStart - World.Map.Count(t => t.IsOvergrown);
            int farmed = World.Map.Count(t => t.Tilled);
            int bots = World.Bots.Count - 1;
            _endTitle.text = won ? "The doors open!" : "The bunker is sealed";
            _endTitle.color = won ? Palette.Accent : Palette.Ink;
            _endBody.text = (won
                    ? $"You kept the bunker fed for {World.FinalDay} days. Blinking, the people step out into the green."
                    : $"The bunker went hungry {World.Strikes} times and sealed its doors on day {World.Day}.") + "\n\n" +
                $"Tiles cleared: {cleared}   Tilled: {farmed}\n" +
                $"Bots built: {bots}   Custom commands: {World.Library.Macros.Count}";
            _endCard.SetActive(true);
            _dirty = true;
        }

        void ShowToast(string message)
        {
            _toastText.text = message;
            _toastGroup.alpha = 1f;
            _toastUntil = Time.unscaledTime + 4f;
        }
    }
}
