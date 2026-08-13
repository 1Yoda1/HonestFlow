using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Infrastructure.Api
{
    public interface IApiSessionPersistenceController
    {
        void SetPersistSession(bool persistSession);
        Task<ApiSession> RestoreRegistrationContinuationAsync(CancellationToken cancellationToken);
        Task PersistRegistrationContinuationAsync(CancellationToken cancellationToken);
        Task ClearRegistrationContinuationAsync(CancellationToken cancellationToken);
        void PrepareForRegistrationCompletion();
    }
}
