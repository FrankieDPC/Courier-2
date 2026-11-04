using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    public enum BuildingRole
    {
        /// <summary>Makes outputs from inputs on a cycle (farms, workshops).</summary>
        Producer,

        /// <summary>The wizard's tower: the stockpile, the depot, and the thing the doom wants to flatten.</summary>
        Tower,

        /// <summary>A villager's home. Posts odd jobs and accepts deliveries for them.</summary>
        Cottage,

        /// <summary>A defensive pylon. Fires at monsters during the siege, spending its ammo.</summary>
        Ward,
    }

    /// <summary>Static definition of a kind of building. Instances live in <see cref="Catalogue"/>.</summary>
    public sealed class BuildingType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public BuildingRole Role { get; }

        /// <summary>Consumed at the start of each production cycle.</summary>
        public IReadOnlyList<ResourceAmount> Inputs { get; }

        /// <summary>Added to the output buffer at the end of each production cycle.</summary>
        public IReadOnlyList<ResourceAmount> Outputs { get; }

        public int CycleTicks { get; }
        public int InputCapacity { get; }
        public int OutputCapacity { get; }
        public IReadOnlyList<ResourceAmount> BuildCost { get; }
        public bool PlayerCanBuild { get; }

        /// <summary>Defence stats (wards; the tower uses Balance.Tower*).</summary>
        public DefenceStats Defence { get; }

        public bool IsTower => Role == BuildingRole.Tower;
        public bool IsCottage => Role == BuildingRole.Cottage;
        public bool IsWard => Role == BuildingRole.Ward;
        public bool IsProducer => Role == BuildingRole.Producer && Outputs.Count > 0;

        public BuildingType(
            string id,
            string displayName,
            BuildingRole role = BuildingRole.Producer,
            ResourceAmount[] inputs = null,
            ResourceAmount[] outputs = null,
            int cycleTicks = 4,
            int inputCapacity = 6,
            int outputCapacity = 6,
            ResourceAmount[] buildCost = null,
            bool playerCanBuild = true,
            DefenceStats defence = null)
        {
            Id = id;
            DisplayName = displayName;
            Role = role;
            Inputs = inputs ?? Array.Empty<ResourceAmount>();
            Outputs = outputs ?? Array.Empty<ResourceAmount>();
            CycleTicks = Math.Max(1, cycleTicks);
            InputCapacity = inputCapacity;
            OutputCapacity = outputCapacity;
            BuildCost = buildCost ?? Array.Empty<ResourceAmount>();
            PlayerCanBuild = playerCanBuild;
            Defence = defence;
        }

        /// <summary>Static acceptance. Cottages also depend on their open jobs (see World).</summary>
        public bool Accepts(Resource r)
        {
            if (r == Resource.None || r == Resource.Gold) return false;
            if (IsTower) return true;
            if (IsWard) return Defence != null && r == Defence.Ammo;
            for (int i = 0; i < Inputs.Count; i++)
                if (Inputs[i].Resource == r) return true;
            return false;
        }

        public override string ToString() => DisplayName;
    }

    public sealed class DefenceStats
    {
        public readonly Resource Ammo;
        public readonly int Range;    // Chebyshev distance in cells
        public readonly int Damage;
        public readonly int Cooldown; // ticks between shots

        public DefenceStats(Resource ammo, int range, int damage, int cooldown)
        {
            Ammo = ammo;
            Range = range;
            Damage = damage;
            Cooldown = cooldown;
        }
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

        /// <summary>For cottages: whose house it is ("Mabel").</summary>
        public string Owner { get; internal set; }

        public string Name => Owner != null ? $"{Owner}'s {Type.DisplayName}" : Type.DisplayName;

        /// <summary>Delivered goods. For the tower this is the town stockpile; for wards, their ammo.</summary>
        public Inventory Input { get; } = new Inventory();

        /// <summary>Goods waiting for a construct to collect.</summary>
        public Inventory Output { get; } = new Inventory();

        public int Progress { get; private set; }
        public BuildingStatus Status { get; private set; }
        public int ProducedToday { get; private set; }

        /// <summary>Ticks until a defence can fire again.</summary>
        public int Cooldown { get; internal set; }

        public float CycleProgress01 => Type.IsProducer ? Progress / (float)Type.CycleTicks : 0f;

        internal Building(int id, BuildingType type, Cell cell)
        {
            Id = id;
            Type = type;
            Cell = cell;
        }

        /// <summary>Capacity-based space for producers and wards. Cottages/tower are handled by World.</summary>
        internal int BufferSpace(Resource r)
        {
            if (!Type.Accepts(r)) return 0;
            if (Type.IsTower) return int.MaxValue;
            return Math.Max(0, Type.InputCapacity - Input.Get(r));
        }

        /// <summary>
        /// The output a construct would collect. If the construct already holds something, only that
        /// resource is compatible.
        /// </summary>
        public Resource CollectableOutput(Resource holding)
        {
            if (holding != Resource.None) return Output.Get(holding) > 0 ? holding : Resource.None;

            Resource best = Resource.None;
            int bestCount = 0;
            foreach (var amount in Output.ToList())
            {
                if (amount.Amount > bestCount)
                {
                    best = amount.Resource;
                    bestCount = amount.Amount;
                }
            }
            return best;
        }

        internal int Collect(Resource r, int max) => Output.Remove(r, max);

        internal void Produce()
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

            if (Progress == 0 && !Input.TryRemove(Type.Inputs))
            {
                Status = BuildingStatus.NeedsInput;
                return;
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

        internal void CopyStateFrom(Building o)
        {
            Owner = o.Owner;
            Input.CopyFrom(o.Input);
            Output.CopyFrom(o.Output);
            Progress = o.Progress;
            Status = o.Status;
            ProducedToday = o.ProducedToday;
            Cooldown = o.Cooldown;
        }

        bool OutputHasRoom()
        {
            for (int i = 0; i < Type.Outputs.Count; i++)
            {
                var o = Type.Outputs[i];
                if (Output.Get(o.Resource) + o.Amount > Type.OutputCapacity) return false;
            }
            return true;
        }

        public override string ToString() => $"{Name} #{Id} {Cell}";
    }
}
