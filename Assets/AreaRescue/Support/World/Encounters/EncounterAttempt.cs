using System;
using System.Collections.Generic;
using System.Linq;

namespace DemonWeave.Encounters
{
    public enum EncounterMemberState { Alive, Dead, Unavailable }

    /// <summary>
    /// Identity and evidence for one spawned group. No quest IDs or engine objects.
    /// A sealed roster is not an empty-list victory, and removal is not death.
    /// </summary>
    public sealed class EncounterAttempt : IDisposable
    {
        private sealed class Member
        {
            public bool Required;
            public EncounterMemberState State;
        }
        private readonly Dictionary<Guid, Member> members = new Dictionary<Guid, Member>();
        public Guid Id { get; } = Guid.NewGuid();
        public int ExpectedRequiredCount { get; }
        public bool IsSealed { get; private set; }
        public bool IsReleased { get; private set; }
        public bool IsClosed { get; private set; }
        public int RequiredCount => members.Values.Count(m => m.Required);
        public int ConfirmedDeaths => members.Values.Count(m => m.Required && m.State == EncounterMemberState.Dead);
        public bool HasUnavailableRequiredMember => members.Values.Any(m => m.Required && m.State == EncounterMemberState.Unavailable);
        public bool IsCleared => !IsClosed && IsReleased && IsSealed && RequiredCount == ExpectedRequiredCount && ConfirmedDeaths == ExpectedRequiredCount;

        public EncounterAttempt(int expectedRequiredCount)
        {
            if (expectedRequiredCount < 1) throw new ArgumentOutOfRangeException(nameof(expectedRequiredCount));
            ExpectedRequiredCount = expectedRequiredCount;
        }
        public void Register(Guid memberId, bool required = true)
        {
            if (IsClosed || IsSealed) throw new InvalidOperationException("The encounter roster is closed to registration.");
            if (memberId == Guid.Empty || members.ContainsKey(memberId)) throw new ArgumentException("Member needs a unique runtime identity.");
            if (required && RequiredCount >= ExpectedRequiredCount) throw new InvalidOperationException("Required roster exceeds its declared count.");
            members.Add(memberId, new Member { Required = required, State = EncounterMemberState.Alive });
        }
        public void Seal()
        {
            if (IsClosed) throw new ObjectDisposedException(nameof(EncounterAttempt));
            if (RequiredCount != ExpectedRequiredCount || HasUnavailableRequiredMember) throw new InvalidOperationException("Required encounter members are not ready.");
            IsSealed = true;
        }
        public bool Release()
        {
            if (IsClosed || !IsSealed || HasUnavailableRequiredMember) return false;
            IsReleased = true; return true;
        }
        public bool ReportDeath(Guid attemptId, Guid memberId)
        {
            if (IsClosed || !IsReleased || attemptId != Id || !members.TryGetValue(memberId, out var member) || member.State != EncounterMemberState.Alive) return false;
            member.State = EncounterMemberState.Dead; return true;
        }
        public bool ReportUnavailable(Guid attemptId, Guid memberId)
        {
            if (IsClosed || attemptId != Id || !members.TryGetValue(memberId, out var member) || member.State != EncounterMemberState.Alive) return false;
            member.State = EncounterMemberState.Unavailable; return true;
        }
        public void Dispose()
        {
            if (IsClosed) return;
            IsClosed = true; IsReleased = false; members.Clear();
        }
    }
}
