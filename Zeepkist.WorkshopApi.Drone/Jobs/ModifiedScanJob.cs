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
        // ReSharper disable once ContextualLoggerProblem
        ILogger<DepotDownloader.DepotDownloader> depotDownloaderLogger,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        IOptions<SteamOptions> steamOptions,
        ILogger<ModifiedScanJob> logger
    )
        : base(depotDownloaderLogger, steamClient, apiClient, uploadService, steamOptions, logger)
    {
    }

    protected override int MaxEmptyPages => MAX_EMPTY_PAGES;
    protected override bool ByModified => true;
    
    protected override Task ExecuteJob(CancellationToken stoppingToken)
    {
        return ExecuteMulti(stoppingToken);
    }
}
