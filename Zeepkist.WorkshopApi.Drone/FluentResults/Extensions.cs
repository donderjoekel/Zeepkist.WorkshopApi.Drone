using System.Net;
using FluentResults;

namespace Zeepkist.WorkshopApi.Drone.FluentResults;

public  static class Extensions
{
    public static bool IsFailedWithNotFound(this ResultBase resultBase)
    {
        if (resultBase.IsSuccess)
            return false;

        foreach (IReason reason in resultBase.Reasons)
        {
            if (reason is StatusCodeReason statusCodeReason)
            {
                if (statusCodeReason.StatusCode == HttpStatusCode.NotFound)
                    return true;
            }
        }

        return false;
    }
}
