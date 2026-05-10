namespace DocumentExtractionService.Api.Services
{
    using System.Text;
    using System.Text.Json;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using DocumentExtractionService.Api.Services;

    public sealed class ApproveBlobWorker : BackgroundService
    {
        private readonly IApproveQueue _queue;
        private readonly IConfiguration _config;
        private readonly ILogger<ApproveBlobWorker> _logger;
        private readonly HttpClient _http = new();

        public ApproveBlobWorker(
            IApproveQueue queue,
            IConfiguration config,
            ILogger<ApproveBlobWorker> logger)
        {
            _queue = queue;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_queue.TryDequeue(out var item))
                    {
                        await SaveAsync(item.requestId, item.data, stoppingToken);
                    }
                    else
                    {
                        // Nothing to do; avoid tight loop
                        await Task.Delay(500, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // graceful shutdown
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ApproveBlobWorker loop error");
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }

        private async Task SaveAsync(string requestId, object data, CancellationToken ct)
        {
            var sasUrl = _config["Blob:ContainerSasUrl"];
            if (string.IsNullOrWhiteSpace(sasUrl))
            {
                _logger.LogWarning("Blob:ContainerSasUrl not configured. Skipping blob save.");
                return;
            }

            var containerUri = new Uri(sasUrl);
            var blobUrl =
                $"{containerUri.GetLeftPart(UriPartial.Path)}/invoice-edits/{requestId}.json{containerUri.Query}";

            var json = JsonSerializer.Serialize(data);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Put, blobUrl);
            request.Headers.Add("x-ms-blob-type", "BlockBlob");
            request.Content = content;

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Blob save failed for RequestId={RequestId}. Status={Status}",
                    requestId, response.StatusCode);
            }
            else
            {
                _logger.LogInformation(
                    "Blob save succeeded for RequestId={RequestId}",
                    requestId);
            }
        }
    }

}
