using Courier.Sim;
using NUnit.Framework;

namespace Courier.Tests
{
    public class TownSimTests
    {
        static Town EmptyTown()
        {
            var grid = new TownGrid(4, 4, 2, 1, 1, 2, 2); // unlocked cells 3..9
            var town = new Town(grid) { Depot = new Cell(3, 3), DepotFacing = Facing.North };
            town.AddBuilding(Catalogue.TownHall, new Cell(4, 4));
            return town;
        }

        [Test]
        public void Grid_RoadsEveryPitch_AndLotsBetween()
        {
            var g = new TownGrid(4, 4, 2, 0, 0, 3, 3);
            Assert.AreEqual(CellKind.Road, g.KindAt(new Cell(0, 5)));
            Assert.AreEqual(CellKind.Road, g.KindAt(new Cell(3, 1)));
            Assert.AreEqual(CellKind.Lot, g.KindAt(new Cell(1, 1)));
            Assert.AreEqual(CellKind.Lot, g.KindAt(new Cell(5, 4)));
            Assert.AreEqual(13, g.Width);
        }

        [Test]
        public void Bot_MovesOnRoads_AndIsBlockedByLots()
        {
            var town = EmptyTown();
            var bot = town.AddBot();

            Assert.AreEqual(CommandResult.Ok, town.Execute(bot, Command.Move));
            Assert.AreEqual(new Cell(3, 4), bot.Position);

            town.Execute(bot, Command.Right); // facing east, into lot (4,4)
            Assert.AreEqual(CommandResult.Blocked, town.Execute(bot, Command.Move));
            Assert.AreEqual(new Cell(3, 4), bot.Position);
        }

        [Test]
        public void Bot_CannotDriveIntoLockedArea_UntilExpanded()
        {
            var town = EmptyTown();
            var bot = town.AddBot();
            town.Execute(bot, Command.Left); // facing west, (2,3) is locked road
            Assert.AreEqual(CommandResult.Blocked, town.Execute(bot, Command.Move));

            town.Stockpile.Add(Resource.Planks, 100);
            town.Stockpile.Add(Resource.Stone, 100);
            town.Stockpile.Add(Resource.Bread, 100);
            Assert.IsTrue(town.TryExpand());
            Assert.AreEqual(CommandResult.Ok, town.Execute(bot, Command.Move));
        }

        [Test]
        public void Turns_WrapAround()
        {
            Assert.AreEqual(Facing.West, Facing.North.TurnLeft());
            Assert.AreEqual(Facing.North, Facing.West.TurnRight());
            Assert.AreEqual(Facing.South, Facing.North.Reverse());
        }

        [Test]
        public void Sawmill_ConsumesLogs_ProducesPlanks()
        {
            var town = EmptyTown();
            var mill = town.AddBuilding(Catalogue.Sawmill, new Cell(5, 4));
            town.AddBot(); // so StartDay has something to reset
            mill.Input.Add(Resource.Logs, 2);

            town.StartDay();
            for (int i = 0; i < Catalogue.Sawmill.CycleTicks; i++) town.Step();

            Assert.AreEqual(1, mill.Output.Get(Resource.Planks));
            Assert.AreEqual(1, mill.Input.Get(Resource.Logs), "the second log waits for the next cycle");
        }

        [Test]
        public void Producer_StallsWhenOutputFull()
        {
            var town = EmptyTown();
            var wood = town.AddBuilding(Catalogue.Woodcutter, new Cell(5, 4));
            town.TicksPerDay = 1000;
            town.StartDay();
            for (int i = 0; i < 200; i++) town.Step();
            Assert.AreEqual(Catalogue.Woodcutter.OutputCapacity, wood.Output.Get(Resource.Logs));
            Assert.AreEqual(BuildingStatus.OutputFull, wood.Status);
        }

        [Test]
        public void PickUp_And_DropOff_RespectSidesAndCapacity()
        {
            var town = EmptyTown();
            var wood = town.AddBuilding(Catalogue.Woodcutter, new Cell(4, 5)); // east of road (3,5)
            wood.Output.Add(Resource.Logs, 5);
            var bot = town.AddBot();
            town.Execute(bot, Command.Move);
            town.Execute(bot, Command.Move); // (3,5) facing north

            Assert.AreEqual(CommandResult.NoBuilding, town.Execute(bot, Command.PickUp(InteractSide.Left)));
            Assert.AreEqual(CommandResult.Ok, town.Execute(bot, Command.PickUp(InteractSide.Right)));
            Assert.AreEqual(Balance.BotCapacity, bot.CargoCount);
            Assert.AreEqual(Resource.Logs, bot.Cargo);
            Assert.AreEqual(CommandResult.CargoFull, town.Execute(bot, Command.PickUp()));

            // Town hall at (4,4) is east of (3,4).
            town.Execute(bot, Command.UTurn);
            town.Execute(bot, Command.Move);
            Assert.AreEqual(CommandResult.Ok, town.Execute(bot, Command.DropOff()));
            Assert.AreEqual(Balance.BotCapacity, town.Stockpile.Get(Resource.Logs));
            Assert.AreEqual(Balance.BotCapacity, town.DeliveredToday.Get(Resource.Logs));
            Assert.AreEqual(CommandResult.EmptyHanded, town.Execute(bot, Command.DropOff()));
        }

        [Test]
        public void DropOff_RefusesWrongCargo()
        {
            var town = EmptyTown();
            var mill = town.AddBuilding(Catalogue.Sawmill, new Cell(4, 5));
            var farm = town.AddBuilding(Catalogue.Farm, new Cell(5, 7));
            farm.Output.Add(Resource.Grain, 1);
            var bot = town.AddBot();
            town.Execute(bot, Command.Move);
            town.Execute(bot, Command.Move);
            town.Execute(bot, Command.Move); // (3,6), facing north
            town.Execute(bot, Command.Right);
            town.Execute(bot, Command.Move); // (4,6) facing east
            town.Execute(bot, Command.Move); // (5,6): farm (5,7) on the left
            Assert.AreEqual(CommandResult.Ok, town.Execute(bot, Command.PickUp()));
            Assert.AreEqual(Resource.Grain, bot.Cargo);

            town.Execute(bot, Command.UTurn);
            town.Execute(bot, Command.Move); // (4,6) facing west; sawmill (4,5) on the left
            Assert.AreEqual(CommandResult.NotAccepted, town.Execute(bot, Command.DropOff()));
            Assert.AreEqual(0, mill.Input.Get(Resource.Grain));
        }

        [Test]
        public void StarterTown_TutorialRoute_DeliversPlanks()
        {
            var town = StarterTown.Create();
            int before = town.Stockpile.Get(Resource.Planks);

            var report = town.RunFullDay();

            Assert.IsNotNull(report);
            Assert.AreEqual(1, report.Day);
            Assert.AreEqual(2, town.Day);
            Assert.AreEqual(DayPhase.Planning, town.Phase);
            Assert.Greater(report.Delivered.Get(Resource.Planks), 5);
            Assert.AreEqual(before + report.Delivered.Get(Resource.Planks), town.Stockpile.Get(Resource.Planks));
            Assert.AreEqual(town.Depot, town.Bots[0].Position, "bots return to the depot overnight");
        }

        [Test]
        public void StarterTown_TutorialRoute_ClosesItsLoop()
        {
            var town = StarterTown.Create();
            var trace = ScheduleTracer.Trace(town.Grid, town.Bots[0]);
            Assert.IsTrue(trace.ReturnsToStart);
            Assert.AreEqual(0, trace.BlockedCount);
        }

        [Test]
        public void NonLoopingBot_IdlesAfterLastCommand()
        {
            var town = EmptyTown();
            var bot = town.AddBot();
            bot.Loop = false;
            bot.Schedule.SetAll(new[] { Command.Move, Command.Move });
            town.StartDay();
            for (int i = 0; i < 5; i++) town.Step();
            Assert.AreEqual(new Cell(3, 5), bot.Position);
            Assert.AreEqual(CommandResult.Idle, bot.LastResult);
        }

        [Test]
        public void Placement_Validation()
        {
            var town = EmptyTown();
            Assert.AreEqual(PlacementCheck.NotALot, town.CheckPlacement(Catalogue.Farm, new Cell(3, 4)));
            Assert.AreEqual(PlacementCheck.Occupied, town.CheckPlacement(Catalogue.Farm, new Cell(4, 4)));
            Assert.AreEqual(PlacementCheck.Locked, town.CheckPlacement(Catalogue.Farm, new Cell(1, 1)));
            Assert.AreEqual(PlacementCheck.CannotAfford, town.CheckPlacement(Catalogue.Farm, new Cell(5, 5)));
            Assert.AreEqual(PlacementCheck.NotBuildable, town.CheckPlacement(Catalogue.TownHall, new Cell(5, 5)));

            town.Stockpile.Add(Resource.Planks, 3);
            var farm = town.TryBuild(Catalogue.Farm, new Cell(5, 5), out var result);
            Assert.AreEqual(PlacementCheck.Ok, result);
            Assert.IsNotNull(farm);
            Assert.AreEqual(0, town.Stockpile.Get(Resource.Planks));
        }

        [Test]
        public void Schedule_MoveToInsertionPoint()
        {
            var s = new Schedule();
            s.SetAll(new[] { Command.Move, Command.Left, Command.Right, Command.Wait });

            s.MoveToInsertionPoint(0, 3); // drop "Move" between Right and Wait
            CollectionAssert.AreEqual(new[] { Command.Left, Command.Right, Command.Move, Command.Wait }, s);

            s.MoveToInsertionPoint(3, 0); // drag "Wait" to the top
            CollectionAssert.AreEqual(new[] { Command.Wait, Command.Left, Command.Right, Command.Move }, s);

            s.MoveToInsertionPoint(1, 2); // drop just below itself = no change
            CollectionAssert.AreEqual(new[] { Command.Wait, Command.Left, Command.Right, Command.Move }, s);
        }

        [Test]
        public void BuyBot_SpendsStockpile()
        {
            var town = EmptyTown();
            town.AddBot();
            Assert.IsNull(town.TryBuyBot());
            town.Stockpile.Add(Resource.Planks, 5);
            Assert.IsNotNull(town.TryBuyBot());
            Assert.AreEqual(2, town.Bots.Count);
            Assert.AreEqual(0, town.Stockpile.Get(Resource.Planks));
        }
    }
}
