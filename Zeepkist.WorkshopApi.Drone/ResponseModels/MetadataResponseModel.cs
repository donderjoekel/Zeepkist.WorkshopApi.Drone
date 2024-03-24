namespace TNRD.Zeepkist.WorkshopApi.Drone.ResponseModels;

public class MetadataResponseModel
{
    public int Id { get; set; }
    public string Hash { get; set; } = null!;
    public int Checkpoints { get; set; }
    public string Blocks { get; set; } = null!;
    public bool Valid { get; set; }
    public float Validation { get; set; }
    public float Gold { get; set; }
    public float Silver { get; set; }
    public float Bronze { get; set; }
    public int Ground { get; set; }
    public int Skybox { get; set; }
}
