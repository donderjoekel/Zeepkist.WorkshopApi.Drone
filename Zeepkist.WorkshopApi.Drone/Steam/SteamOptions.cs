namespace TNRD.Zeepkist.WorkshopApi.Drone.Steam;

public class SteamOptions
{
    public string Key { get; set; } = null!;
    public string Destination { get; set; } = null!;
    public string? LastStamp { get; set; }
}
