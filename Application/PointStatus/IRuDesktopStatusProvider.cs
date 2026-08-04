using System.Threading.Tasks;
using HonestFlow.Application.RemoteAccess;

namespace HonestFlow.Application.PointStatus
{
    public interface IRuDesktopStatusProvider
    {
        Task<RuDesktopStatus> GetStatus();
    }
}
