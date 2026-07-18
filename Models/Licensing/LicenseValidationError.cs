namespace HonestFlow.Models.Licensing
{
    public sealed class LicenseValidationError
    {
        public LicenseValidationError(string path, string message)
        {
            Path = path;
            Message = message;
        }

        public string Path { get; }
        public string Message { get; }
    }
}
