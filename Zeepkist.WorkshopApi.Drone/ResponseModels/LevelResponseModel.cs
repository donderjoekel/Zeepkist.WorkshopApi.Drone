namespace TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;

public class LevelResponseModel
{
    public int Id { get; set; }
    public int? ReplacedBy { get; set; }
    public bool Deleted { get; set; }
    public string WorkshopId { get; set; } = null!;
    public string AuthorId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ImageUrl { get; set; } = null!;
    public string FileUrl { get; set; } = null!;
    public string FileUid { get; set; } = null!;
    public string FileHash { get; set; } = null!;
    public string FileAuthor { get; set; } = null!;
}
