using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HonestFlow.Models.Licensing
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum LicenseFeature
    {
        Diagnostics,
        SendLogs,
        Install,
        Repair,
        AutoFix,
        ManualTools
    }
}
