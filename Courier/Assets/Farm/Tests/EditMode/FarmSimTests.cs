using System.Linq;
using Farm.Sim;
using NUnit.Framework;

namespace Farm.Tests
{
    /// <summary>
    /// Sim tests on the starter farm. Handy coordinates (x, y with y up):
    /// home/door (8,11) · field x4..15, y3..9 · pre-tilled (5..7, 7..8) · parsnips (5..7, 4..6)
    /// well (18..19, 7..8) · bin (12,13) · seed shed (5,13) · pond top-right.
    /// </summary>
    public class FarmSimTests
    {
        static World Farm() => StarterFarm.Create();

        /// <summary>The starter farm with Sprocket's routine cleared, ready for a test to drive.</summary>
        static (World w, Bot b) Blank()
        {
            var w = StarterFarm.Create();
            var b = w.Bots[0];
            b.Routine.Clear();
            return (w, b);
        }

        static void Teleport(Bot b, int x, int y) => b.Position = new Cell(x, y);

        // ------------------------------------------------------------------ map

        [Test]
        public void Map_ParsesTheLayout()
        {
            var w = Farm();
            Assert.AreEqual(24, w.Map.Width);
            Assert.AreEqual(16, w.Map.Height);
            Assert.AreEqual(new Cell(8, 11), w.Map.Home);
            Assert.AreEqual(Structure.Well, w.Map[new Cell(18, 8)].Structure);
            Assert.IsTrue(w.Map[new Cell(10, 5)].IsFarmland);
            Assert.IsFalse(w.Map[new Cell(10, 5)].Tilled);
            Assert.IsTrue(w.Map[new Cell(6, 7)].Tilled);
            Assert.AreEqual(Crops.Parsnip, w.Map[new Cell(6, 5)].Crop);
            Assert.IsFalse(w.Map.IsWalkable(new Cell(3, 6)), "fence");
            Assert.IsTrue(w.Map.IsWalkable(new Cell(6, 5)), "bots can walk over crops");
        }

        // ------------------------------------------------------------------ movement

        [Test]
        public void GoToWell_WalksOneTilePerTick_ToTheNearestWaterSource()
        {
            var (w, b) = Blank();
            var cmd = Command.GoTo(GoTarget.Well);
            int expected = w.DistanceTo(GoTarget.Well, default)[8, 11];
            Assert.Greater(expected, 5);

            int ticks = 0;
            bool finished;
            do
            {
                w.Execute(b, cmd, out finished);
                ticks++;
            } while (!finished && ticks < 100);

            Assert.AreEqual(expected, ticks);
            Assert.IsTrue(w.Map.IsNextTo(b.Position, Structure.Well));
        }

        [Test]
        public void GoToTile_AndStep_RespectObstacles()
        {
            var (w, b) = Blank();
            var to = Command.GoToTile(10, 5);
            bool finished;
            int guard = 0;
            do w.Execute(b, to, out finished); while (!finished && guard++ < 100);
            Assert.AreEqual(new Cell(10, 5), b.Position);

            Assert.AreEqual(CommandResult.NoPath, w.Execute(b, Command.GoToTile(3, 6)), "a fence tile");
            Teleport(b, 4, 6);
            Assert.AreEqual(CommandResult.Blocked, w.Execute(b, Command.Step(Facing.West)));
            Assert.AreEqual(new Cell(4, 6), b.Position);
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Step(Facing.North)));
            Assert.AreEqual(new Cell(4, 7), b.Position);
        }

        // ------------------------------------------------------------------ tasks

        [Test]
        public void Grab_NeedsTheSource_AndRespectsWeight()
        {
            var (w, b) = Blank();
            Assert.AreEqual(CommandResult.NotAtSource, w.Execute(b, Command.Grab(Item.Water, 5)));

            Teleport(b, 17, 7); // next to the well
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Grab(Item.Water, 20)));
            Assert.AreEqual(Balance.BotCapacity, b.Load.Get(Item.Water), "only as much as it can carry");
            Assert.AreEqual(CommandResult.LoadFull, w.Execute(b, Command.Grab(Item.Water, 1)));

            Teleport(b, 4, 13); // next to the shed
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Grab(Item.ParsnipSeeds, 5)), "seeds weigh nothing");
            Assert.AreEqual(5, b.Load.Get(Item.ParsnipSeeds));
            Assert.AreEqual(Balance.StartingParsnipSeeds - 5, w.SeedStock.Get(Item.ParsnipSeeds));
            Assert.AreEqual(CommandResult.OutOfStock, w.Execute(b, Command.Grab(Item.PotatoSeeds, 1)));
        }

        [Test]
        public void Till_TakesThreeTicks_OnlyOnFarmland()
        {
            var (w, b) = Blank();
            Assert.AreEqual(CommandResult.NotFarmland, w.Execute(b, Command.Till), "the door path");

            Teleport(b, 10, 5);
            Assert.AreEqual(CommandResult.Working, w.Execute(b, Command.Till, out bool done));
            Assert.IsFalse(done);
            Assert.AreEqual(CommandResult.Working, w.Execute(b, Command.Till, out done));
            Assert.IsFalse(w.Map[b.Position].Tilled);
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Till, out done));
            Assert.IsTrue(done);
            Assert.IsTrue(w.Map[b.Position].Tilled);
            Assert.AreEqual(CommandResult.AlreadyDone, w.Execute(b, Command.Till));
        }

        [Test]
        public void Plant_NeedsTilledSoil_AndSeeds()
        {
            var (w, b) = Blank();
            Teleport(b, 10, 5);
            Assert.AreEqual(CommandResult.NotTilled, w.Execute(b, Command.Plant()));

            Teleport(b, 6, 7); // pre-tilled
            Assert.AreEqual(CommandResult.NoSeeds, w.Execute(b, Command.Plant()));
            b.Load.Add(Item.PotatoSeeds, 1);
            b.Load.Add(Item.ParsnipSeeds, 1);
            Assert.AreEqual(CommandResult.NoSeeds, w.Execute(b, Command.Plant(Item.CauliflowerSeeds)));
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Plant()));
            Assert.AreEqual(Crops.Parsnip, w.Map[b.Position].Crop, "'any seed' picks in catalogue order");
            Assert.AreEqual(CommandResult.AlreadyDone, w.Execute(b, Command.Plant()));
        }

        [Test]
        public void Water_UsesOneUnit_AndNotTwice()
        {
            var (w, b) = Blank();
            Teleport(b, 6, 5);
            Assert.AreEqual(CommandResult.NoWater, w.Execute(b, Command.Water));
            b.Load.Add(Item.Water, 2);
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Water));
            Assert.IsTrue(w.Map[b.Position].Watered);
            Assert.AreEqual(CommandResult.AlreadyDone, w.Execute(b, Command.Water));
            Assert.AreEqual(1, b.Load.Get(Item.Water), "no water wasted on a watered tile");
        }

        [Test]
        public void Crops_GrowOnlyOnWateredDays_ThenHarvest_AndSellOvernight()
        {
            var (w, b) = Blank();
            var cell = new Cell(6, 5);
            var tile = w.Map[cell];

            w.RunFullDay(); // nobody waters
            Assert.AreEqual(0, tile.Growth);

            for (int day = 0; day < Crops.Parsnip.DaysToGrow; day++)
            {
                tile.Watered = true;
                Assert.IsFalse(tile.IsRipe);
                w.RunFullDay();
                Assert.IsFalse(tile.Watered, "soil dries overnight");
            }
            Assert.IsTrue(tile.IsRipe);

            Teleport(b, 6, 5);
            Assert.AreEqual(CommandResult.Working, w.Execute(b, Command.Harvest));
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Harvest));
            Assert.AreEqual(1, b.Load.Get(Item.Parsnip));
            Assert.IsFalse(tile.HasCrop);
            Assert.IsTrue(tile.Tilled, "ready to replant");

            Teleport(b, 12, 12); // next to the bin
            Assert.AreEqual(CommandResult.Ok, w.Execute(b, Command.Ship));
            Assert.AreEqual(1, w.Bin.Get(Item.Parsnip));
            int gold = w.Gold;
            var report = w.RunFullDay();
            Assert.AreEqual(Crops.Parsnip.SellPrice, report.GoldEarned);
            Assert.AreEqual(gold + Crops.Parsnip.SellPrice, w.Gold);
            Assert.AreEqual(Crops.Parsnip.SellPrice, w.Earned);
        }

        [Test]
        public void Harvest_FailsWhenTheLoadIsTooHeavy()
        {
            var (w, b) = Blank();
            var tile = w.Map[new Cell(6, 5)];
            tile.Growth = Crops.Parsnip.DaysToGrow;
            b.Load.Add(Item.Water, Balance.BotCapacity);
            Teleport(b, 6, 5);
            Assert.AreEqual(CommandResult.LoadFull, w.Execute(b, Command.Harvest));
            b.Load.Remove(Item.Water, 1);
            Assert.AreEqual(CommandResult.Working, w.Execute(b, Command.Harvest), "room for one parsnip now");
        }

        // ------------------------------------------------------------------ the starter routine

        [Test]
        public void Tutorial_WatersAllNineParsnips()
        {
            var w = Farm();
            var report = w.RunFullDay();
            Assert.AreEqual(9, report.CropsWatered);
            Assert.AreEqual(0, report.CropsDry);
            Assert.AreEqual(0, report.Problems);
        }

        [Test]
        public void Tutorial_ParsnipsRipenOnDayFour()
        {
            var w = Farm();
            for (int i = 0; i < Crops.Parsnip.DaysToGrow; i++) w.RunFullDay();
            Assert.AreEqual(4, w.Day);
            Assert.AreEqual(9, w.Map.AllCells().Count(c => w.Map[c].IsRipe));
        }

        [Test]
        public void Tracer_TimingMatchesTheRealDay()
        {
            var w = Farm();
            var bot = w.Bots[0];
            var trace = RoutineTracer.Trace(w, bot);
            Assert.AreEqual(0, trace.BlockedCount);
            CollectionAssert.Contains(trace.PickedTiles, StarterFarm.ParsnipPatch);

            w.StartDay();
            int idleAt = -1;
            while (w.Phase == DayPhase.Running)
            {
                w.Step();
                if (bot.LastResult == CommandResult.Idle)
                {
                    idleAt = w.Tick;
                    break;
                }
            }
            Assert.AreEqual(trace.Ticks + 1, idleAt, "idle on the tick after the last command");
        }

        // ------------------------------------------------------------------ dusk

        [Test]
        public void Dusk_BotsWalkHome_AndEmptyTheirLoads()
        {
            var (w, b) = Blank();
            b.Routine.SetAll(new[] { Command.GoToTile(14, 4) });
            b.Load.Add(Item.Parsnip, 2);
            b.Load.Add(Item.PotatoSeeds, 3);
            b.Load.Add(Item.Water, 4);
            int duskAt = -1;
            w.DuskStarted += () => duskAt = w.Tick;

            var report = w.RunFullDay();
            Assert.AreEqual(Balance.TicksPerDay, duskAt);
            Assert.Greater(w.Tick, Balance.TicksPerDay, "it had a walk home");
            Assert.AreEqual(w.Map.Home, b.Position);
            Assert.AreEqual(b.HomeFacing, b.Facing);
            Assert.IsTrue(b.Load.IsEmpty);
            Assert.AreEqual(3, w.SeedStock.Get(Item.PotatoSeeds), "seeds go back to the shed");
            Assert.AreEqual(2, report.Sold.Get(Item.Parsnip), "produce is shipped");
        }

        // ------------------------------------------------------------------ custom commands

        [Test]
        public void Run_ExpandsInline_ForFree()
        {
            var (w, b) = Blank();
            var hop = w.Library.Create("Hop");
            hop.Routine.SetAll(new[] { Command.Step(Facing.South), Command.Step(Facing.South) });
            b.Routine.SetAll(new[] { Command.Run(hop), Command.Wait });

            w.StartDay();
            w.Step();
            Assert.AreEqual(new Cell(8, 10), b.Position);
            Assert.AreEqual(hop.Id, b.LastPath[1].MacroId);
            w.Step();
            Assert.AreEqual(new Cell(8, 9), b.Position);
            w.Step();
            Assert.AreEqual(1, b.LastIndexIn(-1), "third tick is the Wait");
        }

        [Test]
        public void Library_PreventsSelfRuns_AndDeleteStripsRuns()
        {
            var w = Farm();
            var a = w.Library.Create("A");
            var b = w.Library.Create("B");
            b.Routine.Add(Command.Run(a));
            Assert.IsFalse(w.Library.CanRunFrom(a, a.Id));
            Assert.IsFalse(w.Library.CanRunFrom(a, b.Id), "A -> B -> A");
            Assert.IsTrue(w.Library.CanRunFrom(null, a.Id));

            var bot = w.Bots[0];
            bot.Routine.Add(Command.Run(a));
            w.DeleteMacro(a);
            Assert.IsFalse(bot.Routine.Runs(a.Id));
            Assert.AreEqual(0, b.Routine.Count);
        }

        [Test]
        public void SaveAsCommand_CopiesTheRoutine()
        {
            var w = Farm();
            var bot = w.Bots[0];
            var saved = w.Library.Create("Morning", bot.Routine);
            CollectionAssert.AreEqual(bot.Routine, saved.Routine);
            bot.Routine.Clear();
            Assert.AreEqual(12, saved.Routine.Count);
        }

        [Test]
        public void Commands_CycleTheirOptions()
        {
            Assert.AreEqual(GoTarget.Tile, Command.GoTo(GoTarget.Well).WithNextOption().Target);
            Assert.AreEqual(Facing.South, Command.Step(Facing.East).WithNextOption().Dir);
            Assert.AreEqual(Item.ParsnipSeeds, Command.Grab(Item.Water, 3).WithNextOption().Item);
            Assert.AreEqual(Item.ParsnipSeeds, Command.Plant().WithNextOption().Item);
            Assert.AreEqual(Balance.MaxGrab, Command.Grab(Item.Water, 3).WithCount(999).Count);
            Assert.AreEqual(1, Command.Grab(Item.Water, 3).WithCount(0).Count);
        }

        // ------------------------------------------------------------------ preview

        [Test]
        public void Preview_MatchesTheRealDay_AndLeavesItUntouched()
        {
            var w = Farm();
            var bot = w.Bots[0];
            var preview = DayPreview.Run(w);

            Assert.AreEqual(DayPhase.Planning, w.Phase);
            Assert.IsFalse(w.Map.AllCells().Any(c => w.Map[c].Watered), "the real farm wasn't watered");
            Assert.AreEqual(9, preview.TasksDone(bot.Id, CommandType.Water));
            Assert.AreEqual(9, preview.Night.CropsWatered);

            w.StartDay();
            while (w.Phase != DayPhase.Planning)
            {
                w.Step();
                if (w.Phase == DayPhase.Planning) break;
                Assert.IsTrue(preview.TryGetFrame(bot.Id, w.Tick, out var f));
                Assert.AreEqual(bot.Position, f.Position, $"tick {w.Tick}");
                Assert.AreEqual(bot.Load.Weight, f.LoadWeight, $"tick {w.Tick}");
                Assert.AreEqual(bot.LastResult, f.Result, $"tick {w.Tick}");
            }
            Assert.AreEqual(w.Tick, preview.EndTick);
        }

        [Test]
        public void Preview_FromMidDay_ContinuesFromHere()
        {
            var w = Farm();
            var bot = w.Bots[0];
            w.StartDay();
            for (int i = 0; i < 20; i++) w.Step();
            var preview = DayPreview.Run(w);
            Assert.AreEqual(20, preview.StartTick);
            for (int t = 21; t <= 60; t++)
            {
                w.Step();
                preview.TryGetFrame(bot.Id, t, out var f);
                Assert.AreEqual(bot.Position, f.Position, $"tick {t}");
            }
        }

        // ------------------------------------------------------------------ economy & season

        [Test]
        public void Shop_SeedsBotsAndCapacity()
        {
            var w = Farm();
            w.AddGold(1000);
            Assert.IsTrue(w.TryBuySeeds(Crops.Potato, 4));
            Assert.AreEqual(4, w.SeedStock.Get(Item.PotatoSeeds));
            int gold = w.Gold;
            Assert.IsNotNull(w.TryBuyBot());
            Assert.AreEqual(2, w.Bots.Count);
            Assert.AreEqual(gold - Balance.BotCost(1), w.Gold);
            Assert.IsTrue(w.TryUpgradeCapacity());
            Assert.IsTrue(w.Bots.All(b => b.Capacity == Balance.BotCapacity + Balance.CapacityUpgrade));
        }

        [Test]
        public void Season_EndsWithAnOutcome()
        {
            var w = Farm();
            Outcome result = Outcome.None;
            w.SeasonOver += o => result = o;
            for (int i = 0; i < Balance.SeasonDays; i++) w.RunFullDay();
            Assert.IsTrue(w.IsGameOver);
            Assert.AreEqual(Outcome.GoalMissed, result, "the starter routine alone doesn't hit the goal");
            w.StartDay();
            Assert.AreEqual(DayPhase.Planning, w.Phase, "no days after the season");
        }
    }
}
