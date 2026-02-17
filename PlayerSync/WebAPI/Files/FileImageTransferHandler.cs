using MareSynchronos.API.Dto.Files;
using MareSynchronos.API.Routes;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace MareSynchronos.WebAPI.Files;

public class FileImageTransferHandler
{   
    private readonly ILogger<FileImageTransferHandler> _logger;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;

    public FileImageTransferHandler(ILogger<FileImageTransferHandler> logger, FileTransferOrchestrator fileTransferOrchestrator)
    {
        _logger = logger;
        _fileTransferOrchestrator = fileTransferOrchestrator;
    }

    /// <summary>
    /// Gets profile image bytes as a background task.
    /// </summary>
    /// <param name="uid">UID of the player to get profile image of.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="setImageBytes">Byte array to be used by the UI thread to load an image texture.</param>
    /// <returns></returns>
    public async Task DownloadProfileImageAsync(string uid, CancellationToken ct, Action<byte[]> setImageBytes)
    {
        try
        {
            var profileImageDto = await GetProfileImageLinksForUidAsync(uid, ct).ConfigureAwait(false);

            var downloadUrl = profileImageDto.ProfileProfileDownloadUrl;
            if (string.IsNullOrWhiteSpace(downloadUrl))
                return;

            var imageBytes = await DownloadImageBytesAsync(downloadUrl, ct).ConfigureAwait(false);
            if (imageBytes is not { Length: > 0 })
                return;

            setImageBytes(imageBytes);
        }
        catch (OperationCanceledException)
        {
            //
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download profile image for {uid}", uid);
        }
    }

    public async Task<HttpResponseMessage> UploadProfileImagePngAsync(string imageUsage, byte[] imageBytes, CancellationToken ct)
    {
        var requestUri = MareFiles.ServerFilesProfileImageUpload(_fileTransferOrchestrator.FilesCdnUri!, imageUsage);

        var byteArrayContent = new ByteArrayContent(imageBytes);
        byteArrayContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        return await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Post, requestUri, byteArrayContent, ct, withToken: true).ConfigureAwait(false);
    }

    public Task<HttpResponseMessage> DeleteProfileImageAsync(string imageUsage, CancellationToken ct)
    {
        var requestUri = MareFiles.ServerFilesProfileImageDelete(_fileTransferOrchestrator.FilesCdnUri!, imageUsage);
        return _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Delete, requestUri, ct, withToken: true);
    }

    private async Task<byte[]?> DownloadImageBytesAsync(string downloadUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        // make sure to set withToken to false else we get errors downloading from R2
        using var response = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, new Uri(downloadUrl), ct,
            httpCompletionOption: HttpCompletionOption.ResponseHeadersRead, withToken: false).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<ProfileImagesDto> GetProfileImageLinksForUidAsync(string uid, CancellationToken ct)
    {
        var requestUri = MareFiles.ServerFilesProfileImageDownload(_fileTransferOrchestrator.FilesCdnUri!, uid);

        using var response = await _fileTransferOrchestrator.SendRequestAsync(HttpMethod.Get, requestUri, ct, withToken: true).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new ProfileImagesDto();

        var dto = await response.Content.ReadFromJsonAsync<ProfileImagesDto>(cancellationToken: ct).ConfigureAwait(false);
        return dto ?? new ProfileImagesDto();
    }
}
