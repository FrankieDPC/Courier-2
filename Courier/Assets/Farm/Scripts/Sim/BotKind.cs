using System.Collections.Generic;

namespace Farm.Sim
{
    /// <summary>What a bot is built for. Farmhands do every job; water bots only handle water.</summary>
    public enum BotKind
    {
        Farmhand,

        /// <summary>Gets around, grabs water (or any other fluid) and waters crops. Nothing else.</summary>
        WaterBot,
    }

    /// <summary>Which commands each kind of bot can carry out.</summary>
    public static class BotKinds
    {
        public static string Name(BotKind k) => k == BotKind.WaterBot ? "Water bot" : "Farmhand";

        /// <summary>One line for the UI, e.g. "water jobs only".</summary>
        public static string Duties(BotKind k) => k == BotKind.WaterBot ? "water jobs only" : "every job";

        /// <summary>Getting around, waiting and running custom commands. Every bot can do these.</summary>
        public static bool IsMovement(Command c) =>
            c.Type == CommandType.GoTo || c.Type == CommandType.Step || c.Type == CommandType.Wait || c.Type == CommandType.Run;

        /// <summary>Jobs that handle water (or any future fluid, see <see cref="ItemInfo.IsFluid"/>).</summary>
        public static bool IsFluidJob(Command c) =>
            c.Type == CommandType.Water || (c.Type == CommandType.Grab && ItemInfo.IsFluid(c.Item));

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
            if (k == BotKind.Farmhand) return true;
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
