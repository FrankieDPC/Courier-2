using System;
using System.Collections.Generic;

namespace Courier.Sim
{
    /// <summary>Static definition of a kind of building. Instances live in <see cref="Catalogue"/>.</summary>
    public sealed class BuildingType
    {
        public string Id { get; }
        public string DisplayName { get; }

        /// <summary>Consumed at the start of each production cycle.</summary>
        public IReadOnlyList<ResourceAmount> Inputs { get; }

        /// <summary>Added to the output buffer at the end of each production cycle.</summary>
        public IReadOnlyList<ResourceAmount> Outputs { get; }

        /// <summary>Ticks per production cycle.</summary>
        public int CycleTicks { get; }

        /// <summary>Max stored per input resource (couriers can't overfill).</summary>
        public int InputCapacity { get; }

        /// <summary>Max stored per output resource (production stalls when full).</summary>
        public int OutputCapacity { get; }

        /// <summary>Paid from the town stockpile when the player places one.</summary>
        public IReadOnlyList<ResourceAmount> BuildCost { get; }

        /// <summary>The town hall accepts every resource and its input buffer is the town stockpile.</summary>
        public bool IsTownHall { get; }

        public bool PlayerCanBuild { get; }

        public bool IsProducer => Outputs.Count > 0;

        public BuildingType(
            string id,
            string displayName,
            ResourceAmount[] inputs = null,
            ResourceAmount[] outputs = null,
            int cycleTicks = 4,
            int inputCapacity = 6,
            int outputCapacity = 6,
            ResourceAmount[] buildCost = null,
            bool isTownHall = false,
            bool playerCanBuild = true)
        {
            Id = id;
            DisplayName = displayName;
            Inputs = inputs ?? Array.Empty<ResourceAmount>();
            Outputs = outputs ?? Array.Empty<ResourceAmount>();
            CycleTicks = Math.Max(1, cycleTicks);
            InputCapacity = inputCapacity;
            OutputCapacity = outputCapacity;
            BuildCost = buildCost ?? Array.Empty<ResourceAmount>();
            IsTownHall = isTownHall;
            PlayerCanBuild = playerCanBuild;
        }

        public bool Accepts(Resource r)
        {
            if (r == Resource.None) return false;
            if (IsTownHall) return true;
            for (int i = 0; i < Inputs.Count; i++)
                if (Inputs[i].Resource == r) return true;
            return false;
        }

        public override string ToString() => DisplayName;
    }

    public enum BuildingStatus
    {
        Idle,
        Working,
        NeedsInput,
        OutputFull,
    }

    /// <summary>A placed building with input/output buffers and a production cycle.</summary>
    public sealed class Building
    {
        public int Id { get; }
        public BuildingType Type { get; }
        public Cell Cell { get; }

        /// <summary>Delivered goods waiting to be consumed. For the town hall this is the stockpile.</summary>
        public Inventory Input { get; } = new Inventory();

        /// <summary>Finished goods waiting for a courier to collect.</summary>
        public Inventory Output { get; } = new Inventory();

        public int Progress { get; private set; }
        public BuildingStatus Status { get; private set; }
        public int ProducedToday { get; private set; }

        public float CycleProgress01 => Type.IsProducer ? Progress / (float)Type.CycleTicks : 0f;

        internal Building(int id, BuildingType type, Cell cell)
        {
            Id = id;
            Type = type;
            Cell = cell;
        }

        /// <summary>How many of <paramref name="r"/> this building will take right now.</summary>
        public int FreeInputSpace(Resource r)
        {
            if (!Type.Accepts(r)) return 0;
            if (Type.IsTownHall) return int.MaxValue;
            return Math.Max(0, Type.InputCapacity - Input.Get(r));
        }

        /// <summary>Accepts up to <paramref name="amount"/>; returns how many were taken.</summary>
        internal int Receive(Resource r, int amount)
        {
            int take = Math.Min(amount, FreeInputSpace(r));
            if (take <= 0) return 0;
            Input.Add(r, take);
            return take;
        }

        /// <summary>
        /// The output a courier would collect. If the courier already holds something,
        /// only that resource is compatible.
        /// </summary>
        public Resource CollectableOutput(Resource holding)
        {
            if (holding != Resource.None) return Output.Get(holding) > 0 ? holding : Resource.None;

            Resource best = Resource.None;
            int bestCount = 0;
            for (int i = 0; i < Type.Outputs.Count; i++)
            {
                var r = Type.Outputs[i].Resource;
                int n = Output.Get(r);
                if (n > bestCount)
                {
                    best = r;
                    bestCount = n;
                }
            }
            return best;
        }

        internal int Collect(Resource r, int max) => Output.Remove(r, max);

        internal void Tick()
        {
            if (!Type.IsProducer)
            {
                Status = BuildingStatus.Idle;
                return;
            }

            if (!OutputHasRoom())
            {
                Status = BuildingStatus.OutputFull;
                return;
            }

            if (Progress == 0)
            {
                // Inputs are consumed when a cycle starts.
                if (!Input.TryRemove(Type.Inputs))
                {
                    Status = BuildingStatus.NeedsInput;
                    return;
                }
            }

            Progress++;
            Status = BuildingStatus.Working;

            if (Progress >= Type.CycleTicks)
            {
                Progress = 0;
                for (int i = 0; i < Type.Outputs.Count; i++)
                {
                    Output.Add(Type.Outputs[i].Resource, Type.Outputs[i].Amount);
                    ProducedToday += Type.Outputs[i].Amount;
                }
            }
        }

        internal void ResetDailyStats() => ProducedToday = 0;

        bool OutputHasRoom()
        {
            for (int i = 0; i < Type.Outputs.Count; i++)
            {
                var o = Type.Outputs[i];
                if (Output.Get(o.Resource) + o.Amount > Type.OutputCapacity) return false;
            }
            return true;
        }

        public override string ToString() => $"{Type.DisplayName} #{Id} {Cell}";
    }
}
