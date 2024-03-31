using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using FluentResults;
using Microsoft.Extensions.Options;
using TNRD.Zeepkist.WorkshopApi.Drone.Api;
using TNRD.Zeepkist.WorkshopApi.Drone.Data;
using TNRD.Zeepkist.WorkshopApi.Drone.FluentResults;
using TNRD.Zeepkist.WorkshopApi.Drone.Google;
using TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;
using TNRD.Zeepkist.WorkshopApi.Drone.Steam;

namespace TNRD.Zeepkist.WorkshopApi.Drone;

public class Worker : BackgroundService
{
    private const int MAX_EMPTY_PAGES = 10;

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
        // ReSharper disable once ContextualLoggerProblem
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
        int timeToWait = 5;

        while (!stoppingToken.IsCancellationRequested)
        {
            DepotDownloader.DepotDownloader.Initialize(depotDownloaderLogger);

            try
            {
                await Execute(true, stoppingToken);
                await Execute(false, stoppingToken);

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

            DepotDownloader.DepotDownloader.Dispose();

            GC.Collect();
        }
    }

    private async Task Execute(bool byModified, CancellationToken stoppingToken)
    {
        int amountEmpty = 0;
        int page = 0;

        int totalPages = await steamClient.GetTotalPages(byModified, stoppingToken);

        while (!stoppingToken.IsCancellationRequested && amountEmpty < MAX_EMPTY_PAGES)
        {
            logger.LogInformation("Getting page {Page}/{Total}", page, totalPages);
            Response response = await steamClient.GetResponse(page, byModified, stoppingToken);

            if (response.PublishedFileDetails.Length == 0)
            {
                logger.LogInformation("No more items, breaking loop");
                break;
            }

            if (await ProcessResponse(response, stoppingToken))
                amountEmpty++;
            else
                amountEmpty = 0;

            page++;
        }
    }
    
    private async Task ExecuteSingle(string workshopId, CancellationToken stoppingToken)
    {
        Response response = await steamClient.GetResponse(workshopId, stoppingToken);
        await ProcessResponse(response, stoppingToken);
    }

    private async Task<bool> ProcessResponse(Response response, CancellationToken stoppingToken)
    {
        int totalFalsePositives = 0;

        logger.LogInformation("Processing {Count} items", response.PublishedFileDetails.Length);
        foreach (PublishedFileDetails publishedFileDetails in response.PublishedFileDetails)
        {
            string guid = Guid.NewGuid().ToString().Replace("-", "");
            string destination = Path.Combine(steamOptions.MountDestination, guid);
            
            logger.LogInformation("Downloading {WorkshopId}", publishedFileDetails.PublishedFileId);
            await DepotDownloader.DepotDownloader.Run(publishedFileDetails.PublishedFileId, destination);

            List<string> files = Directory.EnumerateFiles(destination, "*.zeeplevel", SearchOption.AllDirectories)
                .ToList();

            await DeleteMissingLevels(publishedFileDetails, files);

            int falsePositives = 0;
            foreach (string path in files)
            {
                logger.LogInformation("Processing '{Path}'", path);
                Result<bool> processResult = await ProcessItem(path,
                    publishedFileDetails,
                    publishedFileDetails.PublishedFileId,
                    stoppingToken);

                if (processResult.IsFailed)
                {
                    logger.LogError("Unable to process item: {Result}", processResult);
                }
                else if (!processResult.Value)
                {
                    falsePositives++;
                }
            }

            if (falsePositives == files.Count)
            {
                totalFalsePositives++;
                await EnsureFalsePositivesTimeUpdated(publishedFileDetails, files);
            }

            Directory.Delete(destination, true);
        }

        return response.PublishedFileDetails.Length - totalFalsePositives == 0;
    }

    private async Task DeleteMissingLevels(PublishedFileDetails publishedFileDetails, List<string> files)
    {
        Result<IEnumerable<LevelResponseModel>> result =
            await apiClient.GetLevelsByWorkshopId(publishedFileDetails.PublishedFileId);

        if (result.IsFailedWithNotFound())
        {
            return;
        }
        
        if (result.IsFailed)
        {
            logger.LogError("Unable to get levels by workshop id: {Result}", result.ToString());
            return;
        }

        List<(string uid, string hash)> fileData = new();

        foreach (string file in files)
        {
            string uid = await GetUidFromFile(file, CancellationToken.None);
            string textToHash = await GetTextToHash(file, CancellationToken.None);
            string hash = Hash(textToHash);
            fileData.Add((uid, hash));
        }

        foreach (LevelResponseModel levelResponseModel in result.Value)
        {
            bool foundLevelInFile = false;

            foreach ((string uid, string hash) in fileData)
            {
                if (levelResponseModel.FileUid == uid && levelResponseModel.FileHash == hash)
                {
                    foundLevelInFile = true;
                    break;
                }
            }

            if (foundLevelInFile)
                continue;

            Result<LevelResponseModel> deleteResult = await apiClient.DeleteLevel(levelResponseModel.Id);

            if (deleteResult.IsFailed)
            {
                logger.LogError("Unable to delete level: {Result}", deleteResult.ToString());
            }
        }
    }

    private async Task EnsureFalsePositivesTimeUpdated(PublishedFileDetails publishedFileDetails, List<string> files)
    {
        Result<IEnumerable<LevelResponseModel>> result =
            await apiClient.GetLevelsByWorkshopId(publishedFileDetails.PublishedFileId);

        if (!result.IsSuccess)
        {
            logger.LogError("Unable to get levels by workshop id: {Result}", result.ToString());
            return;
        }

        bool sameAmount = result.Value.Count() == files.Count;

        if (sameAmount)
            return;

        foreach (LevelResponseModel levelResponseModel in result.Value)
        {
            if (levelResponseModel.UpdatedAt == publishedFileDetails.TimeUpdated)
                continue;

            Result<LevelResponseModel> updateResult = await apiClient.UpdateLevelTime(
                levelResponseModel.Id,
                new DateTimeOffset(publishedFileDetails.TimeUpdated).ToUnixTimeSeconds());

            if (updateResult.IsFailed)
            {
                logger.LogError("Unable to update level time: {Result}", updateResult.ToString());
            }
        }
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

                if (model.UpdatedAt < details.TimeUpdated)
                {
                    addToFiltered = true;
                }

                if (model.CreatedAt < details.TimeCreated)
                {
                    addToFiltered = true;
                }
            }

            if (addToFiltered)
            {
                filtered.Add(details);
            }
        }

        return filtered;
    }

    private async Task<Result<bool>> ProcessItem(
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
            return await HandleNewLevel(path, item, filename, stoppingToken);
        }

        if (getLevelsResult.IsSuccess)
        {
            return await HandleExistingItem(path, item, getLevelsResult, filename, stoppingToken);
        }

        logger.LogCritical("Unable to get levels from API; Result: {Result}", getLevelsResult.ToString());
        throw new Exception();
    }

    private async Task<Result<bool>> HandleNewLevel(
        string path,
        PublishedFileDetails item,
        string filename,
        CancellationToken stoppingToken
    )
    {
        try
        {
            Result<int> createResult = await CreateNewLevel(path, filename, item, stoppingToken);
            return createResult.IsSuccess ? Result.Ok(true) : createResult.ToResult();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to create new level");
            return Result.Fail(new ExceptionalError(e));
        }
    }

    private async Task<Result<bool>> HandleExistingItem(
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
                Result<int> createResult = await CreateNewLevel(path, filename, item, stoppingToken);
                return createResult.IsSuccess ? Result.Ok(true) : createResult.ToResult();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to create new level");
                return Result.Fail(new ExceptionalError(e));
            }
        }

        if (item.TimeCreated > existingItem.CreatedAt || item.TimeUpdated > existingItem.UpdatedAt)
        {
            return await ReplaceExistingLevel(existingItem, path, filename, item, stoppingToken);
        }

        logger.LogInformation("Received item isn't newer than the existing item, skipping");
        return Result.Ok(false);
    }

    private async Task<Result<int>> CreateNewLevel(
        string path,
        string filename,
        PublishedFileDetails item,
        CancellationToken stoppingToken
    )
    {
        Result<int> metadataResult = await CreateMetadata(path, filename, item, stoppingToken);
        if (metadataResult.IsFailed)
        {
            return metadataResult.ToResult();
        }

        string[] lines = await File.ReadAllLinesAsync(path, stoppingToken);
        if (lines.Length == 0)
        {
            return Result.Fail(new ExceptionalError(new InvalidDataException("Level file is empty")));
        }

        string[] splits = lines[0].Split(',');
        string author = splits[1];
        string uid = splits[2];

        if (string.IsNullOrEmpty(author) || string.IsNullOrWhiteSpace(author))
        {
            logger.LogWarning("Author for {Filename} ({WorkshopId}) is empty", filename, item.PublishedFileId);
            author = "[Unknown]";
        }

        string hash = Hash(await GetTextToHash(path, stoppingToken));
        string sourceDirectory = Path.GetDirectoryName(path)!;
        string? image = Directory.GetFiles(sourceDirectory, "*.jpg").FirstOrDefault();

        if (string.IsNullOrEmpty(image))
        {
            logger.LogWarning("No image found for {Filename}", filename);
        }

        using (FileStream zipStream = File.Create(path + ".zip"))
        {
            string filePath = Path.Combine(sourceDirectory, filename + ".zeeplevel");
            using (ZipArchive archive = new(zipStream, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(filePath, filename + ".zeeplevel", CompressionLevel.Optimal);
            }
        }

        string identifier = Guid.NewGuid().ToString();

        logger.LogInformation("Identifier: {Identifier}", identifier);

        Result<string> uploadLevelResult =
            await uploadService.UploadLevel(identifier,
                await File.ReadAllBytesAsync(path + ".zip", stoppingToken),
                stoppingToken);

        if (uploadLevelResult.IsFailed)
        {
            return uploadLevelResult.ToResult();
        }

        Result<string> uploadThumbnailResult;

        if (!string.IsNullOrEmpty(image))
        {
            uploadThumbnailResult = await uploadService.UploadThumbnail(identifier,
                await File.ReadAllBytesAsync(image, stoppingToken),
                stoppingToken);

            if (uploadThumbnailResult.IsFailed)
            {
                return uploadThumbnailResult.ToResult();
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
                .WithMetadataId(metadataResult.Value);
        });

        if (createLevelResult.IsFailed)
        {
            return createLevelResult.ToResult();
        }

        logger.LogInformation("Created level [{LevelId} ({WorkshopId})] {Name} by {Author} ({AuthorId})",
            createLevelResult.Value.Id,
            item.PublishedFileId,
            filename,
            author,
            item.Creator);

        return createLevelResult.Value.Id;
    }

    private async Task<Result<bool>> ReplaceExistingLevel(
        LevelResponseModel existingItem,
        string path,
        string filename,
        PublishedFileDetails item,
        CancellationToken stoppingToken
    )
    {
        int existingId = existingItem.Id;
        string newUid = await GetUidFromFile(path, stoppingToken);

        if (string.Equals(existingItem.FileUid, newUid))
        {
            if (existingItem.UpdatedAt == item.TimeUpdated)
            {
                logger.LogInformation("False positive for {Filename} ({WorkshopId})", filename, item.PublishedFileId);
                return Result.Ok(false);
            }

            if (existingItem.UpdatedAt < item.TimeUpdated)
            {
                string textToHash = await GetTextToHash(path, stoppingToken);
                string newFileHash = Hash(textToHash);

                if (newFileHash == existingItem.FileHash)
                {
                    Result<LevelResponseModel> updateResult = await apiClient.UpdateLevelTime(existingId,
                        new DateTimeOffset(item.TimeUpdated).ToUnixTimeSeconds());

                    return updateResult.IsSuccess ? Result.Ok(false) : updateResult.ToResult();
                }

                logger.LogError("Hashes don't match for {Filename} ({WorkshopId})", filename, item.PublishedFileId);
                return Result.Ok(false);
            }

            if (existingItem.UpdatedAt > item.TimeUpdated)
            {
                logger.LogInformation("False positive for {Filename} ({WorkshopId}), ours is newer somehow",
                    filename,
                    item.PublishedFileId);
                return Result.Ok(false);
            }

            throw new Exception(
                "I'm not sure when this would exactly happen, but it's here just in case");
        }

        int newId;

        try
        {
            Result<int> createResult = await CreateNewLevel(path, filename, item, stoppingToken);
            if (createResult.IsFailed)
                return createResult.ToResult();

            newId = createResult.Value;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unable to create new level");
            return Result.Fail(new ExceptionalError(e));
        }

        Result<LevelResponseModel> result = await apiClient.ReplaceLevel(existingId, newId);

        if (result.IsFailed)
        {
            logger.LogCritical("Unable to replace level {ExistingId} with {NewId}; Result: {Result}",
                existingId,
                newId,
                result.ToString());

            return result.ToResult();
        }

        logger.LogInformation("Replaced level {ExistingId} with {NewId}", existingId, newId);
        return Result.Ok(true);
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

    private async Task<Result<int>> CreateMetadata(
        string path,
        string filename,
        PublishedFileDetails item,
        CancellationToken stoppingToken
    )
    {
        string hash = Hash(await GetTextToHash(path, stoppingToken));

        Result<MetadataResponseModel> result = await apiClient.GetMetadataByHash(hash);

        if (result.IsSuccess)
        {
            return result.Value.Id;
        }

        if (!result.IsFailedWithNotFound())
        {
            return result.ToResult();
        }
        
        string[] lines = await File.ReadAllLinesAsync(path, stoppingToken);
        if (lines.Length == 0)
        {
            return Result.Fail(new ExceptionalError(new InvalidDataException("Level file is empty")));
        }
        
        string[] splits = lines[2].Split(',');
        
        ParseTimes(filename,
            item,
            splits,
            out bool validTime,
            out float parsedValidation,
            out float parsedGold,
            out float parsedSilver,
            out float parsedBronze);

        int skybox;
        int ground;
        
        if (splits.Length != 6)
        {
            logger.LogWarning("Not enough splits for {Filename} ({WorkshopId})", filename, item.PublishedFileId);
            skybox = int.MaxValue;
            ground = int.MaxValue;
        }
        else
        {
            skybox = int.Parse(splits[^2]);
            ground = int.Parse(splits[^1]);
        }

        string blocks = GetBlocks(path, out int amountOfCheckpoints, out bool validBlocks)
            .TrimEnd('|');

        if (string.IsNullOrEmpty(blocks))
        {
            logger.LogError("No blocks found in level {Filename} ({WorkshopId})", filename, item.PublishedFileId);
            return Result.Fail(new ExceptionalError(new Exception("No blocks found in level")));
        }
        
        bool valid = validTime && validBlocks;

        Result<MetadataResponseModel> metadata = await apiClient.CreateMetadata(builder =>
        {
            builder
                .WithHash(hash)
                .WithCheckpoints(amountOfCheckpoints)
                .WithBlocks(blocks)
                .WithValid(valid)
                .WithValidation(parsedValidation)
                .WithGold(parsedGold)
                .WithSilver(parsedSilver)
                .WithBronze(parsedBronze)
                .WithGround(ground)
                .WithSkybox(skybox);
        });

        if (metadata.IsFailed)
        {
            return metadata.ToResult();
        }

        return metadata.Value.Id;
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
    
    private static readonly string[] starts = new[]{ "1","1363" };
    private static readonly string[] finishes = new[] { "2", "1273", "1274", "1616" };
    private static readonly string[] checkpoints = new[] { "22", "372", "373", "1275", "1276", "1277", "1278", "1279" };

    private string GetBlocks(string path, out int amountOfCheckpoints, out bool valid)
    {
        string[] lines = File.ReadAllLines(path).Skip(3).ToArray();

        Dictionary<string, int> blocks = new();

        foreach (string line in lines)
        {
            if (!line.Contains(','))
            {
                logger.LogWarning("Invalid line in level ({Path}): '{Line}'", path, line);
                continue;
            }
            
            string blockId = line[..line.IndexOf(',')];
            blocks.TryAdd(blockId, 0);
            blocks[blockId]++;
        }

        valid = blocks.Where(x => starts.Contains(x.Key)).Sum(x => x.Value) == 1 &&
                blocks.Where(x => finishes.Contains(x.Key)).Sum(x => x.Value) >= 1;
        
        amountOfCheckpoints = blocks.Where(x => checkpoints.Contains(x.Key)).Sum(x => x.Value);
        
        StringBuilder sb = new();
        foreach (KeyValuePair<string, int> kvp in blocks)
        {
            sb.Append(kvp.Key);
            sb.Append(':');
            sb.Append(kvp.Value);
            sb.Append('|');
        }

        return sb.ToString();
    }
}
