using System.Collections.Generic;

namespace Wizard.Sim
{
    /// <summary>
    /// Every building in the game. Prototype-friendly: tweak numbers here and press Play.
    /// (Swap to ScriptableObjects once the content settles.)
    /// </summary>
    public static class Catalogue
    {
        static ResourceAmount A(Resource r, int n) => new ResourceAmount(r, n);

        public static readonly BuildingType Tower = new BuildingType(
            "tower", "Wizard's Tower", BuildingRole.Tower, playerCanBuild: false);

        public static readonly BuildingType Cottage = new BuildingType(
            "cottage", "Cottage", BuildingRole.Cottage, playerCanBuild: false);

        public static readonly BuildingType Farm = new BuildingType(
            "farm", "Farm",
            outputs: new[] { A(Resource.Grain, 1) }, cycleTicks: 6,
            buildCost: new[] { A(Resource.Gold, 3) });

        public static readonly BuildingType Bakery = new BuildingType(
            "bakery", "Bakery",
            inputs: new[] { A(Resource.Grain, 1) }, outputs: new[] { A(Resource.Bread, 1) }, cycleTicks: 5,
            buildCost: new[] { A(Resource.Gold, 4), A(Resource.Stone, 3) });

        public static readonly BuildingType Quarry = new BuildingType(
            "quarry", "Quarry",
            outputs: new[] { A(Resource.Stone, 1) }, cycleTicks: 5,
            buildCost: new[] { A(Resource.Gold, 4) });

        public static readonly BuildingType HerbGarden = new BuildingType(
            "herb_garden", "Herb Garden",
            outputs: new[] { A(Resource.Herbs, 1) }, cycleTicks: 6,
            buildCost: new[] { A(Resource.Gold, 4), A(Resource.Stone, 2) });

        public static readonly BuildingType Alchemist = new BuildingType(
            "alchemist", "Alchemist",
            inputs: new[] { A(Resource.Herbs, 1) }, outputs: new[] { A(Resource.Potion, 1) }, cycleTicks: 8,
            buildCost: new[] { A(Resource.Gold, 6), A(Resource.Stone, 4) });

        public static readonly BuildingType CrystalMine = new BuildingType(
            "crystal_mine", "Crystal Mine",
            outputs: new[] { A(Resource.Crystal, 1) }, cycleTicks: 6,
            buildCost: new[] { A(Resource.Gold, 6), A(Resource.Stone, 3) });

        public static readonly BuildingType ArcaneForge = new BuildingType(
            "arcane_forge", "Arcane Forge",
            inputs: new[] { A(Resource.Crystal, 1) }, outputs: new[] { A(Resource.Charge, 2) }, cycleTicks: 5,
            outputCapacity: 10,
            buildCost: new[] { A(Resource.Gold, 8), A(Resource.Stone, 4) });

        public static readonly BuildingType Ward = new BuildingType(
            "ward", "Ward", BuildingRole.Ward,
            inputCapacity: 10,
            buildCost: new[] { A(Resource.Gold, 6), A(Resource.Stone, 4) },
            defence: new DefenceStats(Resource.Charge, range: 2, damage: 2, cooldown: 2));

        public static readonly IReadOnlyList<BuildingType> All = new[]
        {
            Tower, Cottage, Farm, Bakery, Quarry, HerbGarden, Alchemist, CrystalMine, ArcaneForge, Ward,
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

    public static class Monsters
    {
        public static readonly MonsterType Shade = new MonsterType("Shade", hp: 3, damage: 1, ticksPerMove: 2);
        public static readonly MonsterType Brute = new MonsterType("Brute", hp: 10, damage: 4, ticksPerMove: 3);
    }

    /// <summary>Tuning knobs that aren't tied to a single building.</summary>
    public static class Balance
    {
        /// <summary>Length of a preparation day. At 0.25s/tick and 1x speed, 96 ticks = 24 seconds.</summary>
        public const int TicksPerDay = 96;
        public const int DoomDay = 10;

        /// <summary>Safety cap: survive this long on the doom day and you win.</summary>
        public const int SiegeMaxTicks = 400;

        public const int ConstructCapacity = 3;

        public const int TowerBaseHp = 20;
        public const int FortifyHp = 5;
        public const int PotionHeal = 4;
        public const int PotionCooldown = 4;

        /// <summary>The wizard fires from the tower using Charges in the stockpile.</summary>
        public static readonly DefenceStats TowerDefence = new DefenceStats(Resource.Charge, range: 3, damage: 2, cooldown: 2);

        public const int MaxOpenJobs = 3;
        public const int JobsPerDay = 2;
        public const int JobMinAmount = 2;
        public const int JobMaxAmount = 4;
        public const int JobBaseReward = 2;

        public readonly struct JobTemplate
        {
            public readonly JobKind Kind;
            public readonly Resource Resource;
            public readonly int Value; // gold per unit

            public JobTemplate(JobKind kind, Resource resource, int value)
            {
                Kind = kind;
                Resource = resource;
                Value = value;
            }
        }

        /// <summary>What villagers ask for. Deliver jobs only appear once something in town makes the good.</summary>
        public static readonly JobTemplate[] JobTemplates =
        {
            new JobTemplate(JobKind.Deliver, Resource.Bread, 3),
            new JobTemplate(JobKind.Deliver, Resource.Grain, 2),
            new JobTemplate(JobKind.Deliver, Resource.Stone, 2),
            new JobTemplate(JobKind.Deliver, Resource.Potion, 6),
            new JobTemplate(JobKind.Collect, Resource.Parcel, 2),
        };

        public static IReadOnlyList<ResourceAmount> ConstructCost(int owned) => owned <= 1
            ? new[] { new ResourceAmount(Resource.Gold, 6) }
            : new[] { new ResourceAmount(Resource.Gold, 4 + owned * 3), new ResourceAmount(Resource.Stone, 2 * (owned - 1)) };

        public static IReadOnlyList<ResourceAmount> ExpansionCost(int level) => new[]
        {
            new ResourceAmount(Resource.Gold, 10 + 8 * level),
            new ResourceAmount(Resource.Stone, 6 + 4 * level),
        };

        public static IReadOnlyList<ResourceAmount> FortifyCost(int level) => new[]
        {
            new ResourceAmount(Resource.Gold, 4 + 2 * level),
            new ResourceAmount(Resource.Stone, 5 + 3 * level),
        };

        static readonly string[] s_ConstructNames =
        {
            "Bramble", "Cobble", "Wick", "Pip", "Moss", "Tumble", "Fennel", "Clink",
        };

        public static string ConstructName(int id) =>
            id <= s_ConstructNames.Length ? s_ConstructNames[id - 1] : "Construct " + id;
    }

    /// <summary>Builds the opening village: a 2x2 block patch in the middle of an 8x8 block map.</summary>
    public static class StarterWorld
    {
        // BlockSize 2 => roads every 3 cells. Unlocked blocks 3..4 cover cells 9..15.
        public static readonly Cell TowerCell = new Cell(11, 11);
        public static readonly Cell DepotCell = new Cell(12, 11);
        public static readonly Cell BakeryCell = new Cell(13, 11);
        public static readonly Cell FarmCell = new Cell(13, 13);
        public static readonly Cell MabelCell = new Cell(11, 13);
        public static readonly Cell TomCell = new Cell(10, 10);
        public static readonly Cell QuarryCell = new Cell(14, 14);

        public const string TutorialSpellName = "Two Steps";

        public static DoomPlan CreateDoom() => new DoomPlan(
            Balance.DoomDay,
            new[] { Facing.North, Facing.East },
            new[]
            {
                new Wave(8, Facing.North, Monsters.Shade, 4),
                new Wave(24, Facing.East, Monsters.Shade, 5),
                new Wave(44, Facing.North, Monsters.Shade, 5),
                new Wave(46, Facing.East, Monsters.Brute, 2, 4),
                new Wave(70, Facing.East, Monsters.Shade, 6),
                new Wave(72, Facing.North, Monsters.Brute, 3, 4),
            });

        public static World Create(int seed = 1234)
        {
            var grid = new TownGrid(blocksX: 8, blocksY: 8, blockSize: 2, minBlockX: 3, minBlockY: 3, maxBlockX: 4, maxBlockY: 4);
            var world = new World(grid, CreateDoom(), seed)
            {
                Depot = DepotCell,
                DepotFacing = Facing.North,
            };

            world.AddBuilding(Catalogue.Tower, TowerCell);
            world.AddBuilding(Catalogue.Bakery, BakeryCell);
            world.AddBuilding(Catalogue.Farm, FarmCell);
            world.AddBuilding(Catalogue.Quarry, QuarryCell);
            var mabel = world.AddBuilding(Catalogue.Cottage, MabelCell, "Mabel");
            var tom = world.AddBuilding(Catalogue.Cottage, TomCell, "Tom");

            world.Stockpile.Add(Resource.Gold, 6);

            world.PostJob(JobKind.Deliver, mabel, Resource.Bread, 3, dueDay: 2, goldReward: 10);
            world.PostJob(JobKind.Collect, tom, Resource.Parcel, 2, dueDay: 3, goldReward: 6);

            // A working first routine that shows off a spell: grain -> bakery, bread -> Mabel.
            var twoSteps = world.Spellbook.Create(TutorialSpellName);
            twoSteps.Routine.SetAll(new[] { Command.Move, Command.Move });

            var bramble = world.AddConstruct();
            bramble.Routine.SetAll(new[]
            {
                Command.Cast(twoSteps),               // to (12,13): Mabel on the left, the farm on the right
                Command.DropOff(InteractSide.Left),   // bread -> Mabel
                Command.PickUp(InteractSide.Right),   // grain <- farm
                Command.UTurn,
                Command.Cast(twoSteps),               // back to the depot, facing south
                Command.DropOff(InteractSide.Left),   // grain -> bakery
                Command.PickUp(InteractSide.Left),    // bread <- bakery
                Command.UTurn,
            });
            return world;
        }
    }
}
