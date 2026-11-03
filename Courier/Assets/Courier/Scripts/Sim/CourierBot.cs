namespace Courier.Sim
{
    /// <summary>A programmable delivery bot. Runs one schedule command per tick.</summary>
    public sealed class CourierBot
    {
        public int Id { get; }
        public string Name { get; set; }

        /// <summary>Where the bot starts each day.</summary>
        public Cell Home { get; internal set; }
        public Facing HomeFacing { get; internal set; }

        public Cell Position { get; internal set; }
        public Facing Facing { get; internal set; }

        public Resource Cargo { get; internal set; }
        public int CargoCount { get; internal set; }
        public int Capacity { get; set; }

        public Schedule Schedule { get; } = new Schedule();

        /// <summary>Repeat the schedule until the day ends. If false the bot idles after the last command.</summary>
        public bool Loop { get; set; } = true;

        /// <summary>Index of the next command to run.</summary>
        public int ProgramCounter { get; internal set; }

        /// <summary>Index of the command run on the most recent tick (-1 if none).</summary>
        public int LastExecutedIndex { get; internal set; } = -1;
        public CommandResult LastResult { get; internal set; }

        /// <summary>Position/facing before the most recent tick; handy for interpolating visuals.</summary>
        public Cell PreviousPosition { get; internal set; }
        public Facing PreviousFacing { get; internal set; }

        public int DeliveredToday { get; internal set; }
        public int ProblemsToday { get; internal set; }

        public bool IsCarrying => CargoCount > 0;
        public int FreeCapacity => Capacity - CargoCount;

        internal CourierBot(int id, string name, Cell home, Facing homeFacing, int capacity)
        {
            Id = id;
            Name = name;
            Home = home;
            HomeFacing = homeFacing;
            Capacity = capacity;
            ReturnHome();
        }

        internal void ReturnHome()
        {
            Position = PreviousPosition = Home;
            Facing = PreviousFacing = HomeFacing;
            ProgramCounter = 0;
            LastExecutedIndex = -1;
            LastResult = CommandResult.None;
        }

        internal void ResetDailyStats()
        {
            DeliveredToday = 0;
            ProblemsToday = 0;
        }

        internal void AddCargo(Resource r, int n)
        {
            if (n <= 0) return;
            Cargo = r;
            CargoCount += n;
        }

        internal void RemoveCargo(int n)
        {
            CargoCount -= n;
            if (CargoCount <= 0)
            {
                CargoCount = 0;
                Cargo = Resource.None;
            }
        }

        public override string ToString() => $"{Name} at {Position} facing {Facing}";
    }
}
