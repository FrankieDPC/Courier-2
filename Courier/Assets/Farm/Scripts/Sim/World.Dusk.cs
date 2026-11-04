using System;

namespace Farm.Sim
{
    // Dusk: when the working day ends every bot stops its routine, walks home by the shortest route
    // (one tile per tick), empties its load and lines up at its start position. The day ends once
    // everyone is home. Emptying: produce goes in the shipping bin (sold overnight), unused seeds go
    // back to the shed, water is poured out.
    public sealed partial class World
    {
        /// <summary>Work is over and bots are heading home.</summary>
        public event Action DuskStarted;

        /// <summary>A bot emptied its load at home: (bot, produce shipped, seeds returned).</summary>
        public event Action<Bot, Inventory> LoadEmptied;

        void BeginDusk()
        {
            Phase = DayPhase.Dusk;
            foreach (var b in _bots)
            {
                b.ResetProgram(); // no routine line is running at dusk (also cancels unfinished tasks)
                b.PreviousPosition = b.Position;
                b.PreviousFacing = b.Facing;
                if (b.Position == b.Home) SettleAtHome(b);
            }
            DuskStarted?.Invoke();
        }

        void DuskStep()
        {
            var dist = DistanceTo(GoTarget.Home, default);
            foreach (var b in _bots)
            {
                b.PreviousPosition = b.Position;
                b.PreviousFacing = b.Facing;
                if (b.Position == b.Home) continue;

                if (Pathing.NextStep(Map, dist, b.Position, out var next, out var dir))
                {
                    b.Facing = dir;
                    b.Position = next;
                }
                else
                {
                    b.Position = b.Home; // stranded: whisked home
                }

                if (b.Position == b.Home) SettleAtHome(b);
            }
        }

        void SettleAtHome(Bot b)
        {
            if (b.IsCarrying)
            {
                var emptied = b.Load.Clone();
                foreach (var a in emptied.ToList())
                {
                    if (ItemInfo.IsProduce(a.Item))
                    {
                        if (Balance.ShipProduceAtDusk)
                        {
                            Bin.Add(a.Item, a.Amount);
                            b.ShippedToday += a.Amount;
                        }
                    }
                    else if (ItemInfo.IsSeed(a.Item))
                    {
                        SeedStock.Add(a.Item, a.Amount);
                    }
                }
                b.Load.Clear();
                LoadEmptied?.Invoke(b, emptied);
            }
            b.Facing = b.HomeFacing;
        }

        bool AllHome()
        {
            foreach (var b in _bots)
                if (b.Position != b.Home || b.IsCarrying || b.Facing != b.HomeFacing) return false;
            return true;
        }
    }
}
