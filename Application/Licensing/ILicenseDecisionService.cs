namespace HonestFlow.Application.Licensing
{
    public interface ILicenseDecisionService
    {
        LicenseDecisionResult Decide(LicenseDecisionContext context);
    }
}
