using System.Linq;
using Bunker.Sim;
using NUnit.Framework;

namespace Bunker.Tests
{
    /// <summary>
    /// Sim tests on the starting surface. Handy coordinates (x, y with y up):
    /// hatch (14,16) · home (14,15) facing north · concrete pad x11..18, y14..15, clear only at x13..15
    /// (the rest is weeds, with rubble at (11,14) and (18,15)) · overgrown soil x12..17, y11..13 (saplings
    /// at (12,13) and (16,13)) · terminal (10,15) · rubble (10,14) · wreck (19,14) · weeds x19..23 on y15 ·
    /// stream x24..25 at y15.
    /// </summary>
    public class BunkerSimTests
    {
        static readonly Cell Hatch = new Cell(14, 16);
        static readonly Cell Home = new Cell(14, 15);

        static World Surface() => StarterSurface.Create();

        /// <summary>Bot and crop stats are tunable at runtime (the BunkerData asset); tests use the built-in numbers.</summary>
        [SetUp]
        public void UseDefaultStats() => GameRules.ResetToDefaults();

        [TearDown]
        public void RestoreDefaultStats() => GameRules.ResetToDefaults();

        static void Place(Bot b, int x, int y, Facing f)
        {
            b.Position = new Cell(x, y);
            b.Facing = f;
        }

        /// <summary>Runs a command to completion (multi-tick jobs included) and returns how it ended.</summary>
        static CommandResult Do(World w, Bot b, Command c)
        {
            CommandResult r;
            bool finished;
            int guard = 0;
            do r = w.Execute(b, c, out finished);
            while (!finished && guard++ < 200);
            return r;
        }

        /// <summary>Clears the pad (x 11..18, y 14..15) and the weeds between it and the stream (y = 15, x = 19..23).</summary>
        static void OpenPathToStream(World w)
        {
            for (int x = 11; x <= 18; x++)
            for (int y = 14; y <= 15; y++)
                w.RemoveDebris(new Cell(x, y));
            for (int x = 19; x <= 23; x++) w.RemoveDebris(new Cell(x, 15));
        }

        /// <summary>Clears the overgrown soil south of the pad (x 12..17, y 11..13) for farming tests.</summary>
        static void ClearPatch(World w)
        {
            for (int x = 12; x <= 17; x++)
            for (int y = 11; y <= 13; y++)
                w.RemoveDebris(new Cell(x, y));
        }

        /// <summary>A radish on tilled soil at <paramref name="c"/> (the patch is cleared first).</summary>
        static Tile PlantRadish(World w, Cell c)
        {
            ClearPatch(w);
            var t = w.Map[c];
            t.Tilled = true;
            t.Crop = Crops.Radish;
            return t;
        }

        /// <summary>
        /// Steps until the bot loops back to line 1 of its routine. Returns the tick that happened on and
        /// where the bot was just before (the end of its first pass).
        /// </summary>
        static int StepUntilLoop(World w, Bot bot, out Cell endOfPass)
        {
            int last = bot.Routine.Count - 1;
            int prev = -1;
            endOfPass = bot.Position;
            for (int guard = 0; guard < Balance.TicksPerDay; guard++)
            {
                var before = bot.Position;
                w.Step();
                int now = bot.LastIndexIn(-1);
                if (prev == last && now == 0)
                {
                    endOfPass = before;
                    return w.Tick;
                }
                prev = now;
            }
            return -1;
        }

        static Bot Build(World w, Recipe r)
        {
            foreach (var a in w.CostOf(r)) w.Stores.Add(a.Item, a.Amount);
            Assert.IsTrue(w.TryBuild(r, out var bot));
            return bot;
        }

        // ------------------------------------------------------------------ the surface

        [Test]
        public void Map_ParsesTheSurface()
        {
            var w = Surface();
            var m = w.Map;
            Assert.AreEqual(30, m.Width);
            Assert.AreEqual(20, m.Height);
            Assert.AreEqual(Hatch, m.Hatch);
            Assert.AreEqual(Home, m.Home);
            Assert.AreEqual(Facing.North, m.HomeFacing);
            Assert.AreEqual(Debris.Terminal, m[new Cell(10, 15)].Debris);
            Assert.AreEqual(Debris.Weeds, m[new Cell(20, 15)].Debris);
            Assert.IsTrue(m[new Cell(24, 15)].IsWater);
            Assert.IsFalse(m[new Cell(13, 14)].IsFarmland, "the pad is concrete");
            Assert.IsTrue(m.IsWalkable(new Cell(13, 14)), "a little of the pad is clear to stand on");

            var padWeeds = m[new Cell(12, 15)];
            Assert.AreEqual(Ground.Concrete, padWeeds.Ground);
            Assert.AreEqual(Debris.Weeds, padWeeds.Debris);
            Assert.AreEqual(Debris.Rubble, m[new Cell(18, 15)].Debris);
            Assert.AreEqual(Ground.Concrete, m[new Cell(18, 15)].Ground);

            Assert.AreEqual(Debris.Sapling, m[new Cell(12, 13)].Debris, "wood for the first farmhand");
            Assert.AreEqual(Ground.Soil, m[new Cell(13, 12)].Ground);
            Assert.IsTrue(m[new Cell(13, 12)].IsOvergrown, "no clear soil by the hatch: you clear your own");
            Assert.AreEqual(0, m.Count(t => t.IsFarmland && !t.IsOvergrown), "no clear soil anywhere");
            Assert.IsTrue(m.IsWalkable(new Cell(24, 4)), "the old bridge");
            Assert.Greater(m.Count(t => t.IsOvergrown), 400, "it's very overgrown");
        }

        [Test]
        public void Start_YouAndTheStores()
        {
            var w = Surface();
            Assert.AreEqual(1, w.Bots.Count);
            Assert.IsTrue(w.Player.IsPlayer);
            Assert.AreEqual(Home, w.Player.Position);
            Assert.AreEqual(Balance.StartingRadishSeeds, w.Stores.Get(Item.RadishSeeds));
            Assert.AreEqual(Balance.StartingRadishes, w.Stores.Food);
            Assert.AreEqual(3, w.NextRationDay);
            Assert.AreEqual(Balance.Ration(0), w.NextRationNeeded);
        }

        // ------------------------------------------------------------------ you

        [Test]
        public void Player_MovesOnInput_OneTilePerTick()
        {
            var w = Surface();
            var p = w.Player;
            w.SetPlayerCommand(Command.Step(Facing.South));
            w.Step();
            Assert.AreEqual(new Cell(14, 14), p.Position);
            w.Step();
            Assert.AreEqual(new Cell(14, 14), p.Position, "no input, no move");
            Assert.AreEqual(CommandResult.Idle, p.LastResult);

            w.SetPlayerCommand(Command.Face(Facing.West));
            w.Step();
            Assert.AreEqual(Facing.West, p.Facing);
            Assert.AreEqual(new Cell(14, 14), p.Position);
        }

        [Test]
        public void Player_JobsTakeSeveralTicks_AndIgnoreInputMeanwhile()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 11, 15, Facing.West); // the dead terminal is ahead
            var acted = CommandResult.None;
            w.PlayerActed += (c, r) => acted = r;

            w.SetPlayerCommand(w.SuggestUse(p));
            w.Step();
            Assert.IsTrue(w.PlayerBusy);
            for (int i = 1; i < Balance.ClearTicks(Debris.Terminal); i++)
            {
                w.SetPlayerCommand(Command.Step(Facing.South)); // ignored while working
                w.Step();
            }
            Assert.IsFalse(w.PlayerBusy);
            Assert.AreEqual(CommandResult.Ok, acted);
            Assert.AreEqual(new Cell(11, 15), p.Position);
            Assert.AreEqual(Debris.None, w.Map[new Cell(10, 15)].Debris);
            Assert.AreEqual(2, p.Load.Get(Item.Circuit));
            Assert.AreEqual(1, p.Load.Get(Item.Scrap));
        }

        [Test]
        public void SuggestUse_PicksTheObviousJob()
        {
            var w = Surface();
            var p = w.Player;
            Command Use(int x, int y, Facing f)
            {
                Place(p, x, y, f);
                return w.SuggestUse(p, Item.RadishSeeds);
            }

            Assert.AreEqual(CommandType.Grab, Use(14, 15, Facing.North).Type, "hatch, nothing to hand in: seeds");
            p.Load.Add(Item.Scrap, 1);
            Assert.AreEqual(CommandType.Deliver, Use(14, 15, Facing.North).Type, "hatch with salvage: deliver");
            p.Load.Clear();
            Assert.AreEqual(CommandType.Clear, Use(18, 15, Facing.East).Type);
            Assert.AreEqual(CommandType.Clear, Use(13, 13, Facing.South).Type, "overgrown soil");
            ClearPatch(w);
            Assert.AreEqual(CommandType.Till, Use(13, 13, Facing.South).Type);
            w.Map[new Cell(13, 12)].Tilled = true;
            Assert.AreEqual(CommandType.Plant, Use(13, 13, Facing.South).Type);
            w.Map[new Cell(13, 12)].Crop = Crops.Radish;
            Assert.AreEqual(CommandType.Water, Use(13, 13, Facing.South).Type);
            w.Map[new Cell(13, 12)].Growth = Crops.Radish.GrowTicks;
            Assert.AreEqual(CommandType.Harvest, Use(13, 13, Facing.South).Type);

            OpenPathToStream(w);
            var fill = Use(23, 15, Facing.East);
            Assert.AreEqual(CommandType.Grab, fill.Type);
            Assert.AreEqual(Item.Water, fill.Item);
        }

        // ------------------------------------------------------------------ clearing

        [Test]
        public void Clear_YieldsByWhatItIs_AndOpensTheWay()
        {
            var w = Surface();
            var p = w.Player;
            Assert.Less(w.DistanceTo(GoTarget.Stream, default)[14, 15], 0, "the stream is cut off at first");

            Place(p, 18, 15, Facing.East);
            for (int x = 19; x <= 23; x++)
            {
                var cell = new Cell(x, 15);
                bool seed = (x + 15) % 2 == 0;
                int seedsBefore = p.Load.Get(Item.RadishSeeds);
                Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Clear));
                Assert.AreEqual(seed ? 1 : 0, p.Load.Get(Item.RadishSeeds) - seedsBefore, $"wild seed at {cell}");
                Assert.AreEqual(CommandResult.Ok, w.Execute(p, Command.Step(Facing.East)));
            }
            Assert.AreEqual(5, p.Load.Get(Item.Fibre));
            Assert.AreEqual(new Cell(23, 15), p.Position);
            Assert.AreEqual(1, w.DistanceTo(GoTarget.Stream, default)[22, 15]);
            Assert.AreEqual(-1, w.DistanceTo(GoTarget.Stream, default)[14, 15], "the rubble at the end of the pad is in the way");
            w.RemoveDebris(new Cell(18, 15));
            Assert.AreEqual(5, w.DistanceTo(GoTarget.Stream, default)[18, 15], "clearing re-plans routes");
            w.Execute(p, Command.Face(Facing.West));
            Assert.AreEqual(CommandResult.NothingToClear, Do(w, p, Command.Clear), "already cleared");
        }

        [Test]
        public void Clear_NeedsRoomForWhatComesOff()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 18, 14, Facing.East); // the wreck: 3 scrap = 6 weight
            p.Load.Add(Item.Wood, 3);
            Assert.AreEqual(CommandResult.LoadFull, Do(w, p, Command.Clear));
            Assert.AreEqual(Debris.Wreck, w.Map[new Cell(19, 14)].Debris);
            p.Load.Clear();
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Clear));
            Assert.AreEqual(3, p.Load.Get(Item.Scrap));
        }

        [Test]
        public void Clear_TakesAsLongAsTheDebrisSays()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 18, 14, Facing.East);
            int ticks = 0;
            bool finished;
            do
            {
                w.Execute(p, Command.Clear, out finished);
                ticks++;
            } while (!finished);
            Assert.AreEqual(Balance.ClearTicks(Debris.Wreck), ticks);
        }

        // ------------------------------------------------------------------ farming (on the tile ahead)

        [Test]
        public void Farming_TillPlantWaterGrowHarvest_OnTheTileAhead()
        {
            var w = Surface();
            ClearPatch(w);
            var p = w.Player;
            var plot = new Cell(13, 12);
            Place(p, 13, 13, Facing.South);

            Assert.AreEqual(CommandResult.NotTilled, Do(w, p, Command.Plant()));
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Till));
            Assert.IsTrue(w.Map[plot].Tilled);
            Assert.AreEqual(CommandResult.NoSeeds, Do(w, p, Command.Plant()));
            p.Load.Add(Item.RadishSeeds, 1);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Plant()));
            Assert.AreEqual(Crops.Radish, w.Map[plot].Crop);
            Assert.AreEqual(CommandResult.NoWater, Do(w, p, Command.Water));
            p.Load.Add(Item.Water, 2);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Water));
            Assert.AreEqual(Crops.WaterLastsTicks, w.Map[plot].Moisture);
            Assert.AreEqual(CommandResult.AlreadyDone, Do(w, p, Command.Water));
            Assert.AreEqual(1, p.Load.Get(Item.Water));
            Assert.AreEqual(CommandResult.NotRipe, Do(w, p, Command.Harvest));

            for (int i = 0; i < 10; i++) w.Step();
            Assert.AreEqual(10, w.Map[plot].Growth, "grows every tick while the soil is wet");

            w.Map[plot].Growth = Crops.Radish.GrowTicks;
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Harvest));
            Assert.AreEqual(1, p.Load.Get(Item.Radish));
            Assert.IsTrue(w.Map[plot].Tilled, "ready to replant");
            Assert.IsTrue(w.Map[plot].Watered, "the soil stays wet");
        }

        [Test]
        public void Water_DriesOut_AndCropsOnlyGrowWhileItsWet()
        {
            var w = Surface();
            var t = PlantRadish(w, new Cell(13, 12));
            t.Moisture = Crops.WaterLastsTicks;
            Assert.AreEqual(2, t.WetLevel);

            for (int i = 0; i < Crops.WaterLastsTicks; i++) w.Step();
            Assert.IsFalse(t.Watered, "dried out");
            Assert.AreEqual(0, t.WetLevel);
            Assert.AreEqual(Crops.WaterLastsTicks, t.Growth);

            for (int i = 0; i < 50; i++) w.Step();
            Assert.AreEqual(Crops.WaterLastsTicks, t.Growth, "dry soil, no growth");
        }

        [Test]
        public void Rewatering_OnlyWorksOnceHalfTheWaterHasGone()
        {
            var w = Surface();
            var p = w.Player;
            var t = PlantRadish(w, new Cell(13, 12));
            Place(p, 13, 13, Facing.South);
            p.Load.Add(Item.Water, 5);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Water));

            for (int i = 0; i < Crops.WaterLastsTicks / 2 - 1; i++) w.Step();
            Assert.IsTrue(t.WellWatered);
            Assert.AreEqual(2, t.WetLevel);
            Assert.AreEqual(CommandResult.AlreadyDone, Do(w, p, Command.Water), "still wet: don't waste water");

            w.Step();
            Assert.IsFalse(t.WellWatered);
            Assert.AreEqual(1, t.WetLevel, "damp");
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Water));
            Assert.AreEqual(Crops.WaterLastsTicks, t.Moisture, "topped right up");
            Assert.AreEqual(3, p.Load.Get(Item.Water));
        }

        [Test]
        public void Water_WorksOnTilledSoil_PlantedOrNot()
        {
            var w = Surface();
            var p = w.Player;
            p.Load.Add(Item.Water, 5);
            Place(p, 13, 14, Facing.South);
            Assert.AreEqual(CommandResult.Overgrown, Do(w, p, Command.Water));
            ClearPatch(w);
            Assert.AreEqual(CommandResult.NotTilled, Do(w, p, Command.Water));
            w.Map[new Cell(13, 13)].Tilled = true;
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Water), "soak it before planting");
            Assert.IsTrue(w.Map[new Cell(13, 13)].Watered);
        }

        [Test]
        public void Crops_RipenAfterTheirWateredHours_RedrawingOnlyAtNewStages()
        {
            var w = Surface();
            var cell = new Cell(13, 12);
            var t = PlantRadish(w, cell);
            t.Moisture = Crops.Radish.GrowTicks * 2; // never dries during the test
            int redraws = 0;
            w.TileChanged += c => { if (c == cell) redraws++; };

            for (int i = 0; i < Crops.Radish.GrowTicks - 1; i++) w.Step();
            Assert.IsFalse(t.IsRipe);
            Assert.AreEqual(3, t.Stage);
            w.Step();
            Assert.IsTrue(t.IsRipe);
            Assert.AreEqual(4, t.Stage);
            Assert.AreEqual(Crops.Radish.GrowHours * Balance.TicksPerHour, t.Growth);
            Assert.AreEqual(4, redraws, "one redraw per growth stage, not one per tick");

            for (int i = 0; i < 20; i++) w.Step();
            Assert.AreEqual(Crops.Radish.GrowTicks, t.Growth, "ripe crops stop growing");
            Assert.AreEqual(1, w.RunFullDay().CropsRipened);
        }

        [Test]
        public void GroundChanged_OnlyWhenTheWayOpens()
        {
            var w = Surface();
            var p = w.Player;
            int ground = 0;
            w.GroundChanged += _ => ground++;
            PlantRadish(w, new Cell(13, 12));
            Assert.AreEqual(18, ground, "the patch was cleared");

            ground = 0;
            Place(p, 13, 13, Facing.South);
            p.Load.Add(Item.Water, 1);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Water));
            for (int i = 0; i < 200; i++) w.Step();
            Assert.AreEqual(0, ground, "watering and growing don't change where you can walk");
        }

        [Test]
        public void Till_RefusesOvergrownAndConcrete()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 18, 15, Facing.East);
            Assert.AreEqual(CommandResult.Overgrown, Do(w, p, Command.Till));
            Place(p, 14, 15, Facing.East);
            Assert.AreEqual(CommandResult.NotSoil, Do(w, p, Command.Till));

            // Weeds on the old pad: clearing them leaves concrete.
            Place(p, 13, 15, Facing.West);
            Assert.AreEqual(CommandResult.Overgrown, Do(w, p, Command.Till));
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Clear));
            Assert.AreEqual(CommandResult.NotSoil, Do(w, p, Command.Till));
        }

        // ------------------------------------------------------------------ the hatch and the stream

        [Test]
        public void Grab_WaterFromTheStream_SeedsFromTheHatch()
        {
            var w = Surface();
            var p = w.Player;
            OpenPathToStream(w);

            Place(p, 23, 15, Facing.East);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Grab(Item.Water, 99)));
            Assert.AreEqual(p.Capacity, p.Load.Get(Item.Water), "fills up to the weight limit");
            Assert.AreEqual(CommandResult.AlreadyDone, Do(w, p, Command.Grab(Item.Water, 99)), "a full tank isn't a mistake");
            p.Load.Clear();
            p.Load.Add(Item.Scrap, p.Capacity / 2);
            Assert.AreEqual(CommandResult.LoadFull, Do(w, p, Command.Grab(Item.Water, 99)), "full of salvage is");
            p.Load.Clear();
            Assert.AreEqual(CommandResult.NotAtHatch, Do(w, p, Command.Grab(Item.RadishSeeds, 3)));

            p.Load.Clear();
            Place(p, 14, 15, Facing.North);
            Assert.AreEqual(CommandResult.NotAtStream, Do(w, p, Command.Grab(Item.Water, 3)));
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Grab(Item.RadishSeeds, 4)));
            Assert.AreEqual(4, p.Load.Get(Item.RadishSeeds));
            Assert.AreEqual(Balance.StartingRadishSeeds - 4, w.Stores.Get(Item.RadishSeeds));
            Assert.AreEqual(CommandResult.OutOfStock, Do(w, p, Command.Grab(Item.PotatoSeeds, 4)));

            w.Stores.Add(Item.SquashSeeds, 1);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Grab(Item.None, 9)), "None: some of every seed");
            Assert.AreEqual(Balance.StartingRadishSeeds, p.Load.Get(Item.RadishSeeds));
            Assert.AreEqual(1, p.Load.Get(Item.SquashSeeds));
        }

        [Test]
        public void Deliver_HandsInFoodAndSalvage_KeepsSeedsAndWater()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 14, 15, Facing.North);
            Assert.AreEqual(CommandResult.NothingToDeliver, Do(w, p, Command.Deliver));
            p.Load.Add(Item.Scrap, 2);
            p.Load.Add(Item.Radish, 3);
            p.Load.Add(Item.RadishSeeds, 2);
            p.Load.Add(Item.Water, 1);
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Deliver));
            Assert.AreEqual(2, w.Stores.Get(Item.Scrap));
            Assert.AreEqual(Balance.StartingRadishes + 3, w.Stores.Get(Item.Radish));
            Assert.AreEqual(2, p.Load.Get(Item.RadishSeeds));
            Assert.AreEqual(1, p.Load.Get(Item.Water));

            Place(p, 14, 14, Facing.North);
            p.Load.Add(Item.Scrap, 1);
            Assert.AreEqual(CommandResult.NotAtHatch, Do(w, p, Command.Deliver));
        }

        [Test]
        public void Seeds_RideInAPouch_AndTakeNoRoomInTheBag()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 14, 15, Facing.North);
            p.Load.Add(Item.Water, p.Capacity);
            Assert.AreEqual(0, p.FreeWeight, "bag full of water");
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Grab(Item.None, Balance.SeedsPerGrab)), "seeds still fit");
            Assert.AreEqual(Balance.StartingRadishSeeds, p.Load.Get(Item.RadishSeeds));
            Assert.AreEqual(p.Capacity, p.Load.Weight);
            Assert.AreEqual(Item.Water, p.Load.Main, "seeds never show as what you're carrying");
            Assert.IsTrue(p.Load.HasSeeds);
            Assert.AreEqual("6 radish", p.Load.SeedText());
            Assert.AreEqual("10 water", p.Load.BagText());

            p.Load.Remove(Item.Water, 99);
            Assert.IsFalse(p.Load.HasBagItems, "only seeds: the bag itself is empty");
            Assert.AreEqual(Item.None, p.Load.Main);
            Assert.AreEqual(0, p.Load.Weight);
        }

        [Test]
        public void Dump_PoursTheWaterOut_AndKeepsTheRest()
        {
            var w = Surface();
            var p = w.Player;
            Place(p, 18, 14, Facing.East); // the wreck: 6 weight of scrap
            p.Load.Add(Item.Water, p.Capacity - 2);
            p.Load.Add(Item.Fibre, 2);
            p.Load.Add(Item.RadishSeeds, 3);
            Assert.AreEqual(CommandResult.LoadFull, Do(w, p, Command.Clear));

            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Dump));
            Assert.AreEqual(0, p.Load.Get(Item.Water));
            Assert.AreEqual(2, p.Load.Get(Item.Fibre), "only the water goes");
            Assert.AreEqual(3, p.Load.Get(Item.RadishSeeds));
            Assert.AreEqual(CommandResult.NothingToDump, Do(w, p, Command.Dump));
            Assert.IsFalse(CommandInfo.IsHardProblem(CommandResult.NothingToDump), "nothing to dump isn't a broken routine");
            Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Clear), "room for the salvage now");

            // Players use X; bots get a block, water bots included.
            CollectionAssert.Contains(CommandInfo.PaletteOrder, CommandType.Dump);
            Build(w, Recipe.Farmhand);
            var d = Build(w, Recipe.WaterBot);
            d.Load.Add(Item.Water, 4);
            Assert.AreEqual(CommandResult.Ok, Do(w, d, Command.Dump));
            Assert.IsTrue(d.Load.IsEmpty);
        }

        [Test]
        public void Dump_InARoutine_TakesATick()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            bot.Load.Add(Item.Water, 5);
            bot.Routine.SetAll(new[] { Command.Dump, Command.Wait });
            Assert.AreEqual(2, RoutineTracer.Trace(w, bot).Ticks);
            w.Step();
            Assert.AreEqual(CommandResult.Ok, bot.LastResult);
            Assert.AreEqual(0, bot.Load.Get(Item.Water));
        }

        // ------------------------------------------------------------------ the clock & night

        [Test]
        public void Clock_RunsFromSixInTheMorningToTwoAtNight()
        {
            var w = Surface();
            Assert.AreEqual(6 * 60, w.ClockMinutes);
            for (int i = 0; i < Balance.TicksPerDay / 2; i++) w.Step();
            Assert.AreEqual(16 * 60, w.ClockMinutes, "halfway is 4 pm");
            Assert.AreEqual(1, w.Day);
        }

        [Test]
        public void Night_ResetsEveryoneAtTheHatch_AndTheNextDayStartsByItself()
        {
            var w = Surface();
            ClearPatch(w);
            var p = w.Player;
            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.SetAll(new[] { Command.Step(Facing.South), Command.Step(Facing.South), Command.Wait, Command.Wait });
            for (int i = 0; i < 2 * World.TicksPerTile(bot) + 1; i++) w.Step();
            Assert.AreEqual(2, bot.LastIndexIn(-1));

            Place(p, 13, 12, Facing.South);
            p.Load.Add(Item.Scrap, 2);
            p.Load.Add(Item.RadishSeeds, 3);
            p.Load.Add(Item.Water, 4);
            bot.Load.Add(Item.Fibre, 1);
            int scrap = w.Stores.Get(Item.Scrap);
            int seeds = w.Stores.Get(Item.RadishSeeds);
            bool morning = false;
            w.DayStarted += () => morning = true;

            var report = w.RunFullDay();

            Assert.IsTrue(morning);
            Assert.AreEqual(2, w.Day);
            Assert.AreEqual(0, w.Tick);
            foreach (var b in w.Bots)
            {
                Assert.AreEqual(Home, b.Position, b.Name);
                Assert.AreEqual(Facing.North, b.Facing, b.Name);
                Assert.IsTrue(b.Load.IsEmpty, b.Name);
            }
            Assert.AreEqual(scrap + 2, w.Stores.Get(Item.Scrap));
            Assert.AreEqual(seeds + 3, w.Stores.Get(Item.RadishSeeds));
            Assert.AreEqual(1, w.Stores.Get(Item.Fibre));
            Assert.AreEqual(2, report.Delivered.Get(Item.Scrap));
            Assert.AreEqual(0, report.Delivered.Get(Item.RadishSeeds), "seeds back on the shelf aren't a delivery");

            w.Step();
            Assert.AreEqual(0, bot.LastIndexIn(-1), "the routine starts again from line 1");
            Assert.AreEqual(new Cell(14, 14), bot.Position);
        }

        [Test]
        public void Bots_WalkAtTheirKindsSpeed_YouAtOneTilePerTick()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            int perTile = World.TicksPerTile(bot);
            Assert.Greater(perTile, 1, "bots are slow");
            bot.Routine.SetAll(new[] { Command.Step(Facing.South), Command.Face(Facing.East) });

            w.Step();
            Assert.AreEqual(new Cell(14, 14), bot.Position, "on its way to the next tile from the first tick");
            Assert.AreEqual(CommandResult.Moving, bot.LastResult);
            for (int i = 1; i < perTile; i++) w.Step();
            Assert.AreEqual(CommandResult.Ok, bot.LastResult);
            Assert.AreEqual(0, bot.LastIndexIn(-1));
            w.Step();
            Assert.AreEqual(1, bot.LastIndexIn(-1), "the next line starts once the tile is crossed");

            Assert.AreEqual(1, World.TicksPerTile(w.Player));
        }

        [Test]
        public void BotAndCropStats_CanBeTuned()
        {
            var w = Surface();
            var farmhand = Build(w, Recipe.Farmhand);

            BotKinds.Configure(BotKind.Farmhand, new BotStats
            {
                TicksPerTile = 2,
                Capacity = 15,
                Cost = new System.Collections.Generic.List<ItemAmount> { new ItemAmount(Item.Wood, 1) },
            });
            w.RefreshStats();
            Assert.AreEqual(15, farmhand.Capacity, "existing bots follow");
            Assert.AreEqual(2, World.TicksPerTile(farmhand));
            var cost = w.CostOf(Recipe.Farmhand);
            Assert.AreEqual(1, cost.First(a => a.Item == Item.Wood).Amount);
            Assert.AreEqual(Balance.ExtraScrapPerBot, cost.First(a => a.Item == Item.Scrap).Amount, "still pricier per bot built");

            Crops.Radish.Configure(growHours: 2, food: 5, weight: 0, seedsSaved: 4);
            Assert.AreEqual(2 * Balance.TicksPerHour, Crops.Radish.GrowTicks);
            Crops.ConfigureWater(5);
            Assert.AreEqual(5 * Balance.TicksPerHour, Crops.WaterLastsTicks);
            Assert.AreEqual(0, ItemInfo.Weight(Item.Radish));
            Assert.AreEqual(5, ItemInfo.FoodValue(Item.Radish));
            int seeds = w.Stores.Get(Item.RadishSeeds);
            Assert.IsTrue(w.TryBuild(Recipe.SaveRadishSeeds));
            Assert.AreEqual(seeds + 4, w.Stores.Get(Item.RadishSeeds));

            GameRules.ResetToDefaults();
            Assert.AreEqual(Crops.Radish.DefaultWeight, ItemInfo.Weight(Item.Radish));
            Assert.AreEqual(Crops.DefaultWaterLastsHours, Crops.WaterLastsHours);
            Assert.AreEqual(BotKinds.DefaultStats(BotKind.Farmhand).TicksPerTile, World.TicksPerTile(farmhand));
        }

        [Test]
        public void PlayerPose_PutsYouOnATile_ButNotMidJobOrInAWall()
        {
            var w = Surface();
            var p = w.Player;
            w.SetPlayerPose(new Cell(13, 12), Facing.South);
            Assert.AreEqual(Home, p.Position, "can't stand in a thicket");
            ClearPatch(w);
            w.SetPlayerPose(new Cell(13, 12), Facing.South);
            Assert.AreEqual(new Cell(13, 12), p.Position);
            Assert.AreEqual(Facing.South, p.Facing);
            w.SetPlayerPose(new Cell(10, 15), Facing.North); // the dead terminal
            Assert.AreEqual(new Cell(13, 12), p.Position);

            w.SetPlayerCommand(Command.Till);
            w.Step();
            Assert.IsTrue(w.PlayerBusy);
            w.SetPlayerPose(new Cell(13, 13), Facing.North);
            Assert.AreEqual(new Cell(13, 12), p.Position, "can't move mid-job");
            w.CancelPlayerJob();
            Assert.IsFalse(w.PlayerBusy);
            Assert.IsFalse(w.Map[new Cell(13, 11)].Tilled, "a cancelled job does nothing");
            w.SetPlayerPose(new Cell(13, 13), Facing.North);
            Assert.AreEqual(new Cell(13, 13), p.Position);
        }

        [Test]
        public void Routines_AlwaysLoop()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.SetAll(new[] { Command.Face(Facing.East), Command.Face(Facing.West) });
            for (int i = 0; i < 5; i++)
            {
                w.Step();
                Assert.AreEqual(i % 2, bot.LastIndexIn(-1));
                Assert.AreNotEqual(CommandResult.Idle, bot.LastResult);
            }
        }

        [Test]
        public void RestartRoutine_GoesBackToLineOne_WhereTheBotStands()
        {
            var w = Surface();
            ClearPatch(w);
            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.SetAll(new[] { Command.Step(Facing.South), Command.Wait, Command.Wait, Command.Face(Facing.East) });
            for (int i = 0; i < 2; i++) w.Step();
            var at = bot.Position;
            w.RestartRoutine(bot);
            w.Step();
            Assert.AreEqual(0, bot.LastIndexIn(-1));
            Assert.AreEqual(at.Step(Facing.South), bot.Position);
        }

        [Test]
        public void Rations_EatEveryThirdNight_MissesAreStrikes_ThreeSealTheBunker()
        {
            var w = Surface();
            Outcome outcome = Outcome.None;
            w.GameOver += o => outcome = o;

            w.RunFullDay();
            w.RunFullDay();
            var r3 = w.RunFullDay();
            Assert.IsTrue(r3.RationNight);
            Assert.AreEqual(Balance.Ration(0), r3.RationNeeded);
            Assert.IsFalse(r3.RationShort, "the starting radishes cover the first ration");
            Assert.AreEqual(0, w.Stores.Food);
            Assert.AreEqual(6, w.NextRationDay);

            for (int d = 4; d <= 12 && !w.IsGameOver; d++) w.RunFullDay();
            Assert.AreEqual(3, w.Strikes);
            Assert.AreEqual(Outcome.Sealed, outcome);
            Assert.AreEqual(12, w.Day);
        }

        [Test]
        public void Rations_EatSmallFoodFirst()
        {
            var w = Surface();
            w.Stores.Remove(Item.Radish, 99);
            w.Stores.Add(Item.Squash, 1);
            w.Stores.Add(Item.Radish, 3);
            w.Stores.Add(Item.Potato, 1);
            w.RunFullDay();
            w.RunFullDay();
            var r = w.RunFullDay(); // needs 4: three radishes, then a potato
            Assert.AreEqual(6, r.RationEaten);
            Assert.AreEqual(1, w.Stores.Get(Item.Squash));
            Assert.AreEqual(0, w.Stores.Get(Item.Potato));
        }

        [Test]
        public void FeedingTheBunkerToTheEnd_OpensTheDoors()
        {
            var w = Surface();
            w.FinalDay = 3;
            Outcome outcome = Outcome.None;
            w.GameOver += o => outcome = o;
            for (int d = 0; d < 3; d++) w.RunFullDay();
            Assert.AreEqual(Outcome.DoorsOpen, outcome);
            int tick = w.Tick;
            w.Step();
            Assert.AreEqual(tick, w.Tick, "time stops at the end");
        }

        // ------------------------------------------------------------------ workshop

        [Test]
        public void Workshop_BuildsBots_FromTheStores()
        {
            var w = Surface();
            Assert.IsFalse(w.CanAfford(Recipe.Farmhand));
            int cost = w.CostOf(Recipe.Farmhand).First(a => a.Item == Item.Scrap).Amount;

            var sprocket = Build(w, Recipe.Farmhand);
            Assert.AreEqual("Sprocket", sprocket.Name);
            Assert.AreEqual(BotKind.Farmhand, sprocket.Kind);
            Assert.AreEqual(Home, sprocket.Position);
            Assert.AreEqual(0, w.Stores.Get(Item.Scrap));
            Assert.AreEqual(cost + Balance.ExtraScrapPerBot, w.CostOf(Recipe.Farmhand).First(a => a.Item == Item.Scrap).Amount);

            var drizzle = Build(w, Recipe.WaterBot);
            Assert.AreEqual("Drizzle", drizzle.Name);
            Assert.AreEqual(BotKind.WaterBot, drizzle.Kind);
            Assert.AreEqual(3, w.Bots.Count);
        }

        [Test]
        public void Workshop_WaterBotsNeedAFarmhandFirst()
        {
            var w = Surface();
            Assert.AreEqual(Recipe.Farmhand, World.AllRecipes[0], "the farmhand is the first bot");
            foreach (var a in w.CostOf(Recipe.WaterBot)) w.Stores.Add(a.Item, a.Amount);
            Assert.IsFalse(w.IsUnlocked(Recipe.WaterBot));
            Assert.IsNotNull(w.LockedReason(Recipe.WaterBot));
            Assert.IsFalse(w.CanAfford(Recipe.WaterBot), "locked, however much is in the stores");
            Assert.IsFalse(w.TryBuild(Recipe.WaterBot));
            Assert.AreEqual(1, w.Bots.Count);

            Build(w, Recipe.Farmhand);
            Assert.IsTrue(w.IsUnlocked(Recipe.WaterBot));
            Assert.IsNull(w.LockedReason(Recipe.WaterBot));
            Build(w, Recipe.WaterBot);
            Assert.AreEqual(3, w.Bots.Count);
        }

        [Test]
        public void Workshop_TanksAndSeeds()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            Build(w, Recipe.BiggerTanks);
            Assert.AreEqual(Balance.BotCapacity + Balance.CapacityUpgrade, w.Player.Capacity);
            Assert.AreEqual(Balance.BotCapacity + Balance.CapacityUpgrade, bot.Capacity);

            int seeds = w.Stores.Get(Item.RadishSeeds);
            Assert.IsTrue(w.TryBuild(Recipe.SaveRadishSeeds));
            Assert.AreEqual(seeds + Crops.Radish.SeedsSaved, w.Stores.Get(Item.RadishSeeds));
            Assert.AreEqual(Balance.StartingRadishes - 1, w.Stores.Get(Item.Radish));
            Assert.IsFalse(w.TryBuild(Recipe.SaveSquashSeeds), "no squash to save");
        }

        [Test]
        public void FirstFarmhand_IsReachableOnDayOne_FromWhatsAroundTheHatch()
        {
            var w = Surface();
            var p = w.Player;
            void Clear(int x, int y, Facing f)
            {
                Place(p, x, y, f);
                Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Clear), $"clearing from ({x},{y}) facing {f}");
            }
            void HandIn()
            {
                Place(p, 14, 15, Facing.North);
                Assert.AreEqual(CommandResult.Ok, Do(w, p, Command.Deliver));
            }

            // West along the pad to the dead terminal, then the rubble below it.
            Clear(13, 15, Facing.West);
            Clear(12, 15, Facing.West);
            Clear(11, 15, Facing.West); // terminal: 1 scrap, 2 circuits
            HandIn();
            Clear(11, 15, Facing.South); // rubble on the pad
            Clear(11, 14, Facing.West); // rubble
            HandIn();
            // A sapling just south of the pad for wood.
            Clear(13, 14, Facing.West); // weeds
            Clear(12, 14, Facing.South); // sapling
            HandIn();
            // East along the pad to the rubble at its end.
            Clear(15, 15, Facing.East);
            Clear(16, 15, Facing.East);
            Clear(17, 15, Facing.East); // rubble on the pad
            HandIn();

            Assert.IsTrue(w.CanAfford(Recipe.Farmhand), string.Join(", ", w.Stores.ToList()));
            Assert.IsFalse(w.CanAfford(Recipe.WaterBot), "locked until there's a farmhand");
            Assert.IsTrue(w.TryBuild(Recipe.Farmhand));
            Assert.IsTrue(w.IsUnlocked(Recipe.WaterBot));
        }

        [Test]
        public void StarterCommands_ClearAndWaterThreeEast()
        {
            var w = Surface();
            var clear = w.Library.Macros.First(m => m.Name == StarterSurface.ClearCommandName);
            Assert.IsNotNull(w.Library.Macros.First(m => m.Name == StarterSurface.WaterCommandName));

            var bot = Build(w, Recipe.Farmhand);
            Place(bot, 15, 15, Facing.North);
            bot.Routine.SetAll(new[] { Command.Run(clear) });
            for (int guard = 0; guard < 200 && bot.Position != new Cell(18, 15); guard++) w.Step();
            Assert.AreEqual(Debris.None, w.Map[new Cell(16, 15)].Debris);
            Assert.AreEqual(Debris.None, w.Map[new Cell(17, 15)].Debris);
            Assert.AreEqual(Debris.None, w.Map[new Cell(18, 15)].Debris, "the rubble too");
            Assert.AreEqual(new Cell(18, 15), bot.Position, "steps onto the last tile it cleared");
            Assert.AreEqual(2, bot.Load.Get(Item.Scrap));
            Assert.AreEqual(0, bot.ProblemsToday);

            // Looping, it carries on east: the weeds between the pad and the stream go next.
            for (int i = 0; i < 12; i++) w.Step();
            Assert.AreEqual(Debris.None, w.Map[new Cell(19, 15)].Debris);
            Assert.AreEqual(0, bot.ProblemsToday);
        }

        // ------------------------------------------------------------------ bots

        /// <summary>A water bot watering three radishes at (13..15, 12) from (12,12), facing east (a farmhand stands idle).</summary>
        static (World w, Bot bot) WateringBot()
        {
            var w = Surface();
            OpenPathToStream(w);
            for (int x = 13; x <= 15; x++) PlantRadish(w, new Cell(x, 12));
            Build(w, Recipe.Farmhand);
            var bot = Build(w, Recipe.WaterBot);
            bot.Routine.SetAll(new[]
            {
                Command.GoTo(GoTarget.Stream), Command.Grab(Item.Water, 3),
                Command.GoToTile(12, 12), Command.Face(Facing.East),
                Command.Run(w.Library.Macros.First(m => m.Name == StarterSurface.WaterCommandName)),
            });
            return (w, bot);
        }

        [Test]
        public void Bot_RunsItsRoutine_WatersTheRow()
        {
            var (w, bot) = WateringBot();
            var report = w.RunFullDay();
            Assert.AreEqual(3, report.CropsRipened, "kept wet, radishes ripen within a day");
            Assert.AreEqual(3, report.CropsRipe);
            Assert.AreEqual(0, report.CropsDry);
        }

        [Test]
        public void Bot_GoToStreamOrHatch_FacesIt()
        {
            var (w, bot) = WateringBot();
            bool finished;
            do w.Execute(bot, Command.GoTo(GoTarget.Stream), out finished);
            while (!finished);
            Assert.IsTrue(w.Map.IsWater(World.Ahead(bot)));
            do w.Execute(bot, Command.GoTo(GoTarget.Hatch), out finished);
            while (!finished);
            Assert.AreEqual(Hatch, World.Ahead(bot));
        }

        [Test]
        public void Tracer_TimingMatchesTheRealDay()
        {
            var (w, bot) = WateringBot();
            var trace = RoutineTracer.Trace(w, bot);
            Assert.AreEqual(0, trace.BlockedCount);
            CollectionAssert.Contains(trace.PickedTiles, new Cell(12, 12));

            int loopedAt = StepUntilLoop(w, bot, out _);
            Assert.AreEqual(trace.Ticks + 1, loopedAt, "back to line 1 on the tick after the last command");
        }

        [Test]
        public void Tracer_TreatsTilesClearedEarlierAsOpen()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.SetAll(new[]
            {
                Command.GoToTile(15, 15), Command.Face(Facing.East),
                Command.Clear, Command.Step(Facing.East), Command.Clear, Command.Step(Facing.East),
            });
            var trace = RoutineTracer.Trace(w, bot);
            Assert.AreEqual(0, trace.BlockedCount);
            Assert.AreEqual(new Cell(17, 15), trace.End);

            int loopedAt = StepUntilLoop(w, bot, out var end);
            Assert.AreEqual(new Cell(17, 15), end);
            Assert.AreEqual(trace.Ticks + 1, loopedAt);
        }

        [Test]
        public void WaterBot_OnlyDoesWaterJobs()
        {
            var w = Surface();
            Build(w, Recipe.Farmhand);
            var d = Build(w, Recipe.WaterBot);
            Place(d, 18, 15, Facing.East);
            Assert.AreEqual(CommandResult.CantDo, Do(w, d, Command.Clear));
            Assert.AreEqual(Debris.Weeds, w.Map[new Cell(19, 15)].Debris);
            Place(d, 13, 13, Facing.South);
            Assert.AreEqual(CommandResult.CantDo, Do(w, d, Command.Till));
            Place(d, 14, 15, Facing.North);
            Assert.AreEqual(CommandResult.CantDo, Do(w, d, Command.Grab(Item.RadishSeeds, 2)));
            Assert.AreEqual(CommandResult.CantDo, Do(w, d, Command.Deliver));
            Assert.AreEqual(CommandResult.Ok, Do(w, d, Command.Face(Facing.East)));

            d.Routine.SetAll(new[] { Command.Clear, Command.Wait });
            Assert.AreEqual(1, RoutineTracer.Trace(w, d).CantDoCount);
        }

        // ------------------------------------------------------------------ preview

        [Test]
        public void Preview_MatchesTheRealDay_AndLeavesItUntouched()
        {
            var (w, bot) = WateringBot();
            var preview = DayPreview.Run(w);

            Assert.AreEqual(0, w.Tick, "the real day hasn't moved");
            Assert.IsFalse(w.Map.AllCells().Any(c => w.Map[c].Watered), "the real surface wasn't watered");
            Assert.Greater(preview.TasksDone(bot.Id, CommandType.Water), 3, "rewaters as the soil dries");
            Assert.AreEqual(3, preview.Night.CropsRipened);

            bool night = false;
            w.DayEnded += _ => night = true;
            while (!night)
            {
                w.Step();
                if (night) break;
                Assert.IsTrue(preview.TryGetFrame(bot.Id, w.Tick, out var f));
                Assert.AreEqual(bot.Position, f.Position, $"tick {w.Tick}");
                Assert.AreEqual(bot.Facing, f.Facing, $"tick {w.Tick}");
                Assert.AreEqual(bot.Load.Weight, f.LoadWeight, $"tick {w.Tick}");
                Assert.AreEqual(bot.LastResult, f.Result, $"tick {w.Tick}");
            }
            Assert.AreEqual(Balance.TicksPerDay, preview.EndTick);
        }

        [Test]
        public void Preview_ShowsTonightsRation()
        {
            var w = Surface();
            w.RunFullDay();
            w.RunFullDay();
            var preview = DayPreview.Run(w);
            Assert.IsTrue(preview.Night.RationNight);
            Assert.AreEqual(Balance.Ration(0), preview.Night.RationNeeded);
            Assert.AreEqual(Balance.StartingRadishes, w.Stores.Food, "the real bunker hasn't eaten");
        }

        [Test]
        public void Preview_RestartsBotsThatWereEdited()
        {
            var w = Surface();
            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.SetAll(new[] { Command.Wait, Command.Wait, Command.Wait, Command.Face(Facing.East) });
            for (int i = 0; i < 2; i++) w.Step();
            Assert.AreEqual(1, bot.LastIndexIn(-1));

            var carryOn = DayPreview.Run(w);
            var restart = DayPreview.Run(w, b => b == bot);
            Assert.IsTrue(carryOn.TryGetFrame(bot.Id, w.Tick + 1, out var a));
            Assert.IsTrue(restart.TryGetFrame(bot.Id, w.Tick + 1, out var r));
            Assert.AreEqual(2, a.IndexIn(-1), "carries on from line 3");
            Assert.AreEqual(0, r.IndexIn(-1), "starts again from line 1");
            Assert.AreEqual(1, bot.LastIndexIn(-1), "the real bot is untouched");
        }

        [Test]
        public void Routine_SettingALineToItselfIsNotAnEdit()
        {
            var r = new Routine();
            r.Add(Command.Grab(Item.Water, 1));
            int changes = 0;
            r.Changed += () => changes++;
            r.Set(0, Command.Grab(Item.Water, 1).WithCount(0)); // "-" at the minimum
            Assert.AreEqual(0, changes);
            r.Set(0, Command.Grab(Item.Water, 2));
            Assert.AreEqual(1, changes);
        }

        // ------------------------------------------------------------------ custom commands & limits

        [Test]
        public void Library_PreventsSelfRuns_AndDeleteStripsRuns()
        {
            var w = Surface();
            var a = w.Library.Create("A");
            var b = w.Library.Create("B");
            b.Routine.Add(Command.Run(a));
            Assert.IsFalse(w.Library.CanRunFrom(a, a.Id));
            Assert.IsFalse(w.Library.CanRunFrom(a, b.Id), "A -> B -> A");
            Assert.IsTrue(w.Library.CanRunFrom(null, a.Id));

            var bot = Build(w, Recipe.Farmhand);
            bot.Routine.Add(Command.Run(a));
            w.DeleteMacro(a);
            Assert.IsFalse(bot.Routine.Runs(a.Id));
            Assert.AreEqual(0, b.Routine.Count);
        }

        [Test]
        public void Commands_CycleTheirOptions()
        {
            Assert.AreEqual(GoTarget.Hatch, Command.GoTo(GoTarget.Stream).WithNextOption().Target);
            Assert.AreEqual(GoTarget.Stream, Command.GoTo(GoTarget.Tile).WithNextOption().Target);
            Assert.AreEqual(Facing.South, Command.Step(Facing.East).WithNextOption().Dir);
            Assert.AreEqual(Facing.South, Command.Face(Facing.East).WithNextOption().Dir);
            Assert.AreEqual(Item.RadishSeeds, Command.Grab(Item.Water, 3).WithNextOption().Item);
            Assert.AreEqual(Item.RadishSeeds, Command.Plant().WithNextOption().Item);
            Assert.AreEqual(Balance.MaxGrab, Command.Grab(Item.Water, 3).WithCount(999).Count);
        }

        [Test]
        public void Limits_RoomForManyCustomCommandsAndADayLongRoutine()
        {
            var w = Surface();
            Assert.Greater(CommandLibrary.MaxMacros, 8);
            while (!w.Library.IsFull) Assert.IsNotNull(w.Library.Create());
            Assert.IsNull(w.Library.Create());

            var r = new Routine();
            for (int i = 0; i < Routine.MaxLength; i++) Assert.IsTrue(r.Add(Command.Wait));
            Assert.IsFalse(r.Add(Command.Wait));
        }
    }
}
