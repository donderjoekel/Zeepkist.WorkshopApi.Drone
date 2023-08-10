namespace TNRD.Zeepkist.WorkshopApi.Drone.Google;

internal class GoogleOptions
{
    public string Credentials { get; set; } = null!;
    public string Bucket { get; set; } = null!;
    public string LevelsFolder { get; set; } = null!;
    public string ThumbnailsFolder { get; set; } = null!;
}
