namespace HonestFlow.Infrastructure.Api
{
    public interface IApiClientAccessStateProvider
    {
        bool? LicensePolicyEnabled { get; }
        string ClientId { get; }
        string ClientName { get; }
    }
}
