using System.Collections.Generic;

namespace Bunker.Sim
{
    /// <summary>What a bot is built for. Farmhands do every job; water bots only handle water.</summary>
    public enum BotKind
    {
        Farmhand,

        /// <summary>Gets around, grabs water (or any other fluid) and waters crops. Nothing else.</summary>
        WaterBot,

        /// <summary>You: steered directly instead of running a routine. Can do every job.</summary>
        Player,
    }

    /// <summary>Tunable numbers for one kind of bot (set from the BunkerData asset in the game).</summary>
    public sealed class BotStats
    {
        /// <summary>Ticks to walk one tile (4 = one tile a second at 1x). You move freely in the game; this only applies to sim steps.</summary>
        public int TicksPerTile = 1;

        /// <summary>Carry weight before any Bigger tanks upgrades.</summary>
        public int Capacity = Balance.BotCapacity;

        /// <summary>Workshop price (before the extra scrap each bot already built adds).</summary>
        public List<ItemAmount> Cost = new List<ItemAmount>();

        public BotStats Clone() => new BotStats { TicksPerTile = TicksPerTile, Capacity = Capacity, Cost = new List<ItemAmount>(Cost) };
    }

    /// <summary>Which commands each kind of bot can carry out, and each kind's stats.</summary>
    public static class BotKinds
    {
        static readonly Dictionary<BotKind, BotStats> s_Stats = new Dictionary<BotKind, BotStats>();

        static BotKinds() => ResetDefaults();

        /// <summary>The built-in numbers (what a fresh BunkerData asset starts from).</summary>
        public static BotStats DefaultStats(BotKind k)
        {
            switch (k)
            {
                case BotKind.WaterBot:
                    return new BotStats
                    {
                        TicksPerTile = 4,
                        Capacity = Balance.BotCapacity,
                        Cost = new List<ItemAmount>
                        {
                            new ItemAmount(Item.Scrap, 6), new ItemAmount(Item.Circuit, 1), new ItemAmount(Item.Fibre, 2),
                        },
                    };
                case BotKind.Farmhand:
                    return new BotStats
                    {
                        TicksPerTile = 4,
                        Capacity = Balance.BotCapacity,
                        Cost = new List<ItemAmount>
                        {
                            new ItemAmount(Item.Scrap, 6), new ItemAmount(Item.Circuit, 2), new ItemAmount(Item.Wood, 2), new ItemAmount(Item.Fibre, 4),
                        },
                    };
                default: // you
                    return new BotStats { TicksPerTile = 1, Capacity = Balance.BotCapacity };
            }
        }

        public static BotStats Stats(BotKind k) => s_Stats[k];

        /// <summary>Replaces a kind's stats (the game does this from the BunkerData asset).</summary>
        public static void Configure(BotKind k, BotStats stats)
        {
            if (stats == null) return;
            var copy = stats.Clone();
            copy.TicksPerTile = System.Math.Max(1, copy.TicksPerTile);
            copy.Capacity = System.Math.Max(1, copy.Capacity);
            s_Stats[k] = copy;
        }

        public static void ResetDefaults()
        {
            foreach (BotKind k in new[] { BotKind.Farmhand, BotKind.WaterBot, BotKind.Player }) s_Stats[k] = DefaultStats(k);
        }

        public static string Name(BotKind k) => k == BotKind.WaterBot ? "Water bot" : k == BotKind.Player ? "You" : "Farmhand";

        /// <summary>One line for the UI, e.g. "water jobs only".</summary>
        public static string Duties(BotKind k) => k == BotKind.WaterBot ? "water jobs only" : k == BotKind.Player ? "every job, by hand" : "every job";

        /// <summary>Getting around, waiting and running custom commands. Every bot can do these.</summary>
        public static bool IsMovement(Command c) =>
            c.Type == CommandType.GoTo || c.Type == CommandType.Step || c.Type == CommandType.Face ||
            c.Type == CommandType.Wait || c.Type == CommandType.Run;

        /// <summary>Jobs that handle water (or any future fluid, see <see cref="ItemInfo.IsFluid"/>).</summary>
        public static bool IsFluidJob(Command c) =>
            c.Type == CommandType.Water || c.Type == CommandType.Dump || (c.Type == CommandType.Grab && ItemInfo.IsFluid(c.Item));

        public static bool CanDo(BotKind k, Command c)
        {
            switch (k)
            {
                case BotKind.WaterBot: return IsMovement(c) || IsFluidJob(c);
                default: return true;
            }
        }

        /// <summary>
        /// True if every job in the custom command (and in anything it runs) is one this kind of bot can do.
        /// Missing custom commands count as fine here; running them reports their own problem.
        /// </summary>
        public static bool CanRun(BotKind k, CommandLibrary library, int macroId)
        {
            if (k != BotKind.WaterBot) return true;
            return CanRun(k, library, macroId, new HashSet<int>());
        }

        static bool CanRun(BotKind k, CommandLibrary library, int macroId, HashSet<int> seen)
        {
            if (!seen.Add(macroId)) return true;
            var macro = library.Get(macroId);
            if (macro == null) return true;
            foreach (var c in macro.Routine)
            {
                if (c.IsRun)
                {
                    if (!CanRun(k, library, c.MacroId, seen)) return false;
                }
                else if (!CanDo(k, c)) return false;
            }
            return true;
        }

        /// <summary>Whether a line of a routine (a job, or a Run of a custom command) is fully doable.</summary>
        public static bool CanDoLine(BotKind k, CommandLibrary library, Command c) =>
            c.IsRun ? CanRun(k, library, c.MacroId) : CanDo(k, c);
    }
}
