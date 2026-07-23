namespace HonestFlow.Models.Licensing
{
    public sealed class OperatorDevice
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;
        public string Comment { get; set; }
    }
}
