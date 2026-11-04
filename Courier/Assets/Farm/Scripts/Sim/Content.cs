namespace Farm.Sim
{
    /// <summary>All the tuning knobs in one place. Crop stats live in <see cref="Crops"/>.</summary>
    public static class Balance
    {
        /// <summary>Length of a working day. At 0.2s/tick and 1x speed, 150 ticks = 30 seconds.</summary>
        public const int TicksPerDay = 150;

        public const int SeasonDays = 14;

        /// <summary>Gold to earn from sales by the end of the season.</summary>
        public const int SeasonGoal = 2000;

        public const int StartingGold = 120;
        public const int StartingParsnipSeeds = 12;

        // Task durations in ticks (walking is one tick per tile).
        public const int TillTicks = 3;
        public const int PlantTicks = 1;
        public const int WaterTicks = 1;
        public const int HarvestTicks = 2;
        public const int GrabTicks = 1;
        public const int ShipTicks = 1;

        /// <summary>Carry weight per bot. Water and parsnips weigh 1, potatoes 2, cauliflowers 3, seeds 0.</summary>
        public const int BotCapacity = 10;
        public const int CapacityUpgrade = 5;
        public const int MaxGrab = 20;

        /// <summary>At dusk, produce a bot is still carrying goes in the shipping bin (true) or is lost (false).</summary>
        public static readonly bool ShipProduceAtDusk = true;

        public static int BotCost(int owned) => 150 + 100 * (owned - 1);
        public static int CapacityCost(int level) => 120 + 120 * level;

        static readonly string[] s_BotNames = { "Sprocket", "Bolt", "Cog", "Rivet", "Gizmo", "Widget", "Spanner", "Tinker" };

        public static string BotName(int id) => id <= s_BotNames.Length ? s_BotNames[id - 1] : "Bot " + id;
    }

    /// <summary>The starting farm. Edit the ASCII layout to redesign it (legend in <see cref="FarmMap.Parse"/>).</summary>
    public static class StarterFarm
    {
        public static readonly string[] Layout =
        {
            "TTTTTTTTTTTTTTTTTTTTTTTT",
            "TgggggHHHHHggggggggg~~~T",
            "TggggSHHHHHgBgggggggg~~T",
            "TgggggHHHHHggggggggggggT",
            "Tgg=====D======gggggggRT",
            "Tgg#####=########ggggggT",
            "Tgg#............#ggggggT",
            "Tgg#.ttt........#gWWgggT",
            "Tgg#.ttt........=gWWgggT",
            "Tgg#.ppp........#ggggggT",
            "Tgg#.ppp........#ggRgggT",
            "Tgg#.ppp........#ggggggT",
            "Tgg#............#ggggTgT",
            "Tgg##############ggggggT",
            "TggggggggggggggggggggggT",
            "TTTTTTTTTTTTTTTTTTTTTTTT",
        };

        /// <summary>Top-left of the pre-planted 3x3 parsnip patch.</summary>
        public static readonly Cell ParsnipPatch = new Cell(5, 6);

        public const string TutorialCommandName = "Water 3 East";

        public static World Create()
        {
            var world = new World(FarmMap.Parse(Layout));
            world.AddGold(Balance.StartingGold);
            world.SeedStock.Add(Item.ParsnipSeeds, Balance.StartingParsnipSeeds);

            // A custom command that waters three tiles in a row, heading east.
            var row = world.Library.Create(TutorialCommandName);
            row.Routine.SetAll(new[]
            {
                Command.Water, Command.Step(Facing.East),
                Command.Water, Command.Step(Facing.East),
                Command.Water,
            });

            // Sprocket waters the starter parsnips once a day.
            var bot = world.AddBot();
            bot.Routine.SetAll(new[]
            {
                Command.GoTo(GoTarget.Well),
                Command.Grab(Item.Water, 9),
                Command.GoToTile(ParsnipPatch.X, ParsnipPatch.Y),
                Command.Run(row),
                Command.Step(Facing.South), Command.Step(Facing.West), Command.Step(Facing.West),
                Command.Run(row),
                Command.Step(Facing.South), Command.Step(Facing.West), Command.Step(Facing.West),
                Command.Run(row),
            });
            return world;
        }
    }
}
