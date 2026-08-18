namespace ContosoDashboard.Services;

public interface IDocumentScanQueueClient
{
    Task EnqueueScanRequestAsync(int documentId, string storagePath, CancellationToken cancellationToken = default);
}
