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
                case Item.Radish: return 1;
                case Item.Potato: return 2;
                case Item.Squash: return 3;
                case Item.Fibre: return 1;
                case Item.Wood: return 2;
                case Item.Scrap: return 2;
                case Item.Circuit: return 1;
                default: return 0; // seeds weigh next to nothing
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

        /// <summary>The item there's most weight of (or the most of, for weightless items).</summary>
        public Item Main
        {
            get
            {
                Item best = Item.None;
                int bestScore = 0;
                foreach (var kv in _counts)
                {
                    if (kv.Value <= 0) continue;
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

        public override string ToString()
        {
            var list = ToList();
            if (list.Count == 0) return "nothing";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>A kind of crop. Instances live in <see cref="Crops"/>.</summary>
    public sealed class CropType
    {
        public string Name { get; }
        public Item Seed { get; }
        public Item Produce { get; }

        /// <summary>Watered days needed before it can be harvested.</summary>
        public int DaysToGrow { get; }

        /// <summary>How much of a ration one harvested crop covers.</summary>
        public int Food { get; }

        public CropType(string name, Item seed, Item produce, int daysToGrow, int food)
        {
            Name = name;
            Seed = seed;
            Produce = produce;
            DaysToGrow = daysToGrow;
            Food = food;
        }

        public override string ToString() => Name;
    }

    /// <summary>Crops that survived a thousand years in the wild. Wild seeds turn up when you clear overgrowth.</summary>
    public static class Crops
    {
        public static readonly CropType Radish = new CropType("Radish", Item.RadishSeeds, Item.Radish, daysToGrow: 3, food: 1);
        public static readonly CropType Potato = new CropType("Potato", Item.PotatoSeeds, Item.Potato, daysToGrow: 5, food: 3);
        public static readonly CropType Squash = new CropType("Squash", Item.SquashSeeds, Item.Squash, daysToGrow: 8, food: 6);

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
    }
}
