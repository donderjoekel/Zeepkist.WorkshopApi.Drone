using System.Web;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TNRD.Zeepkist.WorkshopApi.Drone.Data;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Steam;

public class SteamClient
{
    private const int ITEMS_PER_PAGE = 5;

    private readonly HttpClient httpClient;
    private readonly SteamOptions options;

    public SteamClient(HttpClient httpClient, IOptions<SteamOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<int> GetTotalPages(bool byModified, CancellationToken stoppingToken)
    {
        Dictionary<string, string> query = new()
        {
            { "key", options.Key },
            { "query_type", byModified ? "21" : "1" },
            { "appid", "1440670" },
            { "totalonly", "true" },
            { "format", "json" }
        };

        string url = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/" + ToQueryString(query);

        HttpResponseMessage response = await httpClient.GetAsync(url, stoppingToken);

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(stoppingToken);

        ResponseWrapper? responseWrapper = JsonConvert.DeserializeObject<ResponseWrapper>(json);

        return (int)Math.Ceiling(responseWrapper.Response.Total / (double)ITEMS_PER_PAGE);
    }

    public async Task<Response> GetResponse(int page, bool byModified, CancellationToken stoppingToken)
    {
        int actualPage = page;

        Dictionary<string, string> query = new()
        {
            { "key", options.Key },
            { "query_type", byModified ? "21" : "1" },
            { "appid", "1440670" },
            { "page", actualPage.ToString() },
            { "numperpage", ITEMS_PER_PAGE.ToString() },
            { "return_metadata", "true" },
            { "format", "json" }
        };

        string url = "https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/" + ToQueryString(query);

        HttpResponseMessage response = await httpClient.GetAsync(url, stoppingToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(stoppingToken);

        ResponseWrapper? responseWrapper = JsonConvert.DeserializeObject<ResponseWrapper>(json);
        return responseWrapper.Response;
    }

    private static string ToQueryString(Dictionary<string, string> dict)
    {
        List<string> items = dict
            .Select(kvp => HttpUtility.UrlEncode(kvp.Key) + "=" + HttpUtility.UrlEncode(kvp.Value))
            .ToList();

        return "?" + string.Join("&", items);
    }
}
