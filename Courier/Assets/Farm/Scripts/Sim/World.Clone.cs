namespace Farm.Sim
{
    // Deep copy for "what if" simulation (the day preview). Ids are preserved.
    public sealed partial class World
    {
        /// <summary>
        /// An independent copy of the whole farm, mid-day state included. Stepping the copy never
        /// touches this world. Event subscribers are not copied.
        /// </summary>
        public World Clone()
        {
            var w = new World(Map.Clone())
            {
                TicksPerDay = TicksPerDay,
                SeasonDays = SeasonDays,
                SeasonGoal = SeasonGoal,
                Gold = Gold,
                Day = Day,
                Tick = Tick,
                Phase = Phase,
                Earned = Earned,
                CapacityLevel = CapacityLevel,
                BotsBought = BotsBought,
                Outcome = Outcome,
            };

            w.Library.CopyFrom(Library);
            w.SeedStock.CopyFrom(SeedStock);
            w.Bin.CopyFrom(Bin);

            foreach (var b in _bots)
            {
                var copy = new Bot(b.Id, b.Name, b.Home, b.HomeFacing, b.Capacity, b.Kind);
                copy.CopyStateFrom(b, w.Library);
                w._bots.Add(copy);
            }
            w._nextBotId = _nextBotId;
            return w;
        }
    }
}
