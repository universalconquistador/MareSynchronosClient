﻿using MareSynchronos.API.Dto.Files;

namespace MareSynchronos.WebAPI.Files.Models;

public class DownloadFileTransfer : FileTransfer
{
    public DownloadFileTransfer(DownloadFileDto dto) : base(dto)
    {
    }

    public override bool CanBeTransferred => Dto.FileExists && !Dto.IsForbidden && Dto.Size > 0;
    public override long Total
    {
        set
        {
            // nothing to set
        }
        get => Dto.Size;
    }
    public string? DirectDownloadUrl => ((DownloadFileDto)TransferDto).DirectDownloadUrl;

    public long TotalRaw => Dto.RawSize;
    private DownloadFileDto Dto => (DownloadFileDto)TransferDto;
}