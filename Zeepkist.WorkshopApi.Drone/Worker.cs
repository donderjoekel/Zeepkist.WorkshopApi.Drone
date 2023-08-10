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
    private const string LAST_STAMP_FILE = "laststamp.txt";

    private readonly ILogger<Worker> logger;
    private readonly ILogger<DepotDownloader.DepotDownloader> depotDownloaderLogger;
    private readonly SteamClient steamClient;
    private readonly ApiClient apiClient;
    private readonly IUploadService uploadService;
    private readonly SteamOptions steamOptions;

    private long lastStamp;
    private DateTimeOffset Stamp => DateTimeOffset.FromUnixTimeSeconds(lastStamp);

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

        GetLastStampFromFile();
        GetLastStampFromOptions();
    }

    private void GetLastStampFromFile()
    {
        if (!File.Exists(LAST_STAMP_FILE))
            return;

        string txt = File.ReadAllText(LAST_STAMP_FILE);
        if (string.IsNullOrEmpty(txt) || string.IsNullOrWhiteSpace(txt))
            return;

        if (!long.TryParse(txt, out lastStamp))
        {
            lastStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    private void GetLastStampFromOptions()
    {
        if (string.IsNullOrEmpty(steamOptions.LastStamp))
            return;

        if (long.TryParse(steamOptions.LastStamp, out long tempStamp))
        {
            if (tempStamp > lastStamp)
            {
                lastStamp = tempStamp;
            }
        }
        else if (lastStamp == 0)
        {
            lastStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DepotDownloader.DepotDownloader.Initialize(depotDownloaderLogger);

        while (!stoppingToken.IsCancellationRequested)
        {
            int page = 1;

            bool pastLastStamp = false;

            while (!stoppingToken.IsCancellationRequested && !pastLastStamp)
            {
                logger.LogInformation("Getting page {Page}", page);
                Response response = await steamClient.GetResponse(page, stoppingToken);
                foreach (PublishedFileDetails publishedFileDetails in response.PublishedFileDetails)
                {
                    if (publishedFileDetails.TimeCreated < Stamp || publishedFileDetails.TimeUpdated < Stamp)
                    {
                        pastLastStamp = true;
                        break;
                    }

                    await DepotDownloader.DepotDownloader.Run(publishedFileDetails.PublishedFileId,
                        steamOptions.Destination);

                    List<string> files = Directory
                        .EnumerateFiles(steamOptions.Destination, "*.zeeplevel", SearchOption.AllDirectories).ToList();

                    foreach (string path in files)
                    {
                        await ProcessItem(path,
                            publishedFileDetails,
                            publishedFileDetails.PublishedFileId,
                            stoppingToken);
                    }
                }

                page++;
            }

            lastStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await File.WriteAllTextAsync(LAST_STAMP_FILE, lastStamp.ToString(), stoppingToken);

            logger.LogInformation("Waiting 2.5 minutes before checking again");
            await Task.Delay(TimeSpan.FromMinutes(2.5), stoppingToken);
        }

        DepotDownloader.DepotDownloader.Dispose();
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
            try
            {
                await CreateNewLevel(path, filename, item, stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Unable to create new level");
                throw;
            }

            return;
        }

        LevelResponseModel? existingItem =
            getLevelsResult.Value.FirstOrDefault(x => x.Name == filename && x.AuthorId == item.Creator);

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
            logger.LogWarning("Level already exists, probably need to check if we need to change the file?");
        }
        else
        {
            logger.LogInformation("Received item isn't newer than the existing item, skipping");
        }
    }

    private async Task CreateNewLevel(
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

        splits = lines[2].Split(',');
        float parsedValidation = 0;
        float parsedGold = 0;
        float parsedSilver = 0;
        float parsedBronze = 0;

        bool valid = false;

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

        string hash = Hash(await File.ReadAllTextAsync(path, stoppingToken));
        string sourceDirectory = Path.GetDirectoryName(path)!;
        string? image = Directory.GetFiles(sourceDirectory, "*.jpg").FirstOrDefault();

        if (string.IsNullOrEmpty(image))
        {
            logger.LogWarning("No image found for {Filename}", filename);
        }

        FastZip fastZip = new();
        fastZip.CompressionLevel = Deflater.CompressionLevel.BEST_COMPRESSION;
        fastZip.CreateZip(path + ".zip", sourceDirectory, true, @"\.zeeplevel$");

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
    }

    // private async Task<List<PublishedFileDetails>> Filter(Response response)
    // {
    //     List<PublishedFileDetails> filtered = new();
    //
    //     foreach (PublishedFileDetails details in response.PublishedFileDetails)
    //     {
    //         Result<IEnumerable<LevelResponseModel>> result =
    //             await apiClient.GetLevelsByWorkshopId(details.PublishedFileId);
    //
    //         if (result.IsFailed)
    //         {
    //             filtered.Add(details);
    //             continue;
    //         }
    //
    //         if (result.Value.Any(x => x.CreatedAt < details.TimeCreated || x.UpdatedAt < details.TimeUpdated))
    //         {
    //             filtered.Add(details);
    //         }
    //     }
    //
    //     return filtered;
    // }

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
