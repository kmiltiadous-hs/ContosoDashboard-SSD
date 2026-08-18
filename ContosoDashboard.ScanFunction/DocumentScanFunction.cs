using System.Text.Json;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContosoDashboard.ScanFunction;

public class DocumentScanFunction
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly IMalwareScanner _malwareScanner;
    private readonly ILogger<DocumentScanFunction> _logger;

    public DocumentScanFunction(
        ApplicationDbContext dbContext,
        IFileStorageService fileStorageService,
        IMalwareScanner malwareScanner,
        ILogger<DocumentScanFunction> logger)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _malwareScanner = malwareScanner;
        _logger = logger;
    }

    [Function(nameof(DocumentScanFunction))]
    public async Task RunAsync(
        [QueueTrigger("document-scan-queue", Connection = "QueueStorage")] string message,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ScanRequestMessage>(message)
            ?? throw new InvalidOperationException("Scan request message could not be deserialized.");

        var document = await _dbContext.Documents.FindAsync(new object[] { request.DocumentId }, cancellationToken);
        if (document is null)
        {
            _logger.LogWarning("DocumentScanFunction: document {DocumentId} not found, skipping scan.", request.DocumentId);
            return;
        }

        ScanResult scanResult;
        await using (var content = await _fileStorageService.DownloadAsync(document.FilePath))
        {
            scanResult = await _malwareScanner.ScanAsync(content, cancellationToken);
        }

        if (scanResult.IsClean)
        {
            document.ScanStatus = DocumentScanStatus.Available;
            document.UpdatedDate = DateTime.UtcNow;
        }
        else
        {
            await _fileStorageService.DeleteAsync(document.FilePath);
            document.ScanStatus = DocumentScanStatus.Rejected;
            document.UpdatedDate = DateTime.UtcNow;

            _dbContext.DocumentActivityLogs.Add(new DocumentActivityLog
            {
                DocumentId = document.DocumentId,
                UserId = document.UploadedByUserId,
                ActivityType = DocumentActivityType.ScanRejected,
                Timestamp = DateTime.UtcNow
            });

            _logger.LogWarning(
                "DocumentScanFunction: document {DocumentId} rejected by scan (threat: {ThreatName}).",
                request.DocumentId, scanResult.ThreatName);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private record ScanRequestMessage(int DocumentId, string StoragePath);
}
