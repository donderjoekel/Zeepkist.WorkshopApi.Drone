using Newtonsoft.Json;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Data;

public class PublishedFileDetails
{
    [JsonProperty("publishedfileid")] public string PublishedFileId { get; set; } = null!;
    [JsonProperty("creator")] public string Creator { get; set; } = null!;
    [JsonProperty("preview_url")] public string PreviewUrl { get; set; } = null!;
    [JsonProperty("title")] public string Title { get; set; } = null!;
    [JsonProperty("time_created")] private long TimeCreatedInternal { get; set; }
    [JsonProperty("time_updated")] private long TimeUpdatedInternal { get; set; }
    [JsonProperty("can_subscribe")] public bool CanSubscribe { get; set; }

    [JsonIgnore] public DateTime TimeCreated => DateTimeOffset.FromUnixTimeSeconds(TimeCreatedInternal).UtcDateTime;
    [JsonIgnore] public DateTime TimeUpdated => DateTimeOffset.FromUnixTimeSeconds(TimeUpdatedInternal).UtcDateTime;
}
