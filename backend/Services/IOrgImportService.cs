using MEval.Api.DTOs;

namespace MEval.Api.Services;

public interface IOrgImportService
{
    byte[] GenerateTemplate();

    Task<(bool Success, OrgImportDryRunResultDto? Result, string? ErrorReason, int StatusCode)> DryRunAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        int actorUserId);

    Task<(bool Success, OrgImportExecuteResponse? Response, string? ErrorReason, int StatusCode)> ExecuteAsync(
        Stream fileStream,
        string fileName,
        long fileSize,
        int actorUserId,
        string actorIpAddress);
}
