using System.Collections.Generic;

namespace Bunker.Sim
{
    /// <summary>All the tuning knobs in one place. Crop stats live in <see cref="Crops"/>.</summary>
    public static class Balance
    {
        /// <summary>Length of a day, 6:00 am to 2:00 am. At 0.25s/tick and 1x speed, 3600 ticks = 15 minutes.</summary>
        public const int TicksPerDay = 3600;

        /// <summary>The clock: the day starts at 6:00 am and night falls at 2:00 am (minutes after midnight).</summary>
        public const int DayStartMinutes = 6 * 60;
        public const int DayEndMinutes = 26 * 60;

        // Rations: every few nights the bunker eats from the stores.
        public const int RationEvery = 3;

        /// <summary>Feed the bunker through this day and the doors open.</summary>
        public const int FinalDay = 30;

        /// <summary>Missed rations before the bunker seals its doors.</summary>
        public const int MaxStrikes = 3;

        static readonly int[] s_Rations = { 4, 6, 9, 12, 16, 20, 25, 30, 36, 42 };

        /// <summary>Food needed on the Nth ration night (0-based). Radish = 1 food, potato = 3, squash = 6.</summary>
        public static int Ration(int index) =>
            index < s_Rations.Length ? s_Rations[index] : s_Rations[s_Rations.Length - 1] + 6 * (index - s_Rations.Length + 1);

        // What the bunker starts with.
        public const int StartingRadishSeeds = 6;
        public const int StartingRadishes = 4;

        // Job durations in ticks (walking is one tick per tile; a tick is a quarter second at 1x).
        public const int TillTicks = 4;
        public const int PlantTicks = 2;
        public const int WaterTicks = 2;
        public const int HarvestTicks = 3;
        public const int GrabTicks = 2;
        public const int DeliverTicks = 2;

        /// <summary>How long clearing takes, by what's in the way.</summary>
        public static int ClearTicks(Debris d)
        {
            switch (d)
            {
                case Debris.Weeds: return 4;
                case Debris.Thicket: return 10;
                case Debris.Sapling: return 10;
                case Debris.Tree: return 24;
                case Debris.Rubble: return 14;
                case Debris.Wreck: return 20;
                case Debris.Terminal: return 16;
                case Debris.SeedCache: return 8;
                default: return 1;
            }
        }

        /// <summary>
        /// What clearing gives you. Some weeds and thickets hide wild seeds (a fixed pattern, so the
        /// preview is exact): every other weed has a radish seed, every third thicket a potato seed.
        /// </summary>
        public static List<ItemAmount> Yield(Debris d, Cell at)
        {
            var list = new List<ItemAmount>();
            int pattern = at.X + at.Y;
            switch (d)
            {
                case Debris.Weeds:
                    list.Add(new ItemAmount(Item.Fibre, 1));
                    if (pattern % 2 == 0) list.Add(new ItemAmount(Item.RadishSeeds, 1));
                    break;
                case Debris.Thicket:
                    list.Add(new ItemAmount(Item.Fibre, 2));
                    if (pattern % 3 == 0) list.Add(new ItemAmount(Item.PotatoSeeds, 1));
                    break;
                case Debris.Sapling: list.Add(new ItemAmount(Item.Wood, 2)); break;
                case Debris.Tree: list.Add(new ItemAmount(Item.Wood, 3)); break;
                case Debris.Rubble: list.Add(new ItemAmount(Item.Scrap, 2)); break;
                case Debris.Wreck:
                    list.Add(new ItemAmount(Item.Scrap, 3));
                    list.Add(new ItemAmount(Item.Circuit, 1));
                    break;
                case Debris.Terminal:
                    list.Add(new ItemAmount(Item.Scrap, 1));
                    list.Add(new ItemAmount(Item.Circuit, 2));
                    break;
                case Debris.SeedCache:
                    list.Add(new ItemAmount(Item.SquashSeeds, 4));
                    list.Add(new ItemAmount(Item.Scrap, 1));
                    break;
            }
            return list;
        }

        public static int YieldWeight(Debris d, Cell at)
        {
            int w = 0;
            foreach (var a in Yield(d, at)) w += a.Amount * ItemInfo.Weight(a.Item);
            return w;
        }

        /// <summary>Carry weight for you and every bot. Water, radishes and fibre weigh 1; wood and scrap 2.</summary>
        public const int BotCapacity = 10;
        public const int CapacityUpgrade = 5;

        /// <summary>Most a single Grab can ask for. You still only take what your weight limit allows.</summary>
        public const int MaxGrab = 99;

        /// <summary>Seeds your "use" key takes from the hatch (of each kind in stock, if you haven't picked one).</summary>
        public const int SeedsPerGrab = 9;

        // Workshop recipes (paid from the stores).
        public const int WaterBotScrap = 6;
        public const int WaterBotCircuits = 1;
        public const int WaterBotFibre = 2;
        public const int FarmhandScrap = 8;
        public const int FarmhandCircuits = 2;
        public const int FarmhandWood = 2;
        public const int FarmhandFibre = 4;

        /// <summary>Each bot you've built makes the next one cost this much more scrap.</summary>
        public const int ExtraScrapPerBot = 2;

        /// <summary>Bigger tanks cost this much scrap and wood times the next level.</summary>
        public const int TankScrap = 3;
        public const int TankWood = 3;

        /// <summary>Seeds you get from saving one stored crop.</summary>
        public const int SeedsSaved = 2;

        // Editor limits.
        /// <summary>Longest routine or custom command (routines loop, so this is per pass).</summary>
        public const int MaxRoutineLength = 300;

        /// <summary>How many custom commands you can keep.</summary>
        public const int MaxCustomCommands = 32;

        static readonly string[] s_WaterBotNames = { "Drizzle", "Puddle", "Dewdrop", "Splash", "Misty", "Brook" };
        static readonly string[] s_FarmhandNames = { "Sprocket", "Bolt", "Cog", "Rivet", "Gizmo", "Widget", "Spanner", "Tinker" };

        /// <summary>Name for the <paramref name="index"/>th bot of a kind (0-based).</summary>
        public static string BotName(BotKind kind, int index)
        {
            var names = kind == BotKind.WaterBot ? s_WaterBotNames : s_FarmhandNames;
            return index < names.Length ? names[index] : names[index % names.Length] + " " + (index / names.Length + 1);
        }
    }

    /// <summary>The surface outside the bunker. Edit the ASCII layout to redesign it (legend in <see cref="SurfaceMap.Parse"/>).</summary>
    public static class StarterSurface
    {
        public static readonly string[] Layout =
        {
            "TTTTTTTTTTTTTTTTTTTTTTTT~~TTTT",
            "T\"\"\",\"\"i,\"iBBBBBBBB\",r,,~~,T,T",
            "T\"\",T,Xi,riBBBBBBBB\"r,i,~~,T\"T",
            "T,iV,,\",\"T,BBBHBBBB,\",\",~~,iiT",
            "Tiii,\"\"iiiX========,,,,,~~,C\"T",
            "T\",\"\"\",\",,r========C\"i,,~~,i,T",
            "TT\"ii\"r,,,,,......r,,,,,~~,\",T",
            "T,\",,,,,,,,,......,,,,,~~,\"r\"T",
            "T,,i,,,ii,,\"......,\",i,~~,\"\"\"T",
            "Tii,TT,CC,\",,,,,,,,,\",,~~,,TiT",
            "T\"\"iT\"\"\"\"\",,,\",\"\",,\"CC,~~,XiiT",
            "T\",T\"\"\"i,i\",,,,,,,,,\"\",~~,,\",T",
            "T\"\"Ti,,,ii,iTC,\",,T\"\"i,~~,,,,T",
            "T,,iX\"TTT,,,,,\"ii,\"\",\"T,~~,C\"T",
            "T,,T\",\",CTi\",,,\"\"\",,CTT,~~,\"\"T",
            "T\"rXr,,,\",,,,,,irr,\"\",i,==,,\"T",
            "TTrT,r\"TT,,,,\"\",,\"\"\"Ti\",~~,VTT",
            "T\"rrr\"iT\"i,iiTi,\"i,\",,,,~~,T,T",
            "T\"\",,,i\"\"\"\",ii,\"\"Ti,T,\",~~,iiT",
            "TTTTTTTTTTTTTTTTTTTTTTTT~~TTTT",
        };

        public const string PlayerName = "Pip";
        public const string TutorialCommandName = "Water 3 East";

        public static World Create()
        {
            var world = new World(SurfaceMap.Parse(Layout));
            world.Stores.Add(Item.RadishSeeds, Balance.StartingRadishSeeds);
            world.Stores.Add(Item.Radish, Balance.StartingRadishes);
            world.AddPlayer(PlayerName);

            // Ready for your first bot: waters three crops in a row, starting just west of them facing east.
            var row = world.Library.Create(TutorialCommandName);
            row.Routine.SetAll(new[]
            {
                Command.Water, Command.Step(Facing.East),
                Command.Water, Command.Step(Facing.East),
                Command.Water,
            });
            return world;
        }
    }
}
