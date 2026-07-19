namespace HonestFlow.Application.PointIdentity
{
    public sealed class PointAddressResult
    {
        public string Address { get; set; }
        public PointAddressSource Source { get; set; }
        public bool IsAvailable => !string.IsNullOrWhiteSpace(Address);
    }
}
