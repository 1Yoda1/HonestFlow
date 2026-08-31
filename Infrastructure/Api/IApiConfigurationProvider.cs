namespace HonestFlow.Infrastructure.Api
{
    /// <summary>
    /// Provides the authenticated client's most recent configuration/current document.
    /// It keeps installation and update metadata tied to the same server response as
    /// the current client and component versions.
    /// </summary>
    public interface IApiConfigurationProvider
    {
        ApiConfigurationResponse CurrentConfiguration { get; }
    }
}
