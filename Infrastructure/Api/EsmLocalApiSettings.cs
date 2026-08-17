using System;
using System.IO;
using Newtonsoft.Json;

namespace HonestFlow.Infrastructure.Api
{
    internal static class EsmLocalApiSettings
    {
        public static int? TryReadPort(string settingsPath)
        {
            try
            {
                if (!File.Exists(settingsPath))
                    return null;

                var settings = JsonConvert.DeserializeObject<EsmGuiSettings>(File.ReadAllText(settingsPath));
                return settings?.Port is > 0 and <= 65535 ? settings.Port : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (JsonException) { return null; }
        }

        private sealed class EsmGuiSettings
        {
            [JsonProperty("port")]
            public int? Port { get; set; }
        }
    }
}
