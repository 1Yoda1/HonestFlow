namespace HonestFlow.Application.Diagnostics
{
    public sealed class DiagnosticArchiveInfo
    {
        public DiagnosticArchiveInfo(string archivePath, string fiscalAddress)
        {
            ArchivePath = archivePath;
            FiscalAddress = fiscalAddress;
        }

        public string ArchivePath { get; }
        public string FiscalAddress { get; }
    }
}
