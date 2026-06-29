using System;
using System.IO;

namespace HonestFlow.Services.Diagnostics
{
    public sealed class AnyDeskIdProvider
    {
        private const string SystemConfigPath = @"C:\ProgramData\AnyDesk\system.conf";
        private const string IdKey = "ad.anynet.id=";

        public string GetId()
        {
            try
            {
                if (!File.Exists(SystemConfigPath))
                    return "не найден";

                foreach (string line in File.ReadLines(SystemConfigPath))
                {
                    string trimmed = line.Trim();
                    if (!trimmed.StartsWith(IdKey, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = trimmed.Substring(IdKey.Length).Trim();
                    return string.IsNullOrWhiteSpace(value) ? "не найден" : value;
                }

                return "не найден";
            }
            catch (Exception ex)
            {
                return $"ошибка чтения: {ex.Message}";
            }
        }
    }
}
