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
        // ReSharper disable once ContextualLoggerProblem
        ILogger<DepotDownloader.DepotDownloader> depotDownloaderLogger,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        IOptions<SteamOptions> steamOptions,
        ILogger<FullScanJob> logger
    )
        : base(depotDownloaderLogger, steamClient, apiClient, uploadService, steamOptions, logger)
    {
    }

    protected override int MaxEmptyPages => int.MaxValue;
    protected override bool ByModified => false;

    protected override Task ExecuteJob(CancellationToken stoppingToken)
    {
        return ExecuteMulti(stoppingToken);
    }
}
