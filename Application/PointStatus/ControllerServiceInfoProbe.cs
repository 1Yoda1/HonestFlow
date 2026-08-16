using System;
using System.Threading;
using System.Threading.Tasks;

namespace HonestFlow.Application.PointStatus;

public interface IControllerServiceInfoProbe
{
    Task<ControllerServiceInfoResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed class ControllerServiceInfoResult
{
    private ControllerServiceInfoResult(bool isAvailable, int? httpStatusCode, string errorCategory)
    {
        IsAvailable = isAvailable;
        HttpStatusCode = httpStatusCode;
        ErrorCategory = errorCategory ?? string.Empty;
    }

    public bool IsAvailable { get; }
    public int? HttpStatusCode { get; }
    public string ErrorCategory { get; }

    public static ControllerServiceInfoResult Available(int httpStatusCode) =>
        new(true, httpStatusCode, string.Empty);

    public static ControllerServiceInfoResult Unavailable(string errorCategory, int? httpStatusCode = null) =>
        new(false, httpStatusCode, errorCategory);
}
