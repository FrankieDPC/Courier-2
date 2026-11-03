using System;
using System.Collections.Generic;
using System.Text;

namespace Courier.Sim
{
    /// <summary>
    /// Everything a courier can carry. Add new goods here, then give them a colour in
    /// Courier.Game.Palette and use them in Catalogue.
    /// </summary>
    public enum Resource
    {
        None = 0,
        Logs,
        Planks,
        Grain,
        Bread,
        Stone,
    }

    public readonly struct ResourceAmount
    {
        public readonly Resource Resource;
        public readonly int Amount;

        public ResourceAmount(Resource resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }

        public override string ToString() => $"{Amount} {Resource}";

        public static string Describe(IReadOnlyList<ResourceAmount> amounts)
        {
            if (amounts == null || amounts.Count == 0) return "Free";
            var sb = new StringBuilder();
            for (int i = 0; i < amounts.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(amounts[i].ToString());
            }
            return sb.ToString();
        }
    }

    /// <summary>A bag of resource counts. Used for building buffers and the town stockpile.</summary>
    public sealed class Inventory
    {
        readonly Dictionary<Resource, int> _counts = new Dictionary<Resource, int>();

        public event Action Changed;

        public int Get(Resource r) => _counts.TryGetValue(r, out var n) ? n : 0;

        public int Total
        {
            get
            {
                int t = 0;
                foreach (var kv in _counts) t += kv.Value;
                return t;
            }
        }

        public bool IsEmpty => Total == 0;

        public void Add(Resource r, int amount)
        {
            if (r == Resource.None || amount <= 0) return;
            _counts[r] = Get(r) + amount;
            Changed?.Invoke();
        }

        /// <summary>Removes up to <paramref name="max"/> of a resource and returns how many were removed.</summary>
        public int Remove(Resource r, int max)
        {
            int have = Get(r);
            int take = Math.Min(have, max);
            if (take <= 0) return 0;
            _counts[r] = have - take;
            Changed?.Invoke();
            return take;
        }

        public bool Has(IReadOnlyList<ResourceAmount> amounts)
        {
            for (int i = 0; i < amounts.Count; i++)
                if (Get(amounts[i].Resource) < amounts[i].Amount) return false;
            return true;
        }

        /// <summary>Removes all amounts if (and only if) every one is available.</summary>
        public bool TryRemove(IReadOnlyList<ResourceAmount> amounts)
        {
            if (!Has(amounts)) return false;
            for (int i = 0; i < amounts.Count; i++)
                _counts[amounts[i].Resource] = Get(amounts[i].Resource) - amounts[i].Amount;
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            _counts.Clear();
            Changed?.Invoke();
        }

        /// <summary>Non-zero entries in enum order (stable for UI).</summary>
        public List<ResourceAmount> ToList()
        {
            var list = new List<ResourceAmount>();
            foreach (Resource r in Enum.GetValues(typeof(Resource)))
            {
                int n = Get(r);
                if (n > 0) list.Add(new ResourceAmount(r, n));
            }
            return list;
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
            return list.Count == 0 ? "nothing" : ResourceAmount.Describe(list);
        }
    }
}
