using System.Text.Json;
using Azure.Storage.Queues;

namespace ContosoDashboard.Services;

// Sends a scan-request message (documentId + storagePath only — no file bytes/PII) to Azure Storage Queue,
// backed by Azurite for offline/local dev; the DocumentScanFunction dequeues and processes it.
public class AzureDocumentScanQueueClient : IDocumentScanQueueClient
{
    private readonly QueueClient _queueClient;

    public AzureDocumentScanQueueClient(IConfiguration configuration)
    {
        var connectionString = configuration["QueueStorage:ConnectionString"]
            ?? throw new InvalidOperationException("QueueStorage:ConnectionString is not configured.");
        var queueName = configuration["QueueStorage:ScanQueueName"] ?? "document-scan-queue";

        // Pin to the latest API version supported by Azurite (local dev emulator lags behind the SDK default).
        var options = new QueueClientOptions(QueueClientOptions.ServiceVersion.V2025_11_05);
        _queueClient = new QueueClient(connectionString, queueName, options);
        _queueClient.CreateIfNotExists();
    }

    public async Task EnqueueScanRequestAsync(int documentId, string storagePath, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new ScanRequestMessage(documentId, storagePath));
        await _queueClient.SendMessageAsync(message, cancellationToken);
    }

    public record ScanRequestMessage(int DocumentId, string StoragePath);
}
