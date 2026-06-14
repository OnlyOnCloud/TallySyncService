using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace TallySyncService.Services;

public class BackendUploadService
{
    private readonly string _backendUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _logger;

    public BackendUploadService(string backendUrl, IHttpClientFactory httpClientFactory, ILogger logger)
    {
        _backendUrl = backendUrl.Trim();
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Zips all exported CSV files into a single in-memory archive and
    /// uploads it to the backend as multipart/form-data in one request.
    /// Returns the number of files successfully included in the zip (0 on failure).
    /// </summary>
    public async Task<int> UploadMultipleCsvFilesAsync(
        List<string> csvFiles,
        int organisationId)
    {
        if (csvFiles.Count == 0)
        {
            _logger.LogWarning("No CSV files to upload");
            return 0;
        }

        try
        {
            // Build zip archive in memory
            using var zipStream = new MemoryStream();
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var csvFile in csvFiles)
                {
                    var entryName = Path.GetFileName(csvFile); // e.g. "Ledger.csv"
                    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    await using var entryStream = entry.Open();
                    await using var fileStream = File.OpenRead(csvFile);
                    await fileStream.CopyToAsync(entryStream);

                    _logger.LogInformation("  Packed: {FileName}", entryName);
                }
            }

            zipStream.Position = 0;
            var zipBytes = zipStream.ToArray();

            _logger.LogInformation("Zip created: {FileCount} files, {SizeKb} KB",
                csvFiles.Count, zipBytes.Length / 1024);

            // Build multipart/form-data request
            using var form = new MultipartFormDataContent();

            // Attach zip file
            var zipContent = new ByteArrayContent(zipBytes);
            zipContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            form.Add(zipContent, "file", $"tally_export_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");

            // Attach metadata fields
            form.Add(new StringContent(organisationId.ToString()), "organisationId");
            form.Add(new StringContent(csvFiles.Count.ToString()), "fileCount");
            form.Add(new StringContent(DateTime.UtcNow.ToString("o")), "exportedAt");

            // Build request with auth headers
            var request = new HttpRequestMessage(HttpMethod.Post, _backendUrl)
            {
                Content = form
            };

            var token = AuthService.LoadToken();
            if (!string.IsNullOrEmpty(token))
                request.Headers.Add("Authorization", token);

            var orgId = AuthService.LoadOrganisationId();
            if (orgId.HasValue)
                request.Headers.Add("orgid", orgId.Value.ToString());

            // Send
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMinutes(10);

            var response = await httpClient.SendAsync(request);

            // ALWAYS read response body
            var responseBody = await response.Content.ReadAsStringAsync();

            // Log everything cleanly
            _logger.LogInformation(
                "Response received: Status={StatusCode}, Reason={Reason}, Body={Body}",
                (int)response.StatusCode,
                response.ReasonPhrase,
                responseBody
            );

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Upload successful: {FileCount} files in one zip ({SizeKb} KB)",
                    csvFiles.Count,
                    zipBytes.Length / 1024
                );

                return csvFiles.Count;
            }
            else
            {
                _logger.LogWarning(
                    "Upload failed: Status={StatusCode}, Body={Body}",
                    (int)response.StatusCode,
                    responseBody
                );

                return 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating or uploading zip");
            return 0;
        }
    }
}
