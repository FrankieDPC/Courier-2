using System.Collections.Generic;

namespace Courier.Sim
{
    /// <summary>
    /// Every building in the game. Prototype-friendly: tweak numbers here and press Play.
    /// (Swap to ScriptableObjects once the content settles.)
    /// </summary>
    public static class Catalogue
    {
        static ResourceAmount A(Resource r, int n) => new ResourceAmount(r, n);

        public static readonly BuildingType TownHall = new BuildingType(
            "town_hall", "Town Hall",
            isTownHall: true,
            playerCanBuild: false);

        public static readonly BuildingType Woodcutter = new BuildingType(
            "woodcutter", "Woodcutter",
            outputs: new[] { A(Resource.Logs, 1) },
            cycleTicks: 6, outputCapacity: 6,
            buildCost: new[] { A(Resource.Planks, 3) });

        public static readonly BuildingType Sawmill = new BuildingType(
            "sawmill", "Sawmill",
            inputs: new[] { A(Resource.Logs, 1) },
            outputs: new[] { A(Resource.Planks, 1) },
            cycleTicks: 4, inputCapacity: 6, outputCapacity: 6,
            buildCost: new[] { A(Resource.Planks, 6) });

        public static readonly BuildingType Farm = new BuildingType(
            "farm", "Farm",
            outputs: new[] { A(Resource.Grain, 1) },
            cycleTicks: 6, outputCapacity: 6,
            buildCost: new[] { A(Resource.Planks, 3) });

        public static readonly BuildingType Bakery = new BuildingType(
            "bakery", "Bakery",
            inputs: new[] { A(Resource.Grain, 1) },
            outputs: new[] { A(Resource.Bread, 1) },
            cycleTicks: 5, inputCapacity: 6, outputCapacity: 6,
            buildCost: new[] { A(Resource.Planks, 5), A(Resource.Stone, 2) });

        public static readonly BuildingType Quarry = new BuildingType(
            "quarry", "Quarry",
            outputs: new[] { A(Resource.Stone, 1) },
            cycleTicks: 8, outputCapacity: 4,
            buildCost: new[] { A(Resource.Planks, 6) });

        public static readonly IReadOnlyList<BuildingType> All = new[]
        {
            TownHall, Woodcutter, Sawmill, Farm, Bakery, Quarry,
        };

        public static IEnumerable<BuildingType> Buildable
        {
            get
            {
                foreach (var t in All)
                    if (t.PlayerCanBuild) yield return t;
            }
        }
    }

    /// <summary>Tuning knobs that aren't tied to a single building.</summary>
    public static class Balance
    {
        /// <summary>Length of a working day. At 0.25s/tick and 1x speed, 96 ticks = 24 seconds.</summary>
        public const int TicksPerDay = 96;

        public const int BotCapacity = 3;

        public static IReadOnlyList<ResourceAmount> BotCost(int botsOwned)
        {
            if (botsOwned <= 1) return new[] { new ResourceAmount(Resource.Planks, 5) };
            return new[]
            {
                new ResourceAmount(Resource.Planks, 3 + botsOwned * 2),
                new ResourceAmount(Resource.Bread, 2 * (botsOwned - 1)),
            };
        }

        public static IReadOnlyList<ResourceAmount> ExpansionCost(int level) => new[]
        {
            new ResourceAmount(Resource.Planks, 12 + 6 * level),
            new ResourceAmount(Resource.Stone, 4 + 3 * level),
            new ResourceAmount(Resource.Bread, 4 + 3 * level),
        };
    }

    /// <summary>Builds the opening town: a 2x2 block patch in the middle of an 8x8 block map.</summary>
    public static class StarterTown
    {
        // Grid with BlockSize 2 => roads every 3 cells. Unlocked blocks 3..4 cover cells 9..15.
        public static readonly Cell TownHallCell = new Cell(11, 11);
        public static readonly Cell SawmillCell = new Cell(13, 11);
        public static readonly Cell WoodcutterCell = new Cell(13, 13);
        public static readonly Cell FarmCell = new Cell(10, 14);
        public static readonly Cell DepotCell = new Cell(12, 11);

        /// <summary>
        /// A working first route: fetch logs from the woodcutter, feed the sawmill, carry planks
        /// to the town hall, and finish where it started so it can loop.
        /// </summary>
        public static readonly Command[] TutorialSchedule =
        {
            Command.Move,       // (12,12)
            Command.Move,       // (12,13) - woodcutter on the right
            Command.PickUp(InteractSide.Right),
            Command.UTurn,      // face south
            Command.Move,       // (12,12)
            Command.Move,       // (12,11) - sawmill on the left, town hall on the right
            Command.DropOff(InteractSide.Left),  // logs -> sawmill
            Command.PickUp(InteractSide.Left),   // planks <- sawmill
            Command.DropOff(InteractSide.Right), // planks -> town hall
            Command.UTurn,      // face north again, ready to loop
        };

        public static Town Create()
        {
            var grid = new TownGrid(blocksX: 8, blocksY: 8, blockSize: 2, minBlockX: 3, minBlockY: 3, maxBlockX: 4, maxBlockY: 4);
            var town = new Town(grid)
            {
                Depot = DepotCell,
                DepotFacing = Facing.North,
            };

            town.AddBuilding(Catalogue.TownHall, TownHallCell);
            town.AddBuilding(Catalogue.Sawmill, SawmillCell);
            town.AddBuilding(Catalogue.Woodcutter, WoodcutterCell);
            town.AddBuilding(Catalogue.Farm, FarmCell);

            town.Stockpile.Add(Resource.Planks, 4);

            var bot = town.AddBot();
            bot.Schedule.SetAll(TutorialSchedule);
            return town;
        }
    }
}
