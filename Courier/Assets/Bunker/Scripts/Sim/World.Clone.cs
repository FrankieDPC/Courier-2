namespace Bunker.Sim
{
    // Deep copy for "what if" simulation (the day preview). Ids are preserved.
    public sealed partial class World
    {
        /// <summary>
        /// An independent copy of the whole surface, mid-day state included. Stepping the copy never
        /// touches this world. Event subscribers are not copied, and neither is your pending input:
        /// in a copy, your robot only finishes the job it's on, then stands still.
        /// </summary>
        public World Clone()
        {
            var w = new World(Map.Clone())
            {
                TicksPerDay = TicksPerDay,
                RationEvery = RationEvery,
                FinalDay = FinalDay,
                MaxStrikes = MaxStrikes,
                RationsServed = RationsServed,
                Strikes = Strikes,
                Day = Day,
                Tick = Tick,
                CapacityLevel = CapacityLevel,
                BotsBuilt = BotsBuilt,
                Outcome = Outcome,
                _tilesClearedToday = _tilesClearedToday,
            };

            w.Library.CopyFrom(Library);
            w.Stores.CopyFrom(Stores);
            w._deliveredToday.CopyFrom(_deliveredToday);

            foreach (var b in _bots)
            {
                var copy = new Bot(b.Id, b.Name, b.Home, b.HomeFacing, b.Capacity, b.Kind);
                copy.CopyStateFrom(b, w.Library);
                w._bots.Add(copy);
                if (b == Player) w.Player = copy;
            }
            w._nextBotId = _nextBotId;
            return w;
        }
    }
}
