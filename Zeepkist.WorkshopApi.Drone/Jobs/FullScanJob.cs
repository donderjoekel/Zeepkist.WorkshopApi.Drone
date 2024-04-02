using Microsoft.Extensions.Options;
using Quartz;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Jobs;

public class FullScanJob : BaseJob
{
    public static readonly JobKey JobKey = new("FullScanJob");

    public FullScanJob(
        ILoggerFactory loggerFactory,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        IOptions<SteamOptions> steamOptions
    )
        : base(loggerFactory, steamClient, apiClient, uploadService, steamOptions)
    {
    }

    protected override int MaxEmptyPages => int.MaxValue;
    protected override bool ByModified => false;

    protected override Task ExecuteJob(CancellationToken ct)
    {
        return ExecuteMulti(ct);
    }
}
