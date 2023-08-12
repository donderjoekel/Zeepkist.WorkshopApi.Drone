using System.Net;
using System.Text;
using FluentResults;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;
using Zeepkist.WorkshopApi.Drone.FluentResults;
using Zeepkist.WorkshopApi.Drone.RequestModels;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Api;

public class ApiClient
{
    private readonly HttpClient client;
    private readonly ILogger<ApiClient> logger;

    public ApiClient(HttpClient client, IOptions<ApiOptions> options, ILogger<ApiClient> logger)
    {
        this.client = client;
        this.client.BaseAddress = new Uri("https://api.zworpshop.com/");
        this.client.DefaultRequestHeaders.Add("x-api-key", options.Value.Key);
        this.logger = logger;
    }

    public async Task<Result<IEnumerable<LevelResponseModel>>> GetLevelsByWorkshopId(string workshopId)
    {
        return await Get<IEnumerable<LevelResponseModel>>("levels/workshop/" + workshopId);
    }

    public async Task<Result<LevelResponseModel>> CreateLevel(Action<LevelPostRequestModelBuilder> builder)
    {
        LevelPostRequestModelBuilder actualBuilder = new();
        builder.Invoke(actualBuilder);
        LevelPostRequestModel model = actualBuilder.Build();

        return await Post<LevelResponseModel>("levels", model);
    }

    public async Task<Result<LevelResponseModel>> UpdateLevel(int existingId, int replacementId)
    {
        LevelPutRequestModel model = new()
        {
            Replacement = replacementId
        };

        return await Put<LevelResponseModel>($"levels/{existingId}", model);
    }

    private async Task<Result<TResponse>> Get<TResponse>(
        string requestUri,
        CancellationToken ct = default
    )
    {
        HttpRequestMessage requestMessage = new(HttpMethod.Get, requestUri);

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(requestMessage, ct);
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e))
                .WithReason(new StatusCodeReason(response.StatusCode));
        }

        try
        {
            string responseJson = await response.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<TResponse>(responseJson)!;
        }
        catch (Exception e)
        {
            return new ExceptionalError(e);
        }
    }

    private async Task<Result> Post(
        string requestUri,
        object data,
        CancellationToken ct = default
    )
    {
        string requestJson = JsonConvert.SerializeObject(data);
        HttpRequestMessage requestMessage = new(HttpMethod.Post, requestUri);
        requestMessage.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(requestMessage, ct);
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e))
                .WithReason(new StatusCodeReason(response.StatusCode));
        }

        return Result.Ok()
            .WithReason(new StatusCodeReason(response.StatusCode));
    }

    private async Task<Result<TResponse>> Post<TResponse>(
        string requestUri,
        object data,
        CancellationToken ct = default
    )
    {
        string requestJson = JsonConvert.SerializeObject(data);
        HttpRequestMessage requestMessage = new(HttpMethod.Post, requestUri);
        requestMessage.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(requestMessage, ct);
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            Result result = Result.Fail(new ExceptionalError(e))
                .WithReason(new StatusCodeReason(response.StatusCode));

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                string errorMessage = await response.Content.ReadAsStringAsync(ct);
                result = result.WithError(errorMessage);
            }

            return result;
        }

        try
        {
            string responseJson = await response.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<TResponse>(responseJson)!;
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }
    }

    private async Task<Result<TResponse>> Put<TResponse>(
        string requestUri,
        object data,
        CancellationToken ct = default
    )
    {
        string requestJson = JsonConvert.SerializeObject(data);
        HttpRequestMessage requestMessage = new(HttpMethod.Put, requestUri);
        requestMessage.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(requestMessage, ct);
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }

        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            Result result = Result.Fail(new ExceptionalError(e))
                .WithReason(new StatusCodeReason(response.StatusCode));

            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                string errorMessage = await response.Content.ReadAsStringAsync(ct);
                result = result.WithError(errorMessage);
            }

            return result;
        }

        try
        {
            string responseJson = await response.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<TResponse>(responseJson)!;
        }
        catch (Exception e)
        {
            return Result.Fail(new ExceptionalError(e));
        }
    }
}
