namespace TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;

public class LevelResponseModel
{
    public int Id { get; set; }
    public string WorkshopId { get; set; } = null!;
    public string AuthorId { get; set; } = null!;
    public string ModioId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ImageUrl { get; set; } = null!;
    public string FileUrl { get; set; } = null!;
    public string FileUid { get; set; } = null!;
    public string FileHash { get; set; } = null!;
    public string FileAuthor { get; set; } = null!;
    public bool Valid { get; set; }
    public float Validation { get; set; }
    public float Gold { get; set; }
    public float Silver { get; set; }
    public float Bronze { get; set; }
}