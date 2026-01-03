using MareSynchronos.FileCache;
using MareSynchronos.Services.Mediator;
using MareSynchronos.WebAPI.Files;
using Microsoft.Extensions.Logging;
using PlayerSync.FileCache;

namespace MareSynchronos.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly ICompressedAlternateManager _compressedAlternateManager;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, MareMediator mareMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor, ICompressedAlternateManager compressedAlternateManager)
    {
        _loggerFactory = loggerFactory;
        _mareMediator = mareMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
        _compressedAlternateManager = compressedAlternateManager;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _mareMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor, _compressedAlternateManager);
    }
}