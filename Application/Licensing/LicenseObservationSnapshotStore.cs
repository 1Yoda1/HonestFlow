using System.Threading;

namespace HonestFlow.Application.Licensing
{
    public sealed class LicenseObservationSnapshotStore : ILicenseObservationSnapshotStore
    {
        private LicenseObservationSnapshot _current;

        public static LicenseObservationSnapshotStore Instance { get; } = new();

        public event System.Action<LicenseObservationSnapshot> SnapshotChanged;

        public LicenseObservationSnapshot Current => Volatile.Read(ref _current);

        public void Set(LicenseObservationSnapshot snapshot)
        {
            Interlocked.Exchange(ref _current, snapshot);
            SnapshotChanged?.Invoke(snapshot);
        }
    }
}
