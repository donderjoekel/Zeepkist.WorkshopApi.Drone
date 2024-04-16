using DepotDownloader;
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
        List<Request> requests = await _db.Requests.OrderBy(x => x.Id).ToListAsync(cancellationToken: ct);

        foreach (Request request in requests)
        {
            Logger.LogInformation("Processing scan request with info; {WorkshopId} {Hash} {Uid}",
                request.WorkshopId,
                request.Hash,
                request.Uid);

            MatchType matchBefore = await HasLevel(request, ct);

            if (matchBefore == MatchType.FullMatch)
            {
                Logger.LogInformation("Full match found for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);

                goto REMOVE_FROM_DB;
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (request.WorkshopId == 0)
            {
                Logger.LogWarning("Request has no workshop id; {Hash} {Uid}", request.Hash, request.Uid);
                goto REMOVE_FROM_DB;
            }

            try
            {
                await ExecuteSingle(request.WorkshopId.ToString(), ct);
            }
            catch (ManifestIdNotFoundException)
            {
                Logger.LogError("Unable to find manifest id for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);

                goto REMOVE_FROM_DB;
            }
            catch (Exception e)
            {
                Logger.LogError(e,
                    "Error occurred while processing request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);

                continue;
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            MatchType matchAfter = await HasLevel(request, ct);

            if (matchAfter == MatchType.NoMatch)
            {
                Logger.LogError("No new level was created for request with info; {WorkshopId} {Hash} {Uid}",
                    request.WorkshopId,
                    request.Hash,
                    request.Uid);

                goto REMOVE_FROM_DB;
            }

            if (matchBefore == MatchType.NoMatch)
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

            REMOVE_FROM_DB:

            if (ct.IsCancellationRequested)
            {
                return;
            }

            _db.Requests.Remove(request);
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task<MatchType> HasLevel(Request request, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(request.Hash) && !string.IsNullOrEmpty(request.Uid))
        {
            return await _db.Levels.AnyAsync(x =>
                    x.WorkshopId == request.WorkshopId && x.FileHash == request.Hash && x.FileUid == request.Uid,
                ct)
                ? MatchType.FullMatch
                : MatchType.NoMatch;
        }

        if (!string.IsNullOrEmpty(request.Hash))
        {
            return await _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId && x.FileHash == request.Hash, ct)
                ? MatchType.PartialMatch
                : MatchType.NoMatch;
        }

        if (!string.IsNullOrEmpty(request.Uid))
        {
            return await _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId && x.FileUid == request.Uid, ct)
                ? MatchType.PartialMatch
                : MatchType.NoMatch;
        }

        return await _db.Levels.AnyAsync(x => x.WorkshopId == request.WorkshopId, ct)
            ? MatchType.PartialMatch
            : MatchType.NoMatch;
    }

    private enum MatchType
    {
        NoMatch = 0,
        PartialMatch = 1,
        FullMatch = 2
    }
}
