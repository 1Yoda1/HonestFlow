namespace HonestFlow.Application.Licensing
{
    public interface ILicenseObservationSnapshotStore
    {
        event System.Action<LicenseObservationSnapshot> SnapshotChanged;
        LicenseObservationSnapshot Current { get; }
        void Set(LicenseObservationSnapshot snapshot);
    }
}
