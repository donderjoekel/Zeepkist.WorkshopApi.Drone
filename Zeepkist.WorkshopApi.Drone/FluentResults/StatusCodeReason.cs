using System.Net;
using FluentResults;

namespace Zeepkist.WorkshopApi.Drone.FluentResults;

internal class StatusCodeReason : IReason
{
    /// <inheritdoc />
    public string Message { get; }

    /// <inheritdoc />
    public Dictionary<string, object> Metadata { get; }

    public HttpStatusCode StatusCode { get; }

    public StatusCodeReason(HttpStatusCode statusCode)
    {
        StatusCode = statusCode;
    }
}
