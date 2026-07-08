namespace HonestFlow.Application.Diagnostics
{
    public sealed class DiagnosticLogSelection
    {
        public bool IncludeSystemInfo { get; set; } = true;
        public bool IncludeHonestFlow { get; set; } = true;
        public bool IncludeLm { get; set; } = true;
        public bool IncludeEsm { get; set; } = true;
        public bool IncludeKkt { get; set; } = true;

        public static DiagnosticLogSelection Full() => new();

        public bool HasAnySelection =>
            IncludeSystemInfo ||
            IncludeHonestFlow ||
            IncludeLm ||
            IncludeEsm ||
            IncludeKkt;
    }
}
