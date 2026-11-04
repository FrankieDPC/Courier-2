using System.Linq;
using NUnit.Framework;
using Wizard.Sim;

namespace Wizard.Tests
{
    public class WorldSimTests
    {
        /// <summary>Small test map: unlocked cells 3..9, tower at (4,4), depot (3,3) facing north.</summary>
        static World EmptyWorld(DoomPlan doom = null)
        {
            var grid = new TownGrid(4, 4, 2, 1, 1, 2, 2);
            doom = doom ?? new DoomPlan(10, new[] { Facing.North }, new Wave[0]);
            var world = new World(grid, doom, 7) { Depot = new Cell(3, 3), DepotFacing = Facing.North };
            world.AddBuilding(Catalogue.Tower, new Cell(4, 4));
            return world;
        }

        // ------------------------------------------------------------------ movement & goods

        [Test]
        public void Construct_MovesOnRoads_AndIsBlockedByLots()
        {
            var w = EmptyWorld();
            var c = w.AddConstruct();
            Assert.AreEqual(CommandResult.Ok, w.Execute(c, Command.Move));
            Assert.AreEqual(new Cell(3, 4), c.Position);
            w.Execute(c, Command.Right);
            Assert.AreEqual(CommandResult.Blocked, w.Execute(c, Command.Move));
        }

        [Test]
        public void Forge_TurnsCrystalIntoTwoCharges()
        {
            var w = EmptyWorld();
            var forge = w.AddBuilding(Catalogue.ArcaneForge, new Cell(5, 4));
            forge.Input.Add(Resource.Crystal, 1);
            w.StartDay();
            for (int i = 0; i < Catalogue.ArcaneForge.CycleTicks; i++) w.Step();
            Assert.AreEqual(2, forge.Output.Get(Resource.Charge));
        }

        [Test]
        public void PickUp_And_DropOff_AtTower()
        {
            var w = EmptyWorld();
            var quarry = w.AddBuilding(Catalogue.Quarry, new Cell(4, 5));
            quarry.Output.Add(Resource.Stone, 5);
            var c = w.AddConstruct();
            w.Execute(c, Command.Move);
            w.Execute(c, Command.Move); // (3,5), quarry on the right
            Assert.AreEqual(CommandResult.Ok, w.Execute(c, Command.PickUp(InteractSide.Right)));
            Assert.AreEqual(3, c.CargoCount);
            w.Execute(c, Command.UTurn);
            w.Execute(c, Command.Move); // (3,4), tower on the left (east when facing south)
            Assert.AreEqual(CommandResult.Ok, w.Execute(c, Command.DropOff(InteractSide.Left)));
            Assert.AreEqual(3, w.Stockpile.Get(Resource.Stone));
        }

        // ------------------------------------------------------------------ spells

        [Test]
        public void Cast_RunsSpellInline_AndCostsNoExtraTick()
        {
            var w = EmptyWorld();
            var hop = w.Spellbook.Create("Hop");
            hop.Routine.SetAll(new[] { Command.Move, Command.Move });
            var c = w.AddConstruct();
            c.Loop = false;
            c.Routine.SetAll(new[] { Command.Cast(hop), Command.Right });

            w.StartDay();
            w.Step();
            Assert.AreEqual(new Cell(3, 4), c.Position, "first tick runs the spell's first command");
            Assert.AreEqual(2, c.LastPath.Count);
            Assert.AreEqual(0, c.LastPath[0].Index);
            Assert.AreEqual(hop.Id, c.LastPath[1].SpellId);
            w.Step();
            Assert.AreEqual(new Cell(3, 5), c.Position);
            w.Step();
            Assert.AreEqual(Facing.East, c.Facing, "third tick is the command after the cast");
            Assert.AreEqual(1, c.LastIndexIn(-1));
            w.Step();
            Assert.AreEqual(CommandResult.Idle, c.LastResult);
        }

        [Test]
        public void Spells_CanCastSpells()
        {
            var w = EmptyWorld();
            var step = w.Spellbook.Create("Step");
            step.Routine.SetAll(new[] { Command.Move });
            var twice = w.Spellbook.Create("Twice");
            twice.Routine.SetAll(new[] { Command.Cast(step), Command.Cast(step) });
            var c = w.AddConstruct();
            c.Routine.SetAll(new[] { Command.Cast(twice), Command.UTurn });

            var trace = RoutineTracer.Trace(w, c);
            Assert.AreEqual(3, trace.Ticks);
            Assert.AreEqual(new Cell(3, 5), trace.End);

            w.StartDay();
            w.Step();
            Assert.AreEqual(3, c.LastPath.Count, "main -> Twice -> Step");
            w.Step();
            w.Step();
            Assert.AreEqual(Facing.South, c.Facing);
        }

        [Test]
        public void EditingASpell_ChangesEveryConstructThatCastsIt()
        {
            var w = EmptyWorld();
            var hop = w.Spellbook.Create("Hop");
            hop.Routine.SetAll(new[] { Command.Move });
            var a = w.AddConstruct();
            var b = w.AddConstruct();
            a.Routine.SetAll(new[] { Command.Cast(hop) });
            b.Routine.SetAll(new[] { Command.Cast(hop) });

            hop.Routine.Add(Command.Move);
            Assert.AreEqual(new Cell(3, 5), RoutineTracer.Trace(w, a).End);
            Assert.AreEqual(new Cell(3, 5), RoutineTracer.Trace(w, b).End);
        }

        [Test]
        public void Spellbook_PreventsSpellsCastingThemselves()
        {
            var book = new Spellbook();
            var a = book.Create("A");
            var b = book.Create("B");
            b.Routine.Add(Command.Cast(a));

            Assert.IsFalse(book.CanCastFrom(a, a.Id), "direct self-cast");
            Assert.IsFalse(book.CanCastFrom(a, b.Id), "A -> B -> A");
            Assert.IsTrue(book.CanCastFrom(b, a.Id));
            Assert.IsTrue(book.CanCastFrom(null, a.Id), "constructs can cast anything");
        }

        [Test]
        public void RunawayRecursion_IsCaught()
        {
            var w = EmptyWorld();
            var a = w.Spellbook.Create("A");
            a.Routine.Add(Command.Cast(a)); // bypassing the editor's guard
            var c = w.AddConstruct();
            c.Routine.SetAll(new[] { Command.Cast(a) });
            w.StartDay();
            w.Step();
            Assert.AreEqual(CommandResult.SpellTooDeep, c.LastResult);
            Assert.IsTrue(RoutineTracer.Trace(w, c).HitDepthLimit);
        }

        [Test]
        public void DeletingASpell_RemovesItsCasts()
        {
            var w = EmptyWorld();
            var hop = w.Spellbook.Create("Hop");
            var other = w.Spellbook.Create("Other");
            other.Routine.SetAll(new[] { Command.Cast(hop), Command.Wait });
            var c = w.AddConstruct();
            c.Routine.SetAll(new[] { Command.Cast(hop), Command.Move });

            w.DeleteSpell(hop);
            Assert.IsNull(w.Spellbook.Get(hop.Id));
            CollectionAssert.AreEqual(new[] { Command.Move }, c.Routine);
            CollectionAssert.AreEqual(new[] { Command.Wait }, other.Routine);
        }

        [Test]
        public void Routine_MoveToInsertionPoint()
        {
            var s = new Routine();
            s.SetAll(new[] { Command.Move, Command.Left, Command.Right, Command.Wait });
            s.MoveToInsertionPoint(0, 3);
            CollectionAssert.AreEqual(new[] { Command.Left, Command.Right, Command.Move, Command.Wait }, s);
            s.MoveToInsertionPoint(3, 0);
            CollectionAssert.AreEqual(new[] { Command.Wait, Command.Left, Command.Right, Command.Move }, s);
        }

        // ------------------------------------------------------------------ jobs

        [Test]
        public void DeliverJob_CompletesAndPaysGold()
        {
            var w = EmptyWorld();
            var cottage = w.AddBuilding(Catalogue.Cottage, new Cell(4, 5), "Ada");
            var job = w.PostJob(JobKind.Deliver, cottage, Resource.Bread, 2, dueDay: 3, goldReward: 9);
            var c = w.AddConstruct();
            c.AddCargo(Resource.Bread, 3);
            w.Execute(c, Command.Move);
            w.Execute(c, Command.Move); // (3,5), cottage on the right

            Assert.AreEqual(CommandResult.Ok, w.Execute(c, Command.DropOff(InteractSide.Right)));
            Assert.AreEqual(JobState.Done, job.State);
            Assert.AreEqual(1, c.CargoCount, "the cottage only takes what the job needs");
            Assert.AreEqual(9, w.Stockpile.Get(Resource.Gold));
            Assert.AreEqual(CommandResult.NotAccepted, w.Execute(c, Command.DropOff(InteractSide.Right)));
        }

        [Test]
        public void CollectJob_PutsParcelsOut_AndCompletesAtTheTower()
        {
            var w = EmptyWorld();
            var cottage = w.AddBuilding(Catalogue.Cottage, new Cell(4, 5), "Ada");
            var job = w.PostJob(JobKind.Collect, cottage, Resource.Parcel, 2, dueDay: 3, goldReward: 5);
            Assert.AreEqual(2, cottage.Output.Get(Resource.Parcel));

            var c = w.AddConstruct();
            w.Execute(c, Command.Move);
            w.Execute(c, Command.Move);
            w.Execute(c, Command.PickUp(InteractSide.Right));
            w.Execute(c, Command.UTurn);
            w.Execute(c, Command.Move);
            w.Execute(c, Command.DropOff(InteractSide.Left)); // tower
            Assert.AreEqual(JobState.Done, job.State);
            Assert.AreEqual(5, w.Stockpile.Get(Resource.Gold));
        }

        [Test]
        public void Jobs_ExpireAfterTheirDueDay_AndTheBoardRefills()
        {
            var w = EmptyWorld();
            w.AddBuilding(Catalogue.Cottage, new Cell(4, 5), "Ada");
            w.AddBuilding(Catalogue.Cottage, new Cell(5, 5), "Bo");
            var cottage = w.Buildings.First(b => b.Owner == "Ada");
            var job = w.PostJob(JobKind.Deliver, cottage, Resource.Bread, 2, dueDay: 1, goldReward: 5);

            var report = w.RunFullDay();
            Assert.AreEqual(JobState.Failed, job.State);
            CollectionAssert.Contains(report.JobsFailed, job);
            Assert.Greater(w.OpenJobCount, 0, "new jobs are posted for the next morning");
            Assert.IsTrue(w.OpenJobs.All(j => j.DueDay >= w.Day));
        }

        [Test]
        public void StarterWorld_TutorialRoutine_FinishesMabelsJob()
        {
            var w = StarterWorld.Create();
            var mabelJob = w.Jobs.First(j => j.Kind == JobKind.Deliver);
            w.RunFullDay();
            Assert.AreEqual(JobState.Done, mabelJob.State);
            Assert.GreaterOrEqual(w.Stockpile.Get(Resource.Gold), 16);
            Assert.IsTrue(RoutineTracer.Trace(w, w.Constructs[0]).ReturnsToStart);
        }

        // ------------------------------------------------------------------ day preview

        [Test]
        public void Preview_MatchesTheRealDayExactly()
        {
            var w = StarterWorld.Create();
            var bramble = w.Constructs[0];
            var preview = DayPreview.Run(w);

            Assert.AreEqual(0, preview.StartTick);
            Assert.AreEqual(Balance.TicksPerDay, preview.EndTick);
            Assert.AreEqual(Balance.TicksPerDay + 1, preview.FramesFor(bramble.Id).Count, "one frame per tick plus the start");

            // Play the real day and compare tick by tick.
            w.StartDay();
            for (int t = 1; t <= Balance.TicksPerDay - 1; t++)
            {
                w.Step();
                Assert.IsTrue(preview.TryGetFrame(bramble.Id, t, out var f));
                Assert.AreEqual(bramble.Position, f.Position, $"tick {t}");
                Assert.AreEqual(bramble.CargoCount, f.CargoCount, $"tick {t}");
                Assert.AreEqual(bramble.LastResult, f.Result, $"tick {t}");
            }
            w.Step();
            var done = w.Jobs.Where(j => j.State == JobState.Done).Select(j => j.Title).ToList();
            CollectionAssert.AreEqual(done, preview.JobsDone.Select(j => j.Title).ToList());
        }

        [Test]
        public void Preview_DoesNotTouchTheRealWorld()
        {
            var w = StarterWorld.Create();
            int gold = w.Stockpile.Get(Resource.Gold);
            var bakery = w.BuildingAt(StarterWorld.BakeryCell);
            DayPreview.Run(w);

            Assert.AreEqual(DayPhase.Planning, w.Phase);
            Assert.AreEqual(1, w.Day);
            Assert.AreEqual(0, w.Tick);
            Assert.AreEqual(gold, w.Stockpile.Get(Resource.Gold));
            Assert.AreEqual(0, bakery.Output.Get(Resource.Bread));
            Assert.IsTrue(w.OpenJobs.All(j => j.Progress == 0));
            Assert.AreEqual(w.Depot, w.Constructs[0].Position);
        }

        [Test]
        public void Preview_ReportsDeliveriesAndGroupedProblems()
        {
            var w = StarterWorld.Create();
            var bramble = w.Constructs[0];
            var preview = DayPreview.Run(w);

            var deliveries = preview.DeliveriesFor(bramble.Id);
            Assert.IsTrue(deliveries.Any(d => d.Resource == Resource.Grain && d.Target == "Bakery"));
            Assert.IsTrue(deliveries.Any(d => d.Resource == Resource.Bread && d.Target == "Mabel's Cottage"));

            var problems = preview.ProblemsFor(bramble.Id);
            Assert.IsNotEmpty(problems, "the tutorial routine has idle pick-ups early on");
            Assert.IsTrue(problems.All(p => p.Count >= 1 && p.FirstTick >= 1));
            for (int i = 1; i < problems.Count; i++) Assert.GreaterOrEqual(problems[i - 1].Count, problems[i].Count);
        }

        [Test]
        public void Preview_FromAPausedMidDay_StartsWhereWeAre()
        {
            var w = StarterWorld.Create();
            var bramble = w.Constructs[0];
            w.StartDay();
            for (int i = 0; i < 25; i++) w.Step(); // bramble is mid-routine, possibly inside a spell

            var preview = DayPreview.Run(w);
            Assert.AreEqual(25, preview.StartTick);
            Assert.IsTrue(preview.TryGetFrame(bramble.Id, 25, out var start));
            Assert.AreEqual(bramble.Position, start.Position);

            for (int t = 26; t <= 60; t++)
            {
                w.Step();
                preview.TryGetFrame(bramble.Id, t, out var f);
                Assert.AreEqual(bramble.Position, f.Position, $"tick {t}");
                Assert.AreEqual(bramble.Facing, f.Facing, $"tick {t}");
            }
        }

        [Test]
        public void Preview_SeesSpellEditsImmediately()
        {
            var w = StarterWorld.Create();
            var bramble = w.Constructs[0];
            var before = DayPreview.Run(w);
            w.Spellbook.Spells[0].Routine.Add(Command.Wait); // "Two Steps" now takes 3 ticks
            var after = DayPreview.Run(w);
            before.TryGetFrame(bramble.Id, 3, out var b3);
            after.TryGetFrame(bramble.Id, 3, out var a3);
            Assert.AreEqual(1, b3.IndexIn(-1), "before: tick 3 is the first drop-off");
            Assert.AreEqual(CommandType.DropOff, bramble.Routine[b3.IndexIn(-1)].Type);
            Assert.AreEqual(0, a3.IndexIn(-1), "after: tick 3 is still inside the spell");
            Assert.AreEqual(2, a3.IndexIn(w.Spellbook.Spells[0].Id));
        }

        [Test]
        public void Preview_OnTheDoomDay_SimulatesTheSiegeToo()
        {
            var w = StarterWorld.Create();
            while (!w.IsDoomDay) w.RunFullDay();
            var ward = w.AddBuilding(Catalogue.Ward, new Cell(11, 14));
            ward.Input.Add(Resource.Charge, 10);
            w.Stockpile.Add(Resource.Charge, 20);

            var preview = DayPreview.Run(w);
            Assert.IsTrue(preview.IsDoomDay);
            Assert.AreEqual(10, ward.Input.Get(Resource.Charge), "the real ward kept its charges");
            Assert.AreEqual(0, w.Monsters.Count);

            w.RunFullDay();
            Assert.AreEqual(w.Tick, preview.EndTick, "the siege ends on the same tick");
        }

        // ------------------------------------------------------------------ the doom

        static World SiegeReady(DoomPlan doom)
        {
            var w = EmptyWorld(doom);
            // Skip straight to the doom day.
            while (!w.IsDoomDay) w.RunFullDay();
            return w;
        }

        [Test]
        public void Countdown_ReachesTheDoomDay()
        {
            var w = EmptyWorld();
            Assert.AreEqual(9, w.DaysUntilDoom);
            for (int i = 0; i < 9; i++) w.RunFullDay();
            Assert.IsTrue(w.IsDoomDay);
            Assert.AreEqual(0, w.DaysUntilDoom);
            Assert.AreEqual(0, w.OpenJobCount, "no new odd jobs on the doom day");
        }

        [Test]
        public void UndefendedTower_TakesDamage_AndCanFall()
        {
            var doom = new DoomPlan(10, new[] { Facing.North }, new[] { new Wave(0, Facing.North, Monsters.Brute, 8, 1) });
            var w = SiegeReady(doom);
            Outcome result = Outcome.None;
            w.GameOver += o => result = o;

            w.RunFullDay();
            Assert.AreEqual(Outcome.Defeat, result);
            Assert.AreEqual(0, w.TowerHp);
            Assert.IsTrue(w.IsGameOver);
            Assert.AreEqual(DayPhase.Planning, w.Phase);
        }

        [Test]
        public void MonstersFollowRoadsToTheTower()
        {
            var doom = new DoomPlan(10, new[] { Facing.North }, new[] { new Wave(0, Facing.North, Monsters.Shade, 1) });
            var w = SiegeReady(doom);
            w.StartDay();
            w.Step();
            var m = w.Monsters.Single();
            Assert.AreEqual(w.GateCell(Facing.North), m.Cell);
            int start = w.DistanceToTower(m.Cell);
            Assert.Greater(start, 0);
            for (int i = 0; i < 10; i++) w.Step();
            Assert.Less(w.DistanceToTower(m.Cell), start);
        }

        [Test]
        public void WardsAndTowerShootMonsters_UsingCharges()
        {
            var doom = new DoomPlan(10, new[] { Facing.North }, new[] { new Wave(0, Facing.North, Monsters.Shade, 3) });
            var w = SiegeReady(doom);
            var ward = w.AddBuilding(Catalogue.Ward, new Cell(4, 7));
            ward.Input.Add(Resource.Charge, 10);
            w.Stockpile.Add(Resource.Charge, 10);
            int shots = 0;
            w.Shot += (b, m) => shots++;

            w.RunFullDay();
            Assert.AreEqual(Outcome.Victory, w.Outcome);
            Assert.AreEqual(3, w.MonstersSlain);
            Assert.AreEqual(w.TowerMaxHp, w.TowerHp);
            Assert.AreEqual(shots, 20 - ward.Input.Get(Resource.Charge) - w.Stockpile.Get(Resource.Charge));
        }

        [Test]
        public void Potions_HealTheTower_AndFortifyRaisesMaxHp()
        {
            var doom = new DoomPlan(10, new[] { Facing.North }, new[]
            {
                new Wave(0, Facing.North, Monsters.Shade, 3, 1),
                new Wave(40, Facing.North, Monsters.Shade, 1), // keeps the siege going while potions work
            });
            var w = SiegeReady(doom);
            w.Stockpile.Add(Resource.Gold, 100);
            w.Stockpile.Add(Resource.Stone, 100);
            Assert.IsTrue(w.TryFortify());
            Assert.AreEqual(Balance.TowerBaseHp + Balance.FortifyHp, w.TowerMaxHp);

            w.Stockpile.Add(Resource.Potion, 5);
            int healed = 0, damage = 0;
            w.TowerHealed += h => healed += h;
            w.TowerDamaged += d => damage += d;
            w.RunFullDay();
            Assert.AreEqual(Outcome.Victory, w.Outcome, "shades only scratch the tower");
            Assert.AreEqual(4, damage);
            Assert.AreEqual(damage, healed);
            Assert.AreEqual(w.TowerMaxHp, w.TowerHp);
            Assert.Less(w.Stockpile.Get(Resource.Potion), 5);
        }

        [Test]
        public void StarterDoom_OverwhelmsAnUnpreparedTower()
        {
            var w = StarterWorld.Create();
            while (!w.IsDoomDay) w.RunFullDay();
            w.RunFullDay();
            Assert.AreEqual(Outcome.Defeat, w.Outcome, "doing nothing for 9 days should lose");
        }
    }
}
