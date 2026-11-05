using System;
using System.Collections.Generic;
using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>
    /// Tunable bot and crop stats. Lives at Assets/Bunker/Resources/BunkerData.asset (made for you, or via
    /// Bunker > Edit Game Data). Applied when the game starts, and again whenever you edit it while
    /// playing, so you can tune live. New assets start from the built-in numbers in the sim.
    /// </summary>
    [CreateAssetMenu(menuName = "Bunker/Game Data", fileName = "BunkerData")]
    public sealed class BunkerData : ScriptableObject
    {
        public const string ResourcePath = "BunkerData";

        [Serializable]
        public sealed class BotEntry
        {
            [Tooltip("Ticks to walk one tile. A tick is 0.25s at 1x, so 4 = one tile a second.")]
            [Min(1)] public int ticksPerTile = 4;

            [Tooltip("Carry weight before any Bigger tanks upgrades. Water, radishes, fibre and circuits weigh 1; wood and scrap 2.")]
            [Min(1)] public int capacity = 10;

            [Header("Workshop price")]
            [Tooltip("Each bot already built adds a little more scrap on top of this.")]
            [Min(0)] public int scrap;
            [Min(0)] public int circuits;
            [Min(0)] public int wood;
            [Min(0)] public int fibre;

            public static BotEntry From(BotStats s)
            {
                var b = new BotEntry { ticksPerTile = s.TicksPerTile, capacity = s.Capacity };
                foreach (var a in s.Cost)
                {
                    switch (a.Item)
                    {
                        case Item.Scrap: b.scrap = a.Amount; break;
                        case Item.Circuit: b.circuits = a.Amount; break;
                        case Item.Wood: b.wood = a.Amount; break;
                        case Item.Fibre: b.fibre = a.Amount; break;
                    }
                }
                return b;
            }

            public BotStats ToStats() => new BotStats
            {
                TicksPerTile = ticksPerTile,
                Capacity = capacity,
                Cost = new List<ItemAmount>
                {
                    new ItemAmount(Item.Scrap, scrap), new ItemAmount(Item.Circuit, circuits),
                    new ItemAmount(Item.Wood, wood), new ItemAmount(Item.Fibre, fibre),
                },
            };
        }

        [Serializable]
        public sealed class CropEntry
        {
            // No initializer on purpose: 0 means "not set" (an asset from before crops grew by the hour).
            [Tooltip("In-game hours of wet soil before it can be harvested. A day is 20 hours (6 am to 2 am); crops don't grow while their soil is dry.")]
            [Min(1)] public int growHours;

            [Tooltip("How much of a ration one harvested crop covers.")]
            [Min(0)] public int food = 1;

            [Tooltip("Carry weight of one harvested crop.")]
            [Min(0)] public int weight = 1;

            [Tooltip("Seeds you get from saving one stored crop in the workshop.")]
            [Min(1)] public int seedsSaved = 2;

            public static CropEntry From(CropType c) => new CropEntry
            {
                growHours = c.DefaultGrowHours,
                food = c.DefaultFood,
                weight = c.DefaultWeight,
                seedsSaved = c.DefaultSeedsSaved,
            };

            public void ApplyTo(CropType c) => c.Configure(growHours > 0 ? growHours : c.DefaultGrowHours, food, weight, seedsSaved);
        }

        [Header("You")]
        [Tooltip("Your walking speed in tiles per second (real time: game speed doesn't change it). Bots at 4 ticks per tile walk 1 tile a second.")]
        [Range(0.5f, 15f)] public float playerTilesPerSecond = 5.5f;

        [Tooltip("Your carry weight before any Bigger tanks upgrades.")]
        [Min(1)] public int playerCapacity = BotKinds.DefaultStats(BotKind.Player).Capacity;

        [Header("Bots")]
        public BotEntry waterBot = BotEntry.From(BotKinds.DefaultStats(BotKind.WaterBot));
        public BotEntry farmhand = BotEntry.From(BotKinds.DefaultStats(BotKind.Farmhand));

        [Header("Crops")]
        public CropEntry radish = CropEntry.From(Crops.Radish);
        public CropEntry potato = CropEntry.From(Crops.Potato);
        public CropEntry squash = CropEntry.From(Crops.Squash);

        [Header("Soil")]
        [Tooltip("How long one watering keeps the soil wet, in in-game hours (an hour is 45 seconds at 1x). Crops only grow while it's wet, and watering again works once half of it has dried.")]
        [Range(1, 20)] public int waterLastsHours = Crops.DefaultWaterLastsHours;

        // Bumped when built-in numbers change, so older assets pick up the new ones (see Upgrade).
        const int CurrentDataVersion = 2;
        [SerializeField, HideInInspector] int dataVersion; // missing from old assets: 0

        /// <summary>True if <see cref="Upgrade"/> changed this asset as it loaded (the editor saves it).</summary>
        [NonSerialized] public bool UpgradedOnLoad;

        /// <summary>Raised when an asset is edited while playing. The game re-applies the one it's using.</summary>
        public static event Action<BunkerData> Edited;

        /// <summary>Pushes these numbers into the sim's bot and crop tables.</summary>
        public void Apply()
        {
            BotKinds.Configure(BotKind.WaterBot, waterBot.ToStats());
            BotKinds.Configure(BotKind.Farmhand, farmhand.ToStats());
            var you = BotKinds.DefaultStats(BotKind.Player);
            you.Capacity = playerCapacity;
            BotKinds.Configure(BotKind.Player, you);
            radish.ApplyTo(Crops.Radish);
            potato.ApplyTo(Crops.Potato);
            squash.ApplyTo(Crops.Squash);
            Crops.ConfigureWater(waterLastsHours);
        }

        void OnEnable()
        {
            if (Upgrade()) UpgradedOnLoad = true;
        }

        /// <summary>
        /// Brings an asset made by an older version up to date: crops now grow by the hour (old assets
        /// had days), and the farmhand got cheaper when it became the first bot. Values you changed
        /// yourself are kept. True if anything changed.
        /// </summary>
        public bool Upgrade()
        {
            bool changed = false;
            changed |= FillGrowHours(radish, Crops.Radish);
            changed |= FillGrowHours(potato, Crops.Potato);
            changed |= FillGrowHours(squash, Crops.Squash);
            if (dataVersion < 2)
            {
                if (farmhand != null && farmhand.scrap == 8) farmhand.scrap = BotEntry.From(BotKinds.DefaultStats(BotKind.Farmhand)).scrap;
                changed = true;
            }
            dataVersion = CurrentDataVersion;
            return changed;
        }

        static bool FillGrowHours(CropEntry e, CropType c)
        {
            if (e == null || e.growHours > 0) return false;
            e.growHours = c.DefaultGrowHours;
            return true;
        }

        // Only flag the change here: Unity calls this mid-inspector-edit, where building objects isn't
        // allowed. The GameController applies it on its next frame.
        void OnValidate()
        {
            if (Application.isPlaying) Edited?.Invoke(this);
        }

        /// <summary>The asset in Resources, or a fresh one with the built-in numbers if there isn't one.</summary>
        public static BunkerData LoadOrDefault()
        {
            var data = Resources.Load<BunkerData>(ResourcePath);
            return data != null ? data : CreateInstance<BunkerData>();
        }
    }
}
