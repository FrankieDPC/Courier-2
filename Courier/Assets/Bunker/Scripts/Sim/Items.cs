using System;
using System.Collections.Generic;
using System.Text;

namespace Bunker.Sim
{
    public enum Item
    {
        None,
        Water,

        RadishSeeds,
        PotatoSeeds,
        SquashSeeds,

        Radish,
        Potato,
        Squash,

        // Building materials, from clearing overgrowth and stripping old tech.
        Fibre,
        Wood,
        Scrap,
        Circuit,
    }

    public static class ItemInfo
    {
        /// <summary>Carry weight per unit. Bots and you have a weight limit (see Balance.BotCapacity).</summary>
        public static int Weight(Item i)
        {
            switch (i)
            {
                case Item.Water: return 1;
                case Item.Radish:
                case Item.Potato:
                case Item.Squash: return Crops.ForProduce(i).Weight;
                case Item.Fibre: return 1;
                case Item.Wood: return 2;
                case Item.Scrap: return 2;
                case Item.Circuit: return 1;
                default: return 0; // seeds: they go in a pouch of their own and never take up room
            }
        }

        public static string Name(Item i, int count = 1)
        {
            switch (i)
            {
                case Item.Water: return "water";
                case Item.RadishSeeds: return "radish seeds";
                case Item.PotatoSeeds: return "potato seeds";
                case Item.SquashSeeds: return "squash seeds";
                case Item.Radish: return count == 1 ? "radish" : "radishes";
                case Item.Potato: return count == 1 ? "potato" : "potatoes";
                case Item.Squash: return count == 1 ? "squash" : "squashes";
                case Item.Fibre: return "fibre";
                case Item.Wood: return "wood";
                case Item.Scrap: return "scrap";
                case Item.Circuit: return count == 1 ? "circuit" : "circuits";
                default: return "nothing";
            }
        }

        public static bool IsSeed(Item i) => Crops.ForSeed(i) != null;

        /// <summary>Liquids: what water bots can grab. Add new fluids (e.g. liquid fertiliser) here.</summary>
        public static bool IsFluid(Item i) => i == Item.Water;

        /// <summary>Harvested crops: delivered to the hatch, they feed the bunker.</summary>
        public static bool IsFood(Item i) => Crops.ForProduce(i) != null;

        public static bool IsMaterial(Item i) => i == Item.Fibre || i == Item.Wood || i == Item.Scrap || i == Item.Circuit;

        /// <summary>How much of a ration one of these covers.</summary>
        public static int FoodValue(Item i) => Crops.ForProduce(i)?.Food ?? 0;

        public static string Describe(Item i, int count) => $"{count} {Name(i, count)}";

        public static readonly Item[] Materials = { Item.Fibre, Item.Wood, Item.Scrap, Item.Circuit };
    }

    public readonly struct ItemAmount
    {
        public readonly Item Item;
        public readonly int Amount;

        public ItemAmount(Item item, int amount)
        {
            Item = item;
            Amount = amount;
        }

        public override string ToString() => ItemInfo.Describe(Item, Amount);
    }

    /// <summary>A bag of item counts with a total weight. Used for loads and the bunker's stores.</summary>
    public sealed class Inventory
    {
        static readonly Item[] s_AllItems = (Item[])Enum.GetValues(typeof(Item));

        readonly Dictionary<Item, int> _counts = new Dictionary<Item, int>();

        public event Action Changed;

        public int Get(Item i) => _counts.TryGetValue(i, out var n) ? n : 0;

        public int Count
        {
            get
            {
                int t = 0;
                foreach (var kv in _counts) t += kv.Value;
                return t;
            }
        }

        public int Weight
        {
            get
            {
                int w = 0;
                foreach (var kv in _counts) w += kv.Value * ItemInfo.Weight(kv.Key);
                return w;
            }
        }

        /// <summary>Total ration value of the food in here.</summary>
        public int Food
        {
            get
            {
                int f = 0;
                foreach (var kv in _counts) f += kv.Value * ItemInfo.FoodValue(kv.Key);
                return f;
            }
        }

        public bool IsEmpty => Count == 0;

        public void Add(Item i, int amount)
        {
            if (i == Item.None || amount <= 0) return;
            _counts[i] = Get(i) + amount;
            Changed?.Invoke();
        }

        public int Remove(Item i, int max)
        {
            int have = Get(i);
            int take = Math.Min(have, max);
            if (take <= 0) return 0;
            _counts[i] = have - take;
            Changed?.Invoke();
            return take;
        }

        public void Clear()
        {
            _counts.Clear();
            Changed?.Invoke();
        }

        /// <summary>Non-zero entries in enum order (stable for UI).</summary>
        public List<ItemAmount> ToList()
        {
            var list = new List<ItemAmount>();
            foreach (var i in s_AllItems)
            {
                int n = Get(i);
                if (n > 0) list.Add(new ItemAmount(i, n));
            }
            return list;
        }

        /// <summary>
        /// What most of the bag is (by weight; by count for weightless crops). Seeds don't count: they ride
        /// in a pouch of their own and take up no room. None if there's nothing but seeds.
        /// </summary>
        public Item Main
        {
            get
            {
                Item best = Item.None;
                int bestScore = 0;
                foreach (var kv in _counts)
                {
                    if (kv.Value <= 0 || ItemInfo.IsSeed(kv.Key)) continue;
                    int score = kv.Value * Math.Max(1, ItemInfo.Weight(kv.Key));
                    if (score > bestScore || (score == bestScore && kv.Key < best)) // ties: earlier item, so it's deterministic
                    {
                        best = kv.Key;
                        bestScore = score;
                    }
                }
                return best;
            }
        }

        internal void CopyFrom(Inventory other)
        {
            _counts.Clear();
            foreach (var kv in other._counts) _counts[kv.Key] = kv.Value;
            Changed?.Invoke();
        }

        public Inventory Clone()
        {
            var copy = new Inventory();
            foreach (var kv in _counts) copy._counts[kv.Key] = kv.Value;
            return copy;
        }

        public override string ToString() => Describe(_ => true);

        /// <summary>Seeds carried: they weigh nothing and ride in their own pouch, not the bag.</summary>
        public bool HasSeeds
        {
            get
            {
                foreach (var kv in _counts)
                    if (kv.Value > 0 && ItemInfo.IsSeed(kv.Key)) return true;
                return false;
            }
        }

        /// <summary>Anything in the bag proper (everything but seeds).</summary>
        public bool HasBagItems
        {
            get
            {
                foreach (var kv in _counts)
                    if (kv.Value > 0 && !ItemInfo.IsSeed(kv.Key)) return true;
                return false;
            }
        }

        /// <summary>The bag's contents, seeds left out: "5 water, 2 scrap" (or "nothing").</summary>
        public string BagText() => Describe(i => !ItemInfo.IsSeed(i));

        /// <summary>The seed pouch: "9 radish, 2 potato" (or "none").</summary>
        public string SeedText()
        {
            var sb = new StringBuilder();
            foreach (var a in ToList())
            {
                var crop = Crops.ForSeed(a.Item);
                if (crop == null) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(a.Amount).Append(' ').Append(crop.Name.ToLowerInvariant());
            }
            return sb.Length == 0 ? "none" : sb.ToString();
        }

        string Describe(Func<Item, bool> include)
        {
            var sb = new StringBuilder();
            foreach (var a in ToList())
            {
                if (!include(a.Item)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(a);
            }
            return sb.Length == 0 ? "nothing" : sb.ToString();
        }
    }

    /// <summary>
    /// A kind of crop. Instances live in <see cref="Crops"/>. Its stats can be tuned (the game sets them
    /// from the BunkerData asset); name, seed and produce are fixed.
    /// </summary>
    public sealed class CropType
    {
        public string Name { get; }
        public Item Seed { get; }
        public Item Produce { get; }

        /// <summary>In-game hours of being watered it needs before it can be harvested.</summary>
        public int GrowHours { get; private set; }

        /// <summary>Watered ticks it needs before it can be harvested.</summary>
        public int GrowTicks => GrowHours * Balance.TicksPerHour;

        /// <summary>How much of a ration one harvested crop covers.</summary>
        public int Food { get; private set; }

        /// <summary>Carry weight of one harvested crop.</summary>
        public int Weight { get; private set; }

        /// <summary>Seeds you get from saving one stored crop in the workshop.</summary>
        public int SeedsSaved { get; private set; }

        readonly int _hours, _food, _weight, _seeds;

        public CropType(string name, Item seed, Item produce, int growHours, int food, int weight, int seedsSaved)
        {
            Name = name;
            Seed = seed;
            Produce = produce;
            _hours = growHours;
            _food = food;
            _weight = weight;
            _seeds = seedsSaved;
            ResetDefaults();
        }

        public int DefaultGrowHours => _hours;
        public int DefaultFood => _food;
        public int DefaultWeight => _weight;
        public int DefaultSeedsSaved => _seeds;

        public void Configure(int growHours, int food, int weight, int seedsSaved)
        {
            GrowHours = Math.Max(1, growHours);
            Food = Math.Max(0, food);
            Weight = Math.Max(0, weight);
            SeedsSaved = Math.Max(1, seedsSaved);
        }

        public void ResetDefaults() => Configure(_hours, _food, _weight, _seeds);

        public override string ToString() => Name;
    }

    /// <summary>Crops that survived a thousand years in the wild. Wild seeds turn up when you clear overgrowth.</summary>
    public static class Crops
    {
        // Grow times are in watered in-game hours (a day is 20 hours, 6 am to 2 am).
        public static readonly CropType Radish = new CropType("Radish", Item.RadishSeeds, Item.Radish, growHours: 12, food: 1, weight: 1, seedsSaved: 2);
        public static readonly CropType Potato = new CropType("Potato", Item.PotatoSeeds, Item.Potato, growHours: 30, food: 3, weight: 2, seedsSaved: 2);
        public static readonly CropType Squash = new CropType("Squash", Item.SquashSeeds, Item.Squash, growHours: 60, food: 6, weight: 3, seedsSaved: 2);

        public const int DefaultWaterLastsHours = 3;

        /// <summary>How long a watering keeps the soil wet, in in-game hours (tunable in the BunkerData asset).</summary>
        public static int WaterLastsHours { get; private set; } = DefaultWaterLastsHours;

        public static int WaterLastsTicks => WaterLastsHours * Balance.TicksPerHour;

        public static void ConfigureWater(int hours) => WaterLastsHours = Math.Max(1, hours);

        public static readonly IReadOnlyList<CropType> All = new[] { Radish, Potato, Squash };

        public static CropType ForSeed(Item seed)
        {
            foreach (var c in All)
                if (c.Seed == seed) return c;
            return null;
        }

        public static CropType ForProduce(Item produce)
        {
            foreach (var c in All)
                if (c.Produce == produce) return c;
            return null;
        }

        public static void ResetDefaults()
        {
            foreach (var c in All) c.ResetDefaults();
            WaterLastsHours = DefaultWaterLastsHours;
        }
    }
}
