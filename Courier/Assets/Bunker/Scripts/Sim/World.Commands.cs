using System;
using System.Collections.Generic;

namespace Bunker.Sim
{
    // Command execution. Every job works on the tile in FRONT of the bot (Step and Face set which way
    // that is). Grab needs the stream (water) or the hatch (seeds) ahead; Deliver needs the hatch ahead.
    public sealed partial class World
    {
        readonly Dictionary<long, int[,]> _distanceCache = new Dictionary<long, int[,]>();

        /// <summary>A bot finished a job successfully: (bot, job, the tile it worked on).</summary>
        public event Action<Bot, CommandType, Cell> TaskDone;

        /// <summary>A bot cleared a tile: (bot, the tile, what came off it).</summary>
        public event Action<Bot, Cell, List<ItemAmount>> Salvaged;

        /// <summary>The tile a bot is facing.</summary>
        public static Cell Ahead(Bot b) => b.Position.Step(b.Facing);

        void RunNextCommand(Bot b)
        {
            b.PreviousPosition = b.Position;
            b.PreviousFacing = b.Facing;

            if (!b.TryFetchNext(Library, out var cmd, out var error))
            {
                b.LastResult = error == CommandResult.None ? CommandResult.Idle : error;
                if (CommandInfo.IsProblem(b.LastResult)) b.ProblemsToday++;
                return;
            }

            b.LastResult = Execute(b, cmd, out bool finished);
            if (!finished) b.RepeatCurrent(); // still walking or working: same line next tick
            if (CommandInfo.IsProblem(b.LastResult)) b.ProblemsToday++;
        }

        /// <summary>One tick of a command. Public so tests can drive bots directly.</summary>
        public CommandResult Execute(Bot b, Command cmd) => Execute(b, cmd, out _);

        /// <summary>
        /// One tick of a (non-Run) command. <paramref name="finished"/> is false when it needs more ticks
        /// (walking somewhere, or a job that takes several ticks).
        /// </summary>
        public CommandResult Execute(Bot b, Command cmd, out bool finished)
        {
            finished = true;
            if (!b.CanDo(cmd)) return CommandResult.CantDo; // takes the tick, then the routine moves on
            switch (cmd.Type)
            {
                case CommandType.GoTo: return GoTo(b, cmd, out finished);
                case CommandType.Step: return StepBot(b, cmd.Dir, out finished);
                case CommandType.Face:
                    b.Facing = cmd.Dir;
                    return CommandResult.Ok;
                case CommandType.Wait: return CommandResult.Ok;
                case CommandType.Clear:
                case CommandType.Till:
                case CommandType.Plant:
                case CommandType.Water:
                case CommandType.Harvest:
                case CommandType.Grab:
                case CommandType.Deliver:
                case CommandType.Dump:
                    return Timed(b, cmd, out finished);
                default: return CommandResult.None;
            }
        }

        int JobTicks(Bot b, Command cmd) =>
            cmd.Type == CommandType.Clear ? Balance.ClearTicks(DebrisAhead(b)) : CommandInfo.Duration(cmd.Type);

        CommandResult Check(Bot b, Command cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.Clear: return CheckClear(b);
                case CommandType.Till: return CheckTill(b);
                case CommandType.Plant: return CheckPlant(b, cmd, out _);
                case CommandType.Water: return CheckWater(b);
                case CommandType.Harvest: return CheckHarvest(b);
                case CommandType.Grab: return CheckGrab(b, cmd);
                case CommandType.Deliver: return CheckDeliver(b);
                case CommandType.Dump: return CheckDump(b);
                default: return CommandResult.None;
            }
        }

        void Apply(Bot b, Command cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.Clear: DoClear(b); break;
                case CommandType.Till: DoTill(b); break;
                case CommandType.Plant: DoPlant(b, cmd); break;
                case CommandType.Water: DoWater(b); break;
                case CommandType.Harvest: DoHarvest(b); break;
                case CommandType.Grab: DoGrab(b, cmd); break;
                case CommandType.Deliver: DoDeliver(b); break;
                case CommandType.Dump: DoDump(b); break;
            }
        }

        /// <summary>
        /// Runs a job that takes several ticks: checks it can start, works, then checks again and applies
        /// the effect on the last tick (another bot may have beaten it to it).
        /// </summary>
        CommandResult Timed(Bot b, Command cmd, out bool finished)
        {
            finished = true;

            if (b.WorkLeft == 0)
            {
                int duration = Math.Max(1, JobTicks(b, cmd));
                var pre = Check(b, cmd);
                if (pre != CommandResult.Ok) return pre;
                if (duration > 1)
                {
                    b.WorkLeft = duration - 1;
                    finished = false;
                    return CommandResult.Working;
                }
            }
            else
            {
                b.WorkLeft--;
                if (b.WorkLeft > 0)
                {
                    finished = false;
                    return CommandResult.Working;
                }
                var pre = Check(b, cmd);
                if (pre != CommandResult.Ok) return pre;
            }

            var target = Ahead(b);
            Apply(b, cmd);
            TaskDone?.Invoke(b, cmd.Type, target);
            return CommandResult.Ok;
        }

        // ------------------------------------------------------------------ what E would do

        /// <summary>
        /// The obvious job for the tile ahead, used by your "use" key: clear overgrowth, till clear soil,
        /// plant tilled soil (<paramref name="seed"/>, None = any), water or harvest a crop, fill up at
        /// the stream, deliver at the hatch (or take seeds when there's nothing to deliver).
        /// </summary>
        public Command SuggestUse(Bot b, Item seed = Item.None)
        {
            var ahead = Ahead(b);
            var t = Map.TileAt(ahead);
            if (t == null) return Command.Clear;
            if (t.Structure == Structure.Hatch)
                return HasDeliverables(b) ? Command.Deliver : Command.Grab(seed, Balance.SeedsPerGrab);
            if (t.IsWater) return Command.Grab(Item.Water, Balance.MaxGrab);
            if (t.IsOvergrown) return Command.Clear;
            if (t.HasCrop) return t.IsRipe ? Command.Harvest : Command.Water;
            if (t.Tilled) return Command.Plant(seed);
            if (t.IsFarmland) return Command.Till;
            return Command.Clear;
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Ticks this bot takes to walk one tile (see <see cref="BotStats.TicksPerTile"/>).</summary>
        public static int TicksPerTile(Bot b) => BotKinds.Stats(b.Kind).TicksPerTile;

        // Walking a tile: the bot moves onto it on the first tick (so it can be drawn gliding across),
        // then spends the rest of its ticks-per-tile getting there (WorkLeft counts them down).

        CommandResult StepBot(Bot b, Facing dir, out bool finished)
        {
            finished = true;
            if (b.WorkLeft > 0)
            {
                b.WorkLeft--;
                finished = b.WorkLeft == 0;
                return finished ? CommandResult.Ok : CommandResult.Moving;
            }
            b.Facing = dir;
            var next = b.Position.Step(dir);
            if (!Map.IsWalkable(next)) return CommandResult.Blocked;
            b.Position = next;
            return BeginWalk(b, out finished) ? CommandResult.Moving : CommandResult.Ok;
        }

        /// <summary>Starts the rest of a tile's walk. False if the bot is so quick it's already there.</summary>
        static bool BeginWalk(Bot b, out bool finished)
        {
            int n = TicksPerTile(b);
            finished = n <= 1;
            if (finished) return false;
            b.WorkLeft = n - 1;
            return true;
        }

        CommandResult GoTo(Bot b, Command cmd, out bool finished)
        {
            finished = true;
            var dist = DistanceTo(cmd.Target, cmd.Tile);
            if (!Map.InBounds(b.Position)) return CommandResult.NoPath;
            int d = dist[b.Position.X, b.Position.Y];

            if (b.WorkLeft > 0)
            {
                // Still crossing the tile it just stepped onto.
                b.WorkLeft--;
                finished = b.WorkLeft == 0 && d == 0;
                if (finished) FaceTarget(b, cmd.Target);
                return finished ? CommandResult.Ok : CommandResult.Moving;
            }

            if (d == 0)
            {
                FaceTarget(b, cmd.Target); // already there
                return CommandResult.Ok;
            }
            if (d < 0 || !Pathing.NextStep(Map, dist, b.Position, out var next, out var dir)) return CommandResult.NoPath;

            b.Facing = dir;
            b.Position = next;
            if (BeginWalk(b, out _))
            {
                finished = false;
                return CommandResult.Moving;
            }
            finished = d - 1 == 0;
            if (finished) FaceTarget(b, cmd.Target);
            return CommandResult.Ok;
        }

        /// <summary>Arriving at the stream or the hatch turns the bot to face it.</summary>
        void FaceTarget(Bot b, GoTarget target)
        {
            if (target != GoTarget.Tile && Pathing.TryFace(Map, b.Position, target, out var dir)) b.Facing = dir;
        }

        /// <summary>Walking distance field for a Go to target (cached until the ground changes).</summary>
        public int[,] DistanceTo(GoTarget target, Cell tile)
        {
            long key = target == GoTarget.Tile ? ((long)(tile.X + 1) << 32) | (uint)(tile.Y + 1) : -(long)target - 1;
            if (_distanceCache.TryGetValue(key, out var cached)) return cached;
            var field = Pathing.DistanceField(Map, Pathing.Targets(Map, target, tile));
            _distanceCache[key] = field;
            return field;
        }


        // ------------------------------------------------------------------ jobs

        Debris DebrisAhead(Bot b) => Map.TileAt(Ahead(b))?.Debris ?? Debris.None;

        CommandResult CheckClear(Bot b)
        {
            var cell = Ahead(b);
            var t = Map.TileAt(cell);
            if (t == null || !t.IsOvergrown) return CommandResult.NothingToClear;
            if (b.FreeWeight < Balance.YieldWeight(t.Debris, cell)) return CommandResult.LoadFull;
            return CommandResult.Ok;
        }

        void DoClear(Bot b)
        {
            var cell = Ahead(b);
            var t = Map[cell];
            var yield = Balance.Yield(t.Debris, cell);
            foreach (var a in yield) b.Load.Add(a.Item, a.Amount);
            _tilesClearedToday++;
            RemoveDebris(cell);
            Salvaged?.Invoke(b, cell, yield);
        }

        /// <summary>Clears a tile with no yield (tests, tools). Routes are re-planned since the ground changed.</summary>
        internal void RemoveDebris(Cell cell)
        {
            if (!Map.InBounds(cell)) return;
            Map[cell].Debris = Debris.None;
            _distanceCache.Clear(); // a new tile is walkable
            RaiseTileChanged(cell);
            GroundChanged?.Invoke(cell);
        }

        CommandResult CheckTill(Bot b)
        {
            var t = Map.TileAt(Ahead(b));
            if (t == null) return CommandResult.NotSoil;
            if (t.IsOvergrown) return CommandResult.Overgrown;
            if (!t.IsFarmland) return CommandResult.NotSoil;
            return t.Tilled ? CommandResult.AlreadyDone : CommandResult.Ok;
        }

        void DoTill(Bot b)
        {
            var cell = Ahead(b);
            Map[cell].Tilled = true;
            RaiseTileChanged(cell);
        }

        CommandResult CheckPlant(Bot b, Command cmd, out Item seed)
        {
            seed = Item.None;
            var t = Map.TileAt(Ahead(b));
            if (t != null && t.IsOvergrown) return CommandResult.Overgrown;
            if (t == null || !t.IsFarmland || !t.Tilled) return CommandResult.NotTilled;
            if (t.HasCrop) return CommandResult.AlreadyDone;

            if (cmd.Item != Item.None)
            {
                if (b.Load.Get(cmd.Item) <= 0) return CommandResult.NoSeeds;
                seed = cmd.Item;
                return CommandResult.Ok;
            }
            foreach (var crop in Crops.All)
            {
                if (b.Load.Get(crop.Seed) > 0)
                {
                    seed = crop.Seed;
                    return CommandResult.Ok;
                }
            }
            return CommandResult.NoSeeds;
        }

        void DoPlant(Bot b, Command cmd)
        {
            CheckPlant(b, cmd, out var seed);
            b.Load.Remove(seed, 1);
            var cell = Ahead(b);
            var t = Map[cell];
            t.Crop = Crops.ForSeed(seed);
            t.Growth = 0;
            RaiseTileChanged(cell);
        }

        /// <summary>
        /// Waters tilled soil (planted or not). Soil that still has more than half its water is
        /// "already watered", so looping water bots don't pour away their tank.
        /// </summary>
        CommandResult CheckWater(Bot b)
        {
            var t = Map.TileAt(Ahead(b));
            if (t == null || !t.IsFarmland || !t.Tilled) return t != null && t.IsOvergrown ? CommandResult.Overgrown : CommandResult.NotTilled;
            if (t.WellWatered) return CommandResult.AlreadyDone;
            if (b.Load.Get(Item.Water) <= 0) return CommandResult.NoWater;
            return CommandResult.Ok;
        }

        void DoWater(Bot b)
        {
            var cell = Ahead(b);
            b.Load.Remove(Item.Water, 1);
            Map[cell].Moisture = Crops.WaterLastsTicks;
            RaiseTileChanged(cell);
        }

        CommandResult CheckHarvest(Bot b)
        {
            var t = Map.TileAt(Ahead(b));
            if (t == null || !t.HasCrop) return CommandResult.NothingHere;
            if (!t.IsRipe) return CommandResult.NotRipe;
            if (b.FreeWeight < ItemInfo.Weight(t.Crop.Produce)) return CommandResult.LoadFull;
            return CommandResult.Ok;
        }

        void DoHarvest(Bot b)
        {
            var cell = Ahead(b);
            var t = Map[cell];
            b.Load.Add(t.Crop.Produce, 1);
            t.Crop = null;
            t.Growth = 0;
            RaiseTileChanged(cell);
        }

        /// <summary>Water from the stream; seeds from the hatch (Item None = some of every seed in stock).</summary>
        CommandResult CheckGrab(Bot b, Command cmd)
        {
            var ahead = Ahead(b);
            if (cmd.Item == Item.Water)
            {
                if (!Map.IsWater(ahead)) return CommandResult.NotAtStream;
                if (b.FreeWeight >= ItemInfo.Weight(Item.Water)) return CommandResult.Ok;
                // A full tank isn't a mistake (the soil was still wet last time round); full of junk is.
                return b.Load.Get(Item.Water) > 0 ? CommandResult.AlreadyDone : CommandResult.LoadFull;
            }
            if (!Map.IsHatch(ahead)) return CommandResult.NotAtHatch;
            if (cmd.Item == Item.None)
            {
                foreach (var crop in Crops.All)
                    if (Stores.Get(crop.Seed) > 0) return CommandResult.Ok;
                return CommandResult.OutOfStock;
            }
            return Stores.Get(cmd.Item) > 0 ? CommandResult.Ok : CommandResult.OutOfStock;
        }

        void DoGrab(Bot b, Command cmd)
        {
            int want = Math.Max(1, cmd.Count);
            if (cmd.Item == Item.Water)
            {
                b.Load.Add(Item.Water, Math.Min(want, b.FreeWeight / ItemInfo.Weight(Item.Water)));
                return;
            }
            if (cmd.Item == Item.None)
            {
                foreach (var crop in Crops.All) b.Load.Add(crop.Seed, Stores.Remove(crop.Seed, want));
                return;
            }
            b.Load.Add(cmd.Item, Stores.Remove(cmd.Item, want));
        }

        /// <summary>Food and materials (not seeds or water) get handed in at the hatch.</summary>
        static bool IsDeliverable(Item i) => ItemInfo.IsFood(i) || ItemInfo.IsMaterial(i);

        public static bool HasDeliverables(Bot b)
        {
            foreach (var a in b.Load.ToList())
                if (IsDeliverable(a.Item)) return true;
            return false;
        }

        CommandResult CheckDeliver(Bot b)
        {
            if (!Map.IsHatch(Ahead(b))) return CommandResult.NotAtHatch;
            return HasDeliverables(b) ? CommandResult.Ok : CommandResult.NothingToDeliver;
        }

        void DoDeliver(Bot b)
        {
            foreach (var a in b.Load.ToList())
            {
                if (!IsDeliverable(a.Item)) continue;
                b.Load.Remove(a.Item, a.Amount);
                HandIn(b, a.Item, a.Amount);
            }
        }

        /// <summary>Water (any fluid) can be poured out anywhere, to make room in the load.</summary>
        static CommandResult CheckDump(Bot b)
        {
            foreach (var a in b.Load.ToList())
                if (ItemInfo.IsFluid(a.Item)) return CommandResult.Ok;
            return CommandResult.NothingToDump;
        }

        static void DoDump(Bot b)
        {
            foreach (var a in b.Load.ToList())
                if (ItemInfo.IsFluid(a.Item)) b.Load.Remove(a.Item, a.Amount);
        }
    }
}
