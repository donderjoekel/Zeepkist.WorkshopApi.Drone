using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TNRD.Zeepkist.WorkshopApi.Database;
using TNRD.Zeepkist.WorkshopApi.Database.Models;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Jobs;

public class RequestsScanJob : BaseJob
{
    public static readonly JobKey JobKey = new("RequestsScanJob");

    private readonly ZworpshopContext _db;

    public RequestsScanJob(
        ILoggerFactory loggerFactory,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        IOptions<SteamOptions> steamOptions,
        ZworpshopContext db
    )
        : base(loggerFactory, steamClient, apiClient, uploadService, steamOptions)
    {
        _db = db;
    }

    protected override int MaxEmptyPages => int.MaxValue;
    protected override bool ByModified => false;

    protected override async Task ExecuteJob(CancellationToken ct)
    {
        List<Request> requests = await _db.Requests.ToListAsync(cancellationToken: ct);

        foreach (Request request in requests)
        {
            bool hadLevelBefore = await HasLevel(request, ct);
            
            if (ct.IsCancellationRequested)
            {
                return;
            }
            
            await ExecuteSingle(request.WorkshopId.ToString(), ct);

            if (ct.IsCancellationRequested)
            {
                return;
            }

            bool hasLevelAfter = await HasLevel(request, ct);

            if (!hasLevelAfter)
            {
                Logger.LogError("No new level was created for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);

                continue;
            }

            if (!hadLevelBefore)
            {
                Logger.LogInformation("New level was created for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);
            }
            else
            {
                Logger.LogInformation("Level was updated for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);
            }
            
            _db.Requests.Remove(request);
            await _db.SaveChangesAsync(ct);
        }
    }

    private Task<bool> HasLevel(Request request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Hash) && !string.IsNullOrEmpty(request.Uid))
        {
            return _db.Levels.AnyAsync(x =>
                    x.WorkshopId == request.WorkshopId && x.FileHash == request.Hash && x.FileUid == request.Uid,
                ct);
        }

        if (!string.IsNullOrEmpty(request.Hash))
        {
            return _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId && x.FileHash == request.Hash, ct);
        }

        if (!string.IsNullOrEmpty(request.Uid))
        {
            return _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId && x.FileUid == request.Uid, ct);
        }

        return _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId, ct);
    }
}
