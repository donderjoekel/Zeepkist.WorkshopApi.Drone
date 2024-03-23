using Newtonsoft.Json;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Data;

public class Response
{
    public int Total { get; set; }
    public PublishedFileDetails[] PublishedFileDetails { get; set; }
    [JsonProperty("next_cursor")]
    public string NextCursor { get; set; }
}
