using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BedrockLauncher.UpdateProcessor.Classes
{
    /// <summary>
    /// Downloads an HTTP resource to a temporary file and resumes interrupted transfers only when the server
    /// supplied a stable validator (a strong ETag or Last-Modified). The destination is replaced only after the
    /// complete response length has been verified.
    /// </summary>
    public sealed class ResumableHttpDownloader
    {
        private readonly HttpClient httpClient;

        public ResumableHttpDownloader(HttpClient httpClient)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task DownloadAsync(
            string url,
            string destination,
            Action<long, long> progress,
            CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("A valid HTTP or HTTPS download URL is required.", nameof(url));
            }

            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("A destination path is required.", nameof(destination));

            string fullDestination = Path.GetFullPath(destination);
            string destinationDirectory = Path.GetDirectoryName(fullDestination);
            Directory.CreateDirectory(destinationDirectory);

            string temporaryPath = fullDestination + ".download";
            string metadataPath = temporaryPath + ".resume.json";
            string urlFingerprint = Fingerprint(url);

            ResumeMetadata metadata = ReadMetadata(metadataPath);
            long partialLength = File.Exists(temporaryPath)
                ? new FileInfo(temporaryPath).Length
                : 0;

            bool canResume =
                partialLength > 0 &&
                metadata != null &&
                string.Equals(metadata.UrlFingerprint, urlFingerprint, StringComparison.Ordinal) &&
                HasUsableValidator(metadata) &&
                (metadata.ExpectedLength <= 0 || partialLength <= metadata.ExpectedLength);

            if (!canResume)
            {
                ResetPartial(temporaryPath, metadataPath);
                partialLength = 0;
                metadata = null;
            }
            else if (metadata.ExpectedLength > 0 && partialLength == metadata.ExpectedLength)
            {
                PromoteCompletedFile(temporaryPath, fullDestination, metadataPath);
                progress?.Invoke(partialLength, partialLength);
                return;
            }

            // A stale or malformed range response gets one clean, full-download retry.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long offset = canResume && attempt == 0
                    ? partialLength
                    : 0;

                using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                {
                    if (offset > 0)
                    {
                        request.Headers.Range = new RangeHeaderValue(offset, null);
                        request.Headers.IfRange = BuildIfRange(metadata);
                    }

                    using (HttpResponseMessage response = await httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken))
                    {
                        if (offset > 0 &&
                            response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                        {
                            ResetPartial(temporaryPath, metadataPath);
                            canResume = false;
                            continue;
                        }

                        response.EnsureSuccessStatusCode();

                        bool append = false;
                        long expectedLength = -1;
                        ResumeMetadata nextMetadata;

                        if (offset > 0 && response.StatusCode == HttpStatusCode.PartialContent)
                        {
                            ContentRangeHeaderValue range = response.Content.Headers.ContentRange;
                            bool validRange =
                                range != null &&
                                string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) &&
                                range.From.HasValue &&
                                range.From.Value == offset;

                            bool validatorsMatch = ValidatorsMatch(metadata, response);
                            bool lengthMatches =
                                !metadata.ExpectedLength.HasValue ||
                                !range?.Length.HasValue == true ||
                                metadata.ExpectedLength.Value == range.Length.Value;

                            if (!validRange || !validatorsMatch || !lengthMatches)
                            {
                                ResetPartial(temporaryPath, metadataPath);
                                canResume = false;
                                continue;
                            }

                            append = true;
                            expectedLength =
                                range.Length ??
                                metadata.ExpectedLength ??
                                -1;

                            if (expectedLength <= 0 && response.Content.Headers.ContentLength.HasValue)
                                expectedLength = offset + response.Content.Headers.ContentLength.Value;

                            nextMetadata = CreateMetadata(urlFingerprint, response, expectedLength, metadata);
                        }
                        else if (response.StatusCode == HttpStatusCode.OK)
                        {
                            // If-Range failed or the server does not support ranges. The 200 body starts at byte 0.
                            offset = 0;
                            expectedLength = response.Content.Headers.ContentLength ?? -1;
                            nextMetadata = CreateMetadata(urlFingerprint, response, expectedLength, null);
                        }
                        else
                        {
                            throw new IOException(
                                $"Unexpected HTTP response {(int)response.StatusCode} ({response.StatusCode}) for download.");
                        }

                        await WriteMetadataAsync(metadataPath, nextMetadata);

                        long transferred = offset;
                        long progressTotal = expectedLength > 0
                            ? expectedLength
                            : Math.Max(transferred, 1);

                        progress?.Invoke(transferred, progressTotal);

                        using (Stream input = await response.Content.ReadAsStreamAsync())
                        using (var output = new FileStream(
                            temporaryPath,
                            append ? FileMode.Append : FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            65536,
                            useAsync: true))
                        {
                            byte[] buffer = new byte[1024 * 1024];

                            while (true)
                            {
                                int read = await input.ReadAsync(
                                    buffer,
                                    0,
                                    buffer.Length,
                                    cancellationToken);

                                if (read == 0)
                                    break;

                                await output.WriteAsync(
                                    buffer,
                                    0,
                                    read,
                                    cancellationToken);

                                transferred += read;

                                if (expectedLength > 0 && transferred > expectedLength)
                                {
                                    throw new InvalidDataException(
                                        $"The server sent more data than declared ({transferred} of {expectedLength} bytes).");
                                }

                                progress?.Invoke(
                                    transferred,
                                    expectedLength > 0 ? expectedLength : transferred);
                            }

                            await output.FlushAsync(cancellationToken);
                        }

                        if (!File.Exists(temporaryPath) || transferred == 0)
                            throw new IOException("The downloaded package is empty or missing.");

                        if (expectedLength > 0 && transferred != expectedLength)
                        {
                            throw new IOException(
                                $"The download is incomplete: {transferred} of {expectedLength} bytes. " +
                                "The partial file has been retained when the server supports safe resuming.");
                        }

                        PromoteCompletedFile(temporaryPath, fullDestination, metadataPath);

                        progress?.Invoke(
                            transferred,
                            expectedLength > 0 ? expectedLength : transferred);

                        return;
                    }
                }
            }

            throw new IOException("The server did not provide a valid response for a resumable download.");
        }

        private static ResumeMetadata CreateMetadata(
            string urlFingerprint,
            HttpResponseMessage response,
            long expectedLength,
            ResumeMetadata previous)
        {
            string etag = response.Headers.ETag != null &&
                          !response.Headers.ETag.IsWeak
                ? response.Headers.ETag.ToString()
                : previous?.ETag;

            string lastModified = GetLastModified(response) ?? previous?.LastModifiedUtc;

            return new ResumeMetadata
            {
                UrlFingerprint = urlFingerprint,
                ETag = etag,
                LastModifiedUtc = lastModified,
                ExpectedLength = expectedLength > 0
                    ? expectedLength
                    : (previous?.ExpectedLength ?? -1)
            };
        }

        private static string GetLastModified(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("Last-Modified", out var values))
                return values.FirstOrDefault();

            if (response.Content.Headers.TryGetValues("Last-Modified", out values))
                return values.FirstOrDefault();

            return null;
        }

        private static bool ValidatorsMatch(
            ResumeMetadata previous,
            HttpResponseMessage response)
        {
            if (!string.IsNullOrWhiteSpace(previous.ETag) &&
                response.Headers.ETag != null &&
                !string.Equals(previous.ETag, response.Headers.ETag.ToString(), StringComparison.Ordinal))
            {
                return false;
            }

            string lastModified = GetLastModified(response);
            if (!string.IsNullOrWhiteSpace(previous.LastModifiedUtc) &&
                !string.IsNullOrWhiteSpace(lastModified) &&
                !string.Equals(previous.LastModifiedUtc, lastModified, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static RangeConditionHeaderValue BuildIfRange(ResumeMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.ETag) &&
                EntityTagHeaderValue.TryParse(metadata.ETag, out EntityTagHeaderValue entityTag) &&
                !entityTag.IsWeak)
            {
                return new RangeConditionHeaderValue(entityTag);
            }

            if (!string.IsNullOrWhiteSpace(metadata.LastModifiedUtc) &&
                DateTimeOffset.TryParse(
                    metadata.LastModifiedUtc,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset lastModified))
            {
                return new RangeConditionHeaderValue(lastModified);
            }

            throw new InvalidDataException("The partial download has no usable HTTP validator.");
        }

        private static bool HasUsableValidator(ResumeMetadata metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.ETag) &&
                EntityTagHeaderValue.TryParse(metadata.ETag, out EntityTagHeaderValue entityTag) &&
                !entityTag.IsWeak)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(metadata.LastModifiedUtc) &&
                   DateTimeOffset.TryParse(
                       metadata.LastModifiedUtc,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.AssumeUniversal,
                       out _);
        }

        private static ResumeMetadata ReadMetadata(string metadataPath)
        {
            if (!File.Exists(metadataPath))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ResumeMetadata>(
                    File.ReadAllText(metadataPath));
            }
            catch (Exception ex) when (
                ex is IOException ||
                ex is UnauthorizedAccessException ||
                ex is JsonException)
            {
                return null;
            }
        }

        private static Task WriteMetadataAsync(
            string metadataPath,
            ResumeMetadata metadata)
        {
            string temporaryMetadataPath = metadataPath + ".tmp";
            string json = JsonSerializer.Serialize(metadata);
            File.WriteAllText(temporaryMetadataPath, json);
            File.Move(temporaryMetadataPath, metadataPath, true);
            return Task.CompletedTask;
        }

        private static string Fingerprint(string url)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(url));
                return Convert.ToHexString(hash);
            }
        }

        private static void PromoteCompletedFile(
            string temporaryPath,
            string destination,
            string metadataPath)
        {
            if (!File.Exists(temporaryPath))
                throw new IOException("The completed download file does not exist.");

            File.Move(temporaryPath, destination, true);
            DeleteIfExists(metadataPath);
            DeleteIfExists(metadataPath + ".tmp");
        }

        private static void ResetPartial(string temporaryPath, string metadataPath)
        {
            DeleteIfExists(temporaryPath);
            DeleteIfExists(metadataPath);
            DeleteIfExists(metadataPath + ".tmp");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        private sealed class ResumeMetadata
        {
            public string UrlFingerprint { get; set; }
            public string ETag { get; set; }
            public string LastModifiedUtc { get; set; }
            public long ExpectedLength { get; set; } = -1;
        }
    }
}
