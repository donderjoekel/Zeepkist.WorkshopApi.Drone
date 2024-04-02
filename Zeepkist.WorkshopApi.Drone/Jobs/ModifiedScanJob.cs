using Microsoft.Extensions.Options;
using Quartz;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Jobs;

public class ModifiedScanJob : BaseJob
{
    public static readonly JobKey JobKey = new("ModifiedScanJob");

    public ModifiedScanJob(
        ILoggerFactory loggerFactory,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        IOptions<SteamOptions> steamOptions
    )
        : base(loggerFactory, steamClient, apiClient, uploadService, steamOptions)
    {
    }

    protected override int MaxEmptyPages => MAX_EMPTY_PAGES;
    protected override bool ByModified => true;
    
    protected override Task ExecuteJob(CancellationToken ct)
    {
        return ExecuteMulti(ct);
    }
}
