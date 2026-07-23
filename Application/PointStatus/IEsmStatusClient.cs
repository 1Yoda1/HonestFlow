using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus
{
    public interface IEsmStatusClient
    {
        Task<EsmStatusResult> GetStatusAsync(CancellationToken cancellationToken);
        Task<EsmCashRegisterResult> GetCashRegisterStatusAsync(CancellationToken cancellationToken);
        Task<EsmRegistrationResult> GetRegistrationStatusAsync(CancellationToken cancellationToken);
    }
}
