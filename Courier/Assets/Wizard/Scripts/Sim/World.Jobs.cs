using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    // The village job board: odd jobs are how the wizard earns gold to prepare for the doom.
    public sealed partial class World
    {
        readonly Random _rng;
        readonly List<Job> _jobs = new List<Job>();
        readonly List<Job> _doneToday = new List<Job>();
        int _nextJobId = 1;

        /// <summary>Every job ever posted, oldest first. Filter by <see cref="Job.State"/>.</summary>
        public IReadOnlyList<Job> Jobs => _jobs;

        public int JobsCompleted { get; private set; }
        public int JobsFailed { get; private set; }

        public event Action<Job> JobPosted;

        /// <summary>Raised when a job is completed or fails.</summary>
        public event Action<Job> JobFinished;

        public IEnumerable<Job> OpenJobs
        {
            get
            {
                foreach (var j in _jobs)
                    if (j.IsOpen) yield return j;
            }
        }

        public int OpenJobCount
        {
            get
            {
                int n = 0;
                foreach (var j in _jobs)
                    if (j.IsOpen) n++;
                return n;
            }
        }

        /// <summary>Posts a specific job (level setup, tests). Collect jobs put the parcels at the giver's door.</summary>
        public Job PostJob(JobKind kind, Building giver, Resource resource, int amount, int dueDay, int goldReward)
        {
            var reward = new[] { new ResourceAmount(Resource.Gold, goldReward) };
            var job = new Job(_nextJobId++, kind, giver, resource, amount, dueDay, reward);
            _jobs.Add(job);
            if (kind == JobKind.Collect) giver.Output.Add(resource, amount);
            JobPosted?.Invoke(job);
            return job;
        }

        /// <summary>Morning job board: tops the board up with new requests from villagers who are free.</summary>
        void PostJobs()
        {
            int toPost = Math.Min(Balance.JobsPerDay, Balance.MaxOpenJobs - OpenJobCount);
            for (int i = 0; i < toPost; i++)
            {
                var giver = PickFreeCottage();
                if (giver == null) return;

                var template = PickTemplate();
                int amount = _rng.Next(Balance.JobMinAmount, Balance.JobMaxAmount + 1);
                int due = Math.Min(Day + _rng.Next(1, 3), Doom.Day - 1);
                int gold = amount * template.Value + Balance.JobBaseReward;
                PostJob(template.Kind, giver, template.Resource, amount, due, gold);
            }
        }

        Building PickFreeCottage()
        {
            var free = new List<Building>();
            foreach (var b in _buildings)
            {
                if (!b.Type.IsCottage) continue;
                bool busy = false;
                foreach (var j in _jobs)
                    if (j.IsOpen && j.Giver == b) busy = true;
                if (!busy) free.Add(b);
            }
            return free.Count == 0 ? null : free[_rng.Next(free.Count)];
        }

        Balance.JobTemplate PickTemplate()
        {
            // Only ask for goods the village can actually make (parcels are always possible).
            var options = new List<Balance.JobTemplate>();
            foreach (var t in Balance.JobTemplates)
                if (t.Kind == JobKind.Collect || AnyBuildingMakes(t.Resource)) options.Add(t);
            return options[_rng.Next(options.Count)];
        }

        bool AnyBuildingMakes(Resource r)
        {
            foreach (var b in _buildings)
                foreach (var o in b.Type.Outputs)
                    if (o.Resource == r) return true;
            return false;
        }

        int DeliverySpace(Building cottage, Resource r)
        {
            int space = 0;
            foreach (var j in _jobs)
                if (j.IsOpen && j.Kind == JobKind.Deliver && j.Giver == cottage && j.Resource == r)
                    space += j.Remaining;
            return space;
        }

        void CreditDeliverJobs(Building cottage, Resource r, int amount)
        {
            foreach (var j in _jobs)
            {
                if (amount <= 0) break;
                if (!j.IsOpen || j.Kind != JobKind.Deliver || j.Giver != cottage || j.Resource != r) continue;
                int used = Math.Min(amount, j.Remaining);
                j.Progress += used;
                amount -= used;
                if (j.Remaining == 0) Complete(j);
            }
        }

        void CreditCollectJobs(Resource r, int amount)
        {
            foreach (var j in _jobs)
            {
                if (amount <= 0) break;
                if (!j.IsOpen || j.Kind != JobKind.Collect || j.Resource != r) continue;
                int used = Math.Min(amount, j.Remaining);
                j.Progress += used;
                amount -= used;
                if (j.Remaining == 0) Complete(j);
            }
        }

        void Complete(Job j)
        {
            j.State = JobState.Done;
            JobsCompleted++;
            foreach (var r in j.Reward) Stockpile.Add(r.Resource, r.Amount);
            _doneToday.Add(j);
            JobFinished?.Invoke(j);
        }

        void ExpireJobs(int endedDay, List<Job> failed)
        {
            foreach (var j in _jobs)
            {
                if (!j.IsOpen || j.DueDay > endedDay) continue;
                j.State = JobState.Failed;
                JobsFailed++;
                failed.Add(j);
                JobFinished?.Invoke(j);
            }
        }
    }
}
