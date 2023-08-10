using FluentResults;

namespace TNRD.Zeepkist.WorkshopApi.Drone.Google;

public interface IUploadService
{
    Task<Result<string>> UploadLevel(string identifier, byte[] buffer, CancellationToken ct = default);
    Task<Result<string>> UploadThumbnail(string identifier, byte[] buffer, CancellationToken ct = default);
}
