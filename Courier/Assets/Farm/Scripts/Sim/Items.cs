using System;
using System.Collections.Generic;
using System.Text;

namespace Farm.Sim
{
    /// <summary>Everything a bot can carry. Add an item here, then give it a weight/name below and a colour in Palette.</summary>
    public enum Item
    {
        None = 0,
        Water,
        ParsnipSeeds,
        PotatoSeeds,
        CauliflowerSeeds,
        Parsnip,
        Potato,
        Cauliflower,
    }

    public static class ItemInfo
    {
        /// <summary>Carry weight per unit. Bots have a weight limit (see Balance.BotCapacity).</summary>
        public static int Weight(Item i)
        {
            switch (i)
            {
                case Item.Water: return 1;
                case Item.Parsnip: return 1;
                case Item.Potato: return 2;
                case Item.Cauliflower: return 3;
                default: return 0; // seeds weigh next to nothing
            }
        }

        public static string Name(Item i, int count = 1)
        {
            switch (i)
            {
                case Item.Water: return "water";
                case Item.ParsnipSeeds: return "parsnip seeds";
                case Item.PotatoSeeds: return "potato seeds";
                case Item.CauliflowerSeeds: return "cauliflower seeds";
                case Item.Parsnip: return count == 1 ? "parsnip" : "parsnips";
                case Item.Potato: return count == 1 ? "potato" : "potatoes";
                case Item.Cauliflower: return count == 1 ? "cauliflower" : "cauliflowers";
                default: return "nothing";
            }
        }

        public static bool IsSeed(Item i) => Crops.ForSeed(i) != null;
        public static bool IsProduce(Item i) => Crops.ForProduce(i) != null;

        public static string Describe(Item i, int count) => $"{count} {Name(i, count)}";
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

    /// <summary>A bag of item counts with a total weight. Used for bot loads, the seed shed and the shipping bin.</summary>
    public sealed class Inventory
    {
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
            foreach (Item i in Enum.GetValues(typeof(Item)))
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
                foreach (var a in ToList())
                {
                    int score = a.Amount * Math.Max(1, ItemInfo.Weight(a.Item));
                    if (score > bestScore)
                    {
                        best = a.Item;
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

        public int SeedPrice { get; }
        public int SellPrice { get; }

        public CropType(string name, Item seed, Item produce, int daysToGrow, int seedPrice, int sellPrice)
        {
            Name = name;
            Seed = seed;
            Produce = produce;
            DaysToGrow = daysToGrow;
            SeedPrice = seedPrice;
            SellPrice = sellPrice;
        }

        public override string ToString() => Name;
    }

    public static class Crops
    {
        public static readonly CropType Parsnip = new CropType("Parsnip", Item.ParsnipSeeds, Item.Parsnip, daysToGrow: 3, seedPrice: 10, sellPrice: 35);
        public static readonly CropType Potato = new CropType("Potato", Item.PotatoSeeds, Item.Potato, daysToGrow: 5, seedPrice: 25, sellPrice: 80);
        public static readonly CropType Cauliflower = new CropType("Cauliflower", Item.CauliflowerSeeds, Item.Cauliflower, daysToGrow: 8, seedPrice: 40, sellPrice: 175);

        public static readonly IReadOnlyList<CropType> All = new[] { Parsnip, Potato, Cauliflower };

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

        public static int SellPrice(Item produce) => ForProduce(produce)?.SellPrice ?? 0;
    }
}
