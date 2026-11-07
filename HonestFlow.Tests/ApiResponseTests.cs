using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HonestFlow.Infrastructure.Api;
using Xunit;

namespace HonestFlow.Tests;

public sealed class ApiResponseTests
{
    [Fact]
    public async Task ServiceForbidden_ProducesTypedEntitlementError()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"code\":\"service_required\"}")
        };

        ApiRequestException error = await Assert.ThrowsAsync<ApiRequestException>(() =>
            ApiResponse.EnsureSuccessAsync(response, CancellationToken.None,
                "Service request was denied."));

        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal(ApiErrorCodes.ServiceRequired, error.ErrorCode);
        Assert.Equal("Service request was denied.", error.Message);
        Assert.True(ApiErrorCodes.IsServiceEntitlementDenied(error));
    }

    [Theory]
    [InlineData(ApiErrorCodes.DeviceDisabled)]
    [InlineData(ApiErrorCodes.ClientDisabled)]
    public void DisabledClientOrDevice_IsTerminalServiceDenial(string code)
    {
        var error = new ApiRequestException(HttpStatusCode.Forbidden, code);

        Assert.True(ApiErrorCodes.IsServiceEntitlementDenied(error));
    }

    [Fact]
    public void UnrelatedForbidden_DoesNotDemoteService()
    {
        var error = new ApiRequestException(HttpStatusCode.Forbidden, "remote_access_key_changed");

        Assert.False(ApiErrorCodes.IsServiceEntitlementDenied(error));
    }
}
