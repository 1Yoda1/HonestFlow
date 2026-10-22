namespace HonestFlow.Application.Lm
{
    public enum LmDatabaseRestoreStatus
    {
        Success,
        BackupNotFound,
        DownloadFailed,
        InstallerUnavailable,
        InstallationUnavailable,
        Cancelled,
        Denied,
        Failed
    }

    public sealed class LmDatabaseRestoreResult
    {
        private LmDatabaseRestoreResult(LmDatabaseRestoreStatus status, string userMessage)
        {
            Status = status;
            UserMessage = userMessage ?? string.Empty;
        }

        public LmDatabaseRestoreStatus Status { get; }
        public string UserMessage { get; }
        public bool IsSuccess => Status == LmDatabaseRestoreStatus.Success;

        public static LmDatabaseRestoreResult Success() =>
            new(LmDatabaseRestoreStatus.Success, "База ЛМ ЧЗ восстановлена.");

        public static LmDatabaseRestoreResult BackupNotFound(string message) =>
            new(LmDatabaseRestoreStatus.BackupNotFound, message);

        public static LmDatabaseRestoreResult DownloadFailed() =>
            new(LmDatabaseRestoreStatus.DownloadFailed,
                "Не удалось получить резервную базу ЛМ ЧЗ. Подробности записаны в журнал.");

        public static LmDatabaseRestoreResult InstallerUnavailable(string message) =>
            new(LmDatabaseRestoreStatus.InstallerUnavailable, message);

        public static LmDatabaseRestoreResult InstallationUnavailable(string message) =>
            new(LmDatabaseRestoreStatus.InstallationUnavailable, message);

        public static LmDatabaseRestoreResult Cancelled() =>
            new(LmDatabaseRestoreStatus.Cancelled, "Восстановление базы ЛМ ЧЗ отменено.");

        public static LmDatabaseRestoreResult Denied(string message) =>
            new(LmDatabaseRestoreStatus.Denied, message);

        public static LmDatabaseRestoreResult Failed() =>
            new(LmDatabaseRestoreStatus.Failed,
                "Не удалось восстановить базу ЛМ ЧЗ. Подробности записаны в журнал.");
    }
}
