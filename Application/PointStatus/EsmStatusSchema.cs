namespace HonestFlow.Application.PointStatus
{
    public static class EsmStatusSchema
    {
        public static bool TryRecognize(EsmStatusDto response, out string schemaName)
        {
            if (HasKnownStatus(response?.Software?.Data))
            {
                schemaName = "software.data";
                return true;
            }

            if (HasKnownStatus(response?.Data?.Software?.Data))
            {
                schemaName = "data.software.data";
                return true;
            }

            if (HasKnownStatus(response?.Data))
            {
                schemaName = "data";
                return true;
            }

            if (HasKnownStatus(response))
            {
                schemaName = "flat";
                return true;
            }

            schemaName = null;
            return false;
        }

        private static bool HasKnownStatus(EsmStatusDto status) =>
            HasStatusCode(status?.LmController) ||
            HasStatusCode(status?.ClientSoftware) ||
            HasStatusCode(status?.Gismt) ||
            HasStatusCode(status?.Lm);

        private static bool HasStatusCode(EsmComponentStatus component) =>
            component?.Code.HasValue == true;
    }
}
