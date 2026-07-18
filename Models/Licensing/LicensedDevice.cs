namespace HonestFlow.Models.Licensing
{
    public sealed class LicensedDevice
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public string Comment { get; set; }
    }
}
