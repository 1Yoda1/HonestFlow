using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace HonestFlow.Application.Diagnostics
{
    public sealed class DiagnosticWorkflowService
    {
        private readonly DiagnosticArchiveService _archiveService;
        private readonly DiagnosticsEmailSender _emailSender;

        public DiagnosticWorkflowService(
            DiagnosticArchiveService archiveService,
            DiagnosticsEmailSender emailSender)
        {
            _archiveService = archiveService ?? throw new ArgumentNullException(nameof(archiveService));
            _emailSender = emailSender ?? throw new ArgumentNullException(nameof(emailSender));
        }

        public Task<DiagnosticArchiveInfo> CreateArchiveAsync(
            DiagnosticLogSelection selection,
            string pointAddress,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => _archiveService.CreateArchiveInfo(selection, pointAddress),
                cancellationToken);
        }

        public Task SendArchiveAsync(
            DiagnosticArchiveInfo archive,
            Action<int, string> progress) =>
            _emailSender.SendWithRetries(archive.ArchivePath, progress, archive.FiscalAddress);

        public void SetPointStatusReport(string report) =>
            _archiveService.SetPointStatusReport(report);

        public static string DescribeSelection(DiagnosticLogSelection selection)
        {
            var groups = new List<string>();
            if (selection.IncludeSystemInfo) groups.Add("система");
            if (selection.IncludeHonestFlow) groups.Add("HonestFlow");
            if (selection.IncludeLm) groups.Add("ЛМ ЧЗ");
            if (selection.IncludeEsm) groups.Add("ЕСМ");
            if (selection.IncludeKkt) groups.Add("ККТ/АТОЛ");
            return groups.Count == 0 ? "ничего не выбрано" : string.Join(", ", groups);
        }
    }
}
