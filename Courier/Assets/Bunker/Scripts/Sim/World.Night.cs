using System;

namespace Bunker.Sim
{
    // Night: at the end of the day everyone (you included) is reset at the hatch in an instant: whatever
    // they carry is handed in (food and materials into the stores, seeds back on the shelf, water poured
    // out), jobs in progress are dropped, and routines start again from line 1 in the morning.
    public sealed partial class World
    {
        /// <summary>A bot emptied its load at the hatch at night: (bot, what it handed in).</summary>
        public event Action<Bot, Inventory> LoadEmptied;

        void ResetAtHome(Bot b)
        {
            if (b.IsCarrying)
            {
                var emptied = b.Load.Clone();
                foreach (var a in emptied.ToList())
                {
                    if (a.Item == Item.Water) continue; // poured out
                    HandIn(b, a.Item, a.Amount);
                }
                b.Load.Clear();
                LoadEmptied?.Invoke(b, emptied);
            }
            b.ReturnHome(); // position, facing, routine back to line 1, any job dropped
        }
    }
}
