using System.Security.Cryptography;
using System.Text;
using FluentResults;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Zip.Compression;
using Microsoft.Extensions.Options;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Data;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;
using Zeepkist.WorkshopApi.Drone.FluentResults;

namespace TNRD.Zeepkist.WorkshopApi.Drone;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> logger;
    private readonly ILogger<DepotDownloader.DepotDownloader> depotDownloaderLogger;
    private readonly SteamClient steamClient;
    private readonly ApiClient apiClient;
    private readonly IUploadService uploadService;
    private readonly SteamOptions steamOptions;

    public Worker(
        ILogger<Worker> logger,
        SteamClient steamClient,
        ApiClient apiClient,
        IUploadService uploadService,
        ILogger<DepotDownloader.DepotDownloader> depotDownloaderLogger,
        IOptions<SteamOptions> steamOptions
    )
    {
        this.logger = logger;
        this.steamClient = steamClient;
        this.apiClient = apiClient;
        this.uploadService = uploadService;
        this.depotDownloaderLogger = depotDownloaderLogger;
        this.steamOptions = steamOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DepotDownloader.DepotDownloader.Initialize(depotDownloaderLogger);
        int timeToWait = 5;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExecuteByCreated(stoppingToken);
                await ExecuteByModified(stoppingToken);

                timeToWait = 5;
                logger.LogInformation("Waiting 1 minute before checking again");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogCritical(e, "Unhandled exception");
                logger.LogInformation("Waiting 5 minutes before trying again");
                await Task.Delay(TimeSpan.FromMinutes(timeToWait), stoppingToken);
                timeToWait *= 2;
            }
        }

        DepotDownloader.DepotDownloader.Dispose();
    }

    private async Task ExecuteByModified(CancellationToken stoppingToken)
    {
        int page = 1;
        int totalPages = await steamClient.GetTotalPages(true, stoppingToken);

        bool pastLastStamp = false;

        while (!stoppingToken.IsCancellationRequested && !pastLastStamp)
        {
            logger.LogInformation("Getting page {Page}/{Total}", page, totalPages);
            Response response = await steamClient.GetResponse(page, true, stoppingToken);

            if (await ProcessResponse(response, stoppingToken))
                break;

            page++;
        }
    }

    private async Task ExecuteByCreated(CancellationToken stoppingToken)
    {
        int page = 1;
        int totalPages = await steamClient.GetTotalPages(false, stoppingToken);

        bool pastLastStamp = false;

        while (!stoppingToken.IsCancellationRequested && !pastLastStamp)
        {
            logger.LogInformation("Getting page {Page}/{Total}", page, totalPages);
            Response response = await steamClient.GetResponse(page, false, stoppingToken);

            if (await ProcessResponse(response, stoppingToken))
                break;

            page++;
        }
    }

    private async Task<bool> ProcessResponse(Response response, CancellationToken stoppingToken)
    {
        logger.LogInformation("Filtering items");
        List<PublishedFileDetails> filtered = await Filter(response);

        logger.LogInformation("Processing {Count} items", filtered.Count);
        foreach (PublishedFileDetails publishedFileDetails in filtered)
        {
            logger.LogInformation("Downloading {WorkshopId}", publishedFileDetails.PublishedFileId);
            await DepotDownloader.DepotDownloader.Run(publishedFileDetails.PublishedFileId,
                steamOptions.MountDestination);

            List<string> files = Directory
                .EnumerateFiles(steamOptions.MountDestination, "*.zeeplevel", SearchOption.AllDirectories).ToList();

            foreach (string path in files)
            {
                logger.LogInformation("Processing '{Path}'", path);
                await ProcessItem(path,
                    publishedFileDetails,
                    publishedFileDetails.PublishedFileId,
                    stoppingToken);
            }

            Directory.Delete(steamOptions.MountDestination, true);
        }

        return filtered.Count == 0;
    }

    private async Task<List<PublishedFileDetails>> Filter(Response response)
    {
        List<PublishedFileDetails> filtered = new();

        foreach (PublishedFileDetails details in response.PublishedFileDetails)
        {
            Result<IEnumerable<LevelResponseModel>> result =
                await apiClient.GetLevelsByWorkshopId(details.PublishedFileId);

            if (result.IsFailed)
            {
                filtered.Add(details);
                continue;
            }

            bool addToFiltered = false;

            foreach (LevelResponseModel model in result.Value)
            {
                if (model.ReplacedBy.HasValue)
                    continue;

                if (model.CreatedAt >= details.TimeCreated)
                    continue;

                if (model.UpdatedAt >= details.TimeUpdated)
                    continue;

                addToFiltered = true;
            }

            if (addToFiltered)
            {
                filtered.Add(details);
            }
        }

        return filtered;
    }

    private async Task ProcessItem(
        string path,
        PublishedFileDetails item,
        string workshopId,
        CancellationToken stoppingToken
    )
    {
        string filename = Path.GetFileNameWithoutExtension(path);

        if (string.IsNullOrEmpty(filename) || string.IsNullOrWhiteSpace(filename))
        {
            logger.LogWarning("Filename for {WorkshopId} is empty", workshopId);
            filename = "[Unknown]";
        }

        Result<IEnumerable<LevelResponseModel>> getLevelsResult = await apiClient.GetLevelsByWorkshopId(workshopId);
        if (getLevelsResult.IsFailedWithNotFound())
        {
            await HandleNewLevel(path, item, filename, stoppingToken);
        }
        else if (getLevelsResult.IsSuccess)
        {
            await HandleExistingItem(path, item, getLevelsResult, filename, stoppingToken);
        }
        else
        {
            logger.LogCritical("Unable to get levels from API; Result: {Result}", getLevelsResult.ToString());
            throw new Exception();
        }
    }

    private async Task HandleNewLevel(
        string path,
        PublishedFileDetails item,
        string filename,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await CreateNewLevel(path, filename, item, stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to create new level");
            throw;
        }
    }

    private async Task HandleExistingItem(
        string path,
        PublishedFileDetails item,
        Result<IEnumerable<LevelResponseModel>> getLevelsResult,
        string filename,
        CancellationToken stoppingToken
    )
    {
        LevelResponseModel? existingItem = getLevelsResult.Value.FirstOrDefault(x =>
            x.Name == filename && x.AuthorId == item.Creator && x.ReplacedBy == null);

        if (existingItem == null)
        {
            try
            {
                await CreateNewLevel(path, filename, item, stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to create new level");
                throw;
            }
        }
        else if (item.TimeCreated > existingItem.CreatedAt || item.TimeUpdated > existingItem.UpdatedAt)
        {
            await ReplaceExistingLevel(existingItem, path, filename, item, stoppingToken);
        }
        else
        {
            logger.LogInformation("Received item isn't newer than the existing item, skipping");
        }
    }

    private async Task<int> CreateNewLevel(
        string path,
        string filename,
        PublishedFileDetails item,
        CancellationToken stoppingToken
    )
    {
        string[] lines = await File.ReadAllLinesAsync(path, stoppingToken);
        string[] splits = lines[0].Split(',');
        string author = splits[1];
        string uid = splits[2];

        if (string.IsNullOrEmpty(author) || string.IsNullOrWhiteSpace(author))
        {
            logger.LogWarning("Author for {Filename} ({WorkshopId}) is empty", filename, item.PublishedFileId);
            author = "[Unknown]";
        }

        ParseTimes(filename,
            item,
            lines[2].Split(','),
            out bool valid,
            out float parsedValidation,
            out float parsedGold,
            out float parsedSilver,
            out float parsedBronze);

        string hash = Hash(await GetTextToHash(path, stoppingToken));
        string sourceDirectory = Path.GetDirectoryName(path)!;
        string? image = Directory.GetFiles(sourceDirectory, "*.jpg").FirstOrDefault();

        if (string.IsNullOrEmpty(image))
        {
            logger.LogWarning("No image found for {Filename}", filename);
        }

        FastZip fastZip = new();
        fastZip.CompressionLevel = Deflater.CompressionLevel.BEST_COMPRESSION;
        fastZip.CreateZip(path + ".zip", sourceDirectory, true, $@"\\({filename}.zeeplevel)$");

        string identifier = Guid.NewGuid().ToString();

        logger.LogInformation("Identifier: {Identifier}", identifier);

        Result<string> uploadLevelResult =
            await uploadService.UploadLevel(identifier,
                await File.ReadAllBytesAsync(path + ".zip", stoppingToken),
                stoppingToken);

        if (uploadLevelResult.IsFailed)
        {
            throw new Exception();
        }

        Result<string> uploadThumbnailResult;

        if (!string.IsNullOrEmpty(image))
        {
            uploadThumbnailResult = await uploadService.UploadThumbnail(identifier,
                await File.ReadAllBytesAsync(image, stoppingToken),
                stoppingToken);

            if (uploadThumbnailResult.IsFailed)
            {
                throw new Exception();
            }
        }
        else
        {
            uploadThumbnailResult = "https://storage.googleapis.com/zworpshop/image-not-found.png";
        }

        Result<LevelResponseModel> createLevelResult = await apiClient.CreateLevel(builder =>
        {
            builder
                .WithWorkshopId(item.PublishedFileId)
                .WithAuthorId(item.Creator)
                .WithName(filename)
                .WithCreatedAt(item.TimeCreated)
                .WithUpdatedAt(item.TimeUpdated)
                .WithImageUrl(uploadThumbnailResult.Value)
                .WithFileUrl(uploadLevelResult.Value)
                .WithFileUid(uid)
                .WithFileHash(hash)
                .WithFileAuthor(author)
                .WithValid(valid)
                .WithValidation(parsedValidation)
                .WithGold(parsedGold)
                .WithSilver(parsedSilver)
                .WithBronze(parsedBronze);
        });

        if (createLevelResult.IsFailed)
        {
            throw new Exception();
        }

        logger.LogInformation("Created level [{LevelId} ({WorkshopId})] {Name} by {Author} ({AuthorId})",
            createLevelResult.Value.Id,
            item.PublishedFileId,
            filename,
            author,
            item.Creator);

        return createLevelResult.Value.Id;
    }

    private void ParseTimes(
        string filename,
        PublishedFileDetails item,
        string[] splits,
        out bool valid,
        out float parsedValidation,
        out float parsedGold,
        out float parsedSilver,
        out float parsedBronze
    )
    {
        parsedValidation = 0;
        parsedGold = 0;
        parsedSilver = 0;
        parsedBronze = 0;

        valid = false;

        if (splits.Length >= 4)
        {
            valid = float.TryParse(splits[0], out parsedValidation) &&
                    float.TryParse(splits[1], out parsedGold) &&
                    float.TryParse(splits[2], out parsedSilver) &&
                    float.TryParse(splits[3], out parsedBronze);
        }
        else
        {
            logger.LogWarning("Not enough splits for {Filename} ({WorkshopId})", filename, item.PublishedFileId);
        }

        if (valid)
        {
            if (float.IsNaN(parsedValidation) || float.IsInfinity(parsedValidation) ||
                float.IsNaN(parsedGold) || float.IsInfinity(parsedGold) ||
                float.IsNaN(parsedSilver) || float.IsInfinity(parsedSilver) ||
                float.IsNaN(parsedBronze) || float.IsInfinity(parsedBronze))
            {
                valid = false;
            }
        }

        if (!valid)
        {
            parsedValidation = 0;
            parsedGold = 0;
            parsedSilver = 0;
            parsedBronze = 0;
        }
    }

    private async Task ReplaceExistingLevel(
        LevelResponseModel existingItem,
        string path,
        string filename,
        PublishedFileDetails item,
        CancellationToken stoppingToken
    )
    {
        string newUid = await GetUidFromFile(path, stoppingToken);
        if (string.Equals(existingItem.FileUid, newUid))
        {
            logger.LogInformation("False positive for {Filename} ({WorkshopId})", filename, item.PublishedFileId);
            return;
        }

        int newId;

        try
        {
            newId = await CreateNewLevel(path, filename, item, stoppingToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to create new level");
            throw;
        }

        int existingId = existingItem.Id;

        Result<LevelResponseModel> result = await apiClient.UpdateLevel(existingId, newId);

        if (result.IsFailed)
        {
            logger.LogCritical("Unable to replace level {ExistingId} with {NewId}; Result: {Result}",
                existingId,
                newId,
                result.ToString());

            throw new Exception();
        }

        logger.LogInformation("Replaced level {ExistingId} with {NewId}", existingId, newId);
    }

    private static async Task<string> GetUidFromFile(string path, CancellationToken stoppingToken)
    {
        string[] lines = await File.ReadAllLinesAsync(path, stoppingToken);
        return lines[0].Split(',')[2];
    }

    private static async Task<string> GetTextToHash(string path, CancellationToken stoppingToken)
    {
        string[] lines = await File.ReadAllLinesAsync(path, stoppingToken);
        string[] splits = lines[2].Split(',');

        string skyboxAndBasePlate = splits.Length != 6
            ? "unknown,unknown"
            : splits[^2] + "," + splits[^1];

        return string.Join("\n", lines.Skip(3).Prepend(skyboxAndBasePlate));
    }

    private static string Hash(string input)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        StringBuilder sb = new(hash.Length * 2);

        foreach (byte b in hash)
        {
            // can be "x2" if you want lowercase
            sb.Append(b.ToString("X2"));
        }

        return sb.ToString();
    }
}
