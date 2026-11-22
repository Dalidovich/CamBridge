using CamBridge.Exceptions;

namespace CamBridge.Api;

public static class ApiErrorMapper
{
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (TryMap(ex, out var result))
        {
            return result;
        }
    }

    public static bool TryMap(Exception exception, out IResult result)
    {
        var status = exception switch
        {
            PhoneNotConnectedException => StatusCodes.Status503ServiceUnavailable,
            PhoneTimeoutException => StatusCodes.Status504GatewayTimeout,
            PhoneCommandException => StatusCodes.Status502BadGateway,
            _ => 0
        };

        result = status == 0
            ? Results.Empty
            : Results.Json(new { ok = false, error = exception.Message }, statusCode: status);
        return status != 0;
    }
}
