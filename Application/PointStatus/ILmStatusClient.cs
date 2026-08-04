using System.Threading.Tasks;
using HonestFlow.Models;

namespace HonestFlow.Application.PointStatus
{
    public interface ILmStatusClient
    {
        Task<ApiResponse<LmStatus>> GetStatus();
    }
}
