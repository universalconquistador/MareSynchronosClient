using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto, CancellationToken token) : base(dto)
    {
        _manualCancellationToken = CancellationTokenSource.CreateLinkedTokenSource(token);
    }

    public string LocalFile { get; set; } = string.Empty;
    public override long Total { get; set; }
    public CancellationToken CancellationToken => _manualCancellationToken.Token;
    private CancellationTokenSource _manualCancellationToken { get; }
    public bool Skip { get; set; }
    public Task CompletionTask { get; set; } 

    public void Cancel()
    {
        _manualCancellationToken.Cancel();
    }
}