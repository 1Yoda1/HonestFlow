namespace HonestFlow.Application.PointStatus
{
    public sealed class PointStatusResult
    {
        public NodeStatus Lm { get; set; }
        public NodeStatus Controller { get; set; }
        public NodeStatus Esm { get; set; }
        public NodeStatus Kkt { get; set; }
        public NodeStatus Cloud { get; set; }
    }
}
