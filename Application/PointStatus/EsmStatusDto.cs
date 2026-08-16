using Newtonsoft.Json;

namespace HonestFlow.Application.PointStatus
{
    public sealed class EsmStatusDto
    {
        [JsonProperty("software")]
        public EsmSoftwareStatusDto Software { get; set; }

        [JsonProperty("data")]
        public EsmStatusDto Data { get; set; }

        [JsonProperty("lmController")]
        public EsmComponentStatus LmController { get; set; }

        [JsonProperty("clientSoftware")]
        public EsmComponentStatus ClientSoftware { get; set; }

        [JsonProperty("gismt")]
        public EsmComponentStatus Gismt { get; set; }

        [JsonProperty("lm")]
        public EsmComponentStatus Lm { get; set; }

        [JsonIgnore]
        public EsmLmInfoDto LmInfo { get; set; }
    }

    public sealed class EsmComponentStatus
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("lastConnection")]
        public string LastConnection { get; set; }
    }

    public sealed class EsmSoftwareStatusDto
    {
        [JsonProperty("data")]
        public EsmStatusDto Data { get; set; }
    }

    public sealed class EsmLmInfoDto
    {
        [JsonProperty("controllerVersion")]
        public string ControllerVersion { get; set; }

        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("lmStatus")]
        public EsmLmStatusDto LmStatus { get; set; }
    }

    public sealed class EsmLmStatusDto
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("operationMode")]
        public string OperationMode { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("lastSync")]
        public long? LastSync { get; set; }
    }
}
