using System;
using System.Collections.Generic;

namespace Farm.Sim
{
    // Command execution: walking, fetching, farming, shipping. Tasks act on the tile the bot stands on;
    // Grab and Ship need the bot to be standing next to the well/pond, shed or bin.
    public sealed partial class World
    {
        readonly Dictionary<long, int[,]> _distanceCache = new Dictionary<long, int[,]>();

        /// <summary>A bot finished a task successfully (Till, Plant, Water, Harvest, Ship, Grab).</summary>
        public event Action<Bot, CommandType, Cell> TaskDone;

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
        /// (walking somewhere, or a task that takes several ticks).
        /// </summary>
        public CommandResult Execute(Bot b, Command cmd, out bool finished)
        {
            finished = true;
            switch (cmd.Type)
            {
                case CommandType.GoTo: return GoTo(b, cmd, out finished);
                case CommandType.Step: return StepBot(b, cmd.Dir);
                case CommandType.Wait: return CommandResult.Ok;
                case CommandType.Grab: return Timed(b, cmd, () => CheckGrab(b, cmd), () => DoGrab(b, cmd), out finished);
                case CommandType.Till: return Timed(b, cmd, () => CheckTill(b), () => DoTill(b), out finished);
                case CommandType.Plant: return Timed(b, cmd, () => CheckPlant(b, cmd, out _), () => DoPlant(b, cmd), out finished);
                case CommandType.Water: return Timed(b, cmd, () => CheckWater(b), () => DoWater(b), out finished);
                case CommandType.Harvest: return Timed(b, cmd, () => CheckHarvest(b), () => DoHarvest(b), out finished);
                case CommandType.Ship: return Timed(b, cmd, () => CheckShip(b), () => DoShip(b), out finished);
                default: return CommandResult.None;
            }
        }

        /// <summary>
        /// Runs a task that takes <see cref="CommandInfo.Duration"/> ticks: checks it can start, works,
        /// then checks again and applies the effect on the last tick (another bot may have beaten it to it).
        /// </summary>
        CommandResult Timed(Bot b, Command cmd, Func<CommandResult> check, Action apply, out bool finished)
        {
            finished = true;
            int duration = Math.Max(1, CommandInfo.Duration(cmd.Type));

            if (b.WorkLeft == 0)
            {
                var pre = check();
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
                var pre = check();
                if (pre != CommandResult.Ok) return pre;
            }

            apply();
            TaskDone?.Invoke(b, cmd.Type, b.Position);
            return CommandResult.Ok;
        }

        // ------------------------------------------------------------------ movement

        CommandResult StepBot(Bot b, Facing dir)
        {
            b.Facing = dir;
            var next = b.Position.Step(dir);
            if (!Map.IsWalkable(next)) return CommandResult.Blocked;
            b.Position = next;
            return CommandResult.Ok;
        }

        CommandResult GoTo(Bot b, Command cmd, out bool finished)
        {
            finished = true;
            var dist = DistanceTo(cmd.Target, cmd.Tile);
            if (!Map.InBounds(b.Position)) return CommandResult.NoPath;
            int d = dist[b.Position.X, b.Position.Y];
            if (d == 0) return CommandResult.Ok; // already there
            if (d < 0 || !Pathing.NextStep(Map, dist, b.Position, out var next, out var dir)) return CommandResult.NoPath;

            b.Facing = dir;
            b.Position = next;
            finished = d - 1 == 0;
            return CommandResult.Ok;
        }

        /// <summary>Walking distance field for a Go to target (cached; the farm's layout never changes).</summary>
        public int[,] DistanceTo(GoTarget target, Cell tile)
        {
            long key = target == GoTarget.Tile ? ((long)(tile.X + 1) << 32) | (uint)(tile.Y + 1) : -(long)target - 1;
            if (_distanceCache.TryGetValue(key, out var cached)) return cached;
            var field = Pathing.DistanceField(Map, Pathing.Targets(Map, target, tile));
            _distanceCache[key] = field;
            return field;
        }

        // ------------------------------------------------------------------ tasks

        CommandResult CheckGrab(Bot b, Command cmd)
        {
            bool water = cmd.Item == Item.Water;
            if (!Map.IsNextTo(b.Position, water ? Structure.Well : Structure.Shed)) return CommandResult.NotAtSource;
            if (!water && SeedStock.Get(cmd.Item) <= 0) return CommandResult.OutOfStock;
            int w = ItemInfo.Weight(cmd.Item);
            if (w > 0 && b.FreeWeight < w) return CommandResult.LoadFull;
            return CommandResult.Ok;
        }

        void DoGrab(Bot b, Command cmd)
        {
            int want = Math.Max(1, cmd.Count);
            int w = ItemInfo.Weight(cmd.Item);
            if (w > 0) want = Math.Min(want, b.FreeWeight / w);
            if (cmd.Item != Item.Water) want = SeedStock.Remove(cmd.Item, want);
            b.Load.Add(cmd.Item, want);
        }

        CommandResult CheckTill(Bot b)
        {
            var t = Map.TileAt(b.Position);
            if (t == null || !t.IsFarmland) return CommandResult.NotFarmland;
            return t.Tilled ? CommandResult.AlreadyDone : CommandResult.Ok;
        }

        void DoTill(Bot b)
        {
            Map[b.Position].Tilled = true;
            RaiseTileChanged(b.Position);
        }

        CommandResult CheckPlant(Bot b, Command cmd, out Item seed)
        {
            seed = Item.None;
            var t = Map.TileAt(b.Position);
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
            var t = Map[b.Position];
            t.Crop = Crops.ForSeed(seed);
            t.Growth = 0;
            RaiseTileChanged(b.Position);
        }

        CommandResult CheckWater(Bot b)
        {
            var t = Map.TileAt(b.Position);
            if (t == null || !t.Tilled) return t != null && t.IsFarmland ? CommandResult.NotTilled : CommandResult.NothingHere;
            if (t.Watered) return CommandResult.AlreadyDone;
            if (b.Load.Get(Item.Water) <= 0) return CommandResult.NoWater;
            return CommandResult.Ok;
        }

        void DoWater(Bot b)
        {
            b.Load.Remove(Item.Water, 1);
            Map[b.Position].Watered = true;
            RaiseTileChanged(b.Position);
        }

        CommandResult CheckHarvest(Bot b)
        {
            var t = Map.TileAt(b.Position);
            if (t == null || !t.HasCrop) return CommandResult.NothingHere;
            if (!t.IsRipe) return CommandResult.NotRipe;
            if (b.FreeWeight < ItemInfo.Weight(t.Crop.Produce)) return CommandResult.LoadFull;
            return CommandResult.Ok;
        }

        void DoHarvest(Bot b)
        {
            var t = Map[b.Position];
            b.Load.Add(t.Crop.Produce, 1);
            t.Crop = null;
            t.Growth = 0;
            RaiseTileChanged(b.Position);
        }

        CommandResult CheckShip(Bot b)
        {
            if (!Map.IsNextTo(b.Position, Structure.Bin)) return CommandResult.NotAtBin;
            foreach (var a in b.Load.ToList())
                if (ItemInfo.IsProduce(a.Item)) return CommandResult.Ok;
            return CommandResult.NothingToShip;
        }

        void DoShip(Bot b)
        {
            foreach (var a in b.Load.ToList())
            {
                if (!ItemInfo.IsProduce(a.Item)) continue;
                b.Load.Remove(a.Item, a.Amount);
                Bin.Add(a.Item, a.Amount);
                b.ShippedToday += a.Amount;
            }
        }
    }
}
