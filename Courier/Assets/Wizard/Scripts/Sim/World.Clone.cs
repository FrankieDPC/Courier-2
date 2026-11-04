namespace Wizard.Sim
{
    // Deep copy for "what if" simulation (the day preview). Ids are preserved so callers can match
    // buildings, constructs, spells and jobs between the copy and the real world.
    public sealed partial class World
    {
        /// <summary>
        /// An independent copy of the whole simulation, mid-day state included. Changing or stepping
        /// the copy never touches this world. Event subscribers are not copied. Future job postings use
        /// a separate random sequence (the real world's isn't touched), which only matters for days
        /// after the copied one.
        /// </summary>
        public World Clone()
        {
            var w = new World(Grid.Clone(), Doom, 7919 * Day + Tick)
            {
                Depot = Depot,
                DepotFacing = DepotFacing,
                TicksPerDay = TicksPerDay,
                Day = Day,
                Tick = Tick,
                Phase = Phase,
                TowerMaxHp = TowerMaxHp,
                TowerHp = TowerHp,
                FortifyLevel = FortifyLevel,
                ExpansionLevel = ExpansionLevel,
                Outcome = Outcome,
                JobsCompleted = JobsCompleted,
                JobsFailed = JobsFailed,
                MonstersSlain = MonstersSlain,
                MonstersArrived = MonstersArrived,
            };

            w.Spellbook.CopyFrom(Spellbook);

            foreach (var b in _buildings)
            {
                var copy = new Building(b.Id, b.Type, b.Cell);
                copy.CopyStateFrom(b);
                w._buildings.Add(copy);
                w._buildingAt[copy.Cell] = copy;
                if (b == Tower) w.Tower = copy;
            }
            w._nextBuildingId = _nextBuildingId;

            foreach (var c in _constructs)
            {
                var copy = new Construct(c.Id, c.Name, c.Home, c.HomeFacing, c.Capacity);
                copy.CopyStateFrom(c, w.Spellbook);
                w._constructs.Add(copy);
            }
            w._nextConstructId = _nextConstructId;

            foreach (var j in _jobs)
            {
                var copy = new Job(j.Id, j.Kind, w.BuildingAt(j.Giver.Cell), j.Resource, j.Amount, j.DueDay, j.Reward)
                {
                    Progress = j.Progress,
                    State = j.State,
                };
                w._jobs.Add(copy);
                if (_doneToday.Contains(j)) w._doneToday.Add(copy);
            }
            w._nextJobId = _nextJobId;
            w.DeliveredToday.CopyFrom(DeliveredToday);

            foreach (var m in _monsters)
            {
                w._monsters.Add(new Monster(m.Id, m.Type, m.Cell)
                {
                    PreviousCell = m.PreviousCell,
                    Hp = m.Hp,
                    MoveTimer = m.MoveTimer,
                });
            }
            w._spawnQueue.AddRange(_spawnQueue);
            w._nextSpawn = _nextSpawn;
            w._nextMonsterId = _nextMonsterId;
            w._healTimer = _healTimer;
            w._distanceDirty = true;
            return w;
        }
    }
}
