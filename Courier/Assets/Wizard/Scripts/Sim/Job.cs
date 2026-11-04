using System.Collections.Generic;

namespace Wizard.Sim
{
    public enum JobKind
    {
        /// <summary>Bring goods to the villager's cottage.</summary>
        Deliver,

        /// <summary>Collect the villager's parcels and bring them to the tower.</summary>
        Collect,
    }

    public enum JobState
    {
        Open,
        Done,
        Failed,
    }

    /// <summary>An odd job from the village job board.</summary>
    public sealed class Job
    {
        public int Id { get; }
        public JobKind Kind { get; }
        public Building Giver { get; }
        public Resource Resource { get; }
        public int Amount { get; }
        public int Progress { get; internal set; }

        /// <summary>The job must be finished by the end of this day.</summary>
        public int DueDay { get; }

        public IReadOnlyList<ResourceAmount> Reward { get; }
        public JobState State { get; internal set; }

        public int Remaining => Amount - Progress;
        public bool IsOpen => State == JobState.Open;

        internal Job(int id, JobKind kind, Building giver, Resource resource, int amount, int dueDay, IReadOnlyList<ResourceAmount> reward)
        {
            Id = id;
            Kind = kind;
            Giver = giver;
            Resource = resource;
            Amount = amount;
            DueDay = dueDay;
            Reward = reward;
        }

        public string Title
        {
            get
            {
                string who = Giver.Owner ?? Giver.Name;
                return Kind == JobKind.Deliver
                    ? $"{who} wants {Amount} {Resource}"
                    : $"{who} needs {Amount} {Resource}s taken to the tower";
            }
        }

        public override string ToString() => $"{Title} ({Progress}/{Amount}, due day {DueDay}) [{State}]";
    }
}
