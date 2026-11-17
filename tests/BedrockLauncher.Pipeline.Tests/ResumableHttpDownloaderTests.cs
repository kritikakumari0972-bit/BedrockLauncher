using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using BedrockLauncher.UpdateProcessor.Classes;
using Xunit;

namespace BedrockLauncher.Pipeline.Tests
{
    public class ResumableHttpDownloaderTests
    {
        [Fact]
        public async Task InterruptedDownloadResumesFromTheLastWrittenByte()
        {
            string root = CreateTempDirectory();
            string destination = Path.Combine(root, "package.msixvc");
            const string url = "https://downloads.example.invalid/package.msixvc";
            byte[] package = Enumerable.Range(0, 20).Select(x => (byte)x).ToArray();

            try
            {
                using (var firstClient = new HttpClient(new DelegateHandler(_ => {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FailsAfterBytesStream(package, 7))
                    };
                    response.Content.Headers.ContentLength = package.Length;
                    response.Headers.ETag = new EntityTagHeaderValue("\"package-v1\"");
                    return response;
                })))
                {
                    var downloader = new ResumableHttpDownloader(firstClient);

                    await Assert.ThrowsAsync<IOException>(() =>
                        downloader.DownloadAsync(url, destination, null, CancellationToken.None));
                }

                string partialPath = destination + ".download";
                Assert.True(File.Exists(partialPath));
                Assert.Equal(7L, new FileInfo(partialPath).Length);
                Assert.False(File.Exists(destination));

                bool rangeRequested = false;
                using (var secondClient = new HttpClient(new DelegateHandler(request => {
                    rangeRequested = request.Headers.Range?.Ranges.SingleOrDefault()?.From == 7;
                    Assert.Equal("\"package-v1\"", request.Headers.IfRange?.EntityTag?.ToString());

                    var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                    {
                        Content = new ByteArrayContent(package.Skip(7).ToArray())
                    };
                    response.Content.Headers.ContentRange =
                        new ContentRangeHeaderValue(7, package.Length - 1, package.Length);
                    response.Headers.ETag = new EntityTagHeaderValue("\"package-v1\"");
                    return response;
                })))
                {
                    var downloader = new ResumableHttpDownloader(secondClient);
                    await downloader.DownloadAsync(url, destination, null, CancellationToken.None);
                }

                Assert.True(rangeRequested);
                Assert.Equal(package, await File.ReadAllBytesAsync(destination));
                Assert.False(File.Exists(partialPath));
                Assert.False(File.Exists(partialPath + ".resume.json"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task ServerReturningFullResponseRestartsInsteadOfAppendingToPartialFile()
        {
            string root = CreateTempDirectory();
            string destination = Path.Combine(root, "package.msixvc");
            const string url = "https://downloads.example.invalid/package.msixvc";
            byte[] oldPackage = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
            byte[] newPackage = Enumerable.Range(50, 16).Select(x => (byte)x).ToArray();

            try
            {
                using (var firstClient = new HttpClient(new DelegateHandler(_ => {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FailsAfterBytesStream(oldPackage, 5))
                    };
                    response.Content.Headers.ContentLength = oldPackage.Length;
                    response.Headers.ETag = new EntityTagHeaderValue("\"package-v1\"");
                    return response;
                })))
                {
                    var downloader = new ResumableHttpDownloader(firstClient);
                    await Assert.ThrowsAsync<IOException>(() =>
                        downloader.DownloadAsync(url, destination, null, CancellationToken.None));
                }

                bool rangeRequested = false;
                using (var secondClient = new HttpClient(new DelegateHandler(request => {
                    rangeRequested = request.Headers.Range != null;
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(newPackage)
                    };
                    response.Headers.ETag = new EntityTagHeaderValue("\"package-v2\"");
                    return response;
                })))
                {
                    var downloader = new ResumableHttpDownloader(secondClient);
                    await downloader.DownloadAsync(url, destination, null, CancellationToken.None);
                }

                Assert.True(rangeRequested);
                Assert.Equal(newPackage, await File.ReadAllBytesAsync(destination));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task PartialWithoutStableValidatorIsDiscardedBeforeRetry()
        {
            string root = CreateTempDirectory();
            string destination = Path.Combine(root, "package.msixvc");
            const string url = "https://downloads.example.invalid/package.msixvc";
            byte[] package = Enumerable.Range(30, 18).Select(x => (byte)x).ToArray();

            try
            {
                using (var firstClient = new HttpClient(new DelegateHandler(_ => {
                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(new FailsAfterBytesStream(package, 4))
                    };
                    response.Content.Headers.ContentLength = package.Length;
                    // Deliberately no ETag or Last-Modified: the partial bytes cannot be safely resumed.
                    return response;
                })))
                {
                    var downloader = new ResumableHttpDownloader(firstClient);
                    await Assert.ThrowsAsync<IOException>(() =>
                        downloader.DownloadAsync(url, destination, null, CancellationToken.None));
                }

                bool rangeRequested = false;
                using (var secondClient = new HttpClient(new DelegateHandler(request => {
                    rangeRequested = request.Headers.Range != null;
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(package)
                    };
                })))
                {
                    var downloader = new ResumableHttpDownloader(secondClient);
                    await downloader.DownloadAsync(url, destination, null, CancellationToken.None);
                }

                Assert.False(rangeRequested);
                Assert.Equal(package, await File.ReadAllBytesAsync(destination));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static string CreateTempDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "BedrockLauncher-DownloadTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private sealed class DelegateHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> callback;

            public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
            {
                this.callback = callback;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(callback(request));
            }
        }

        private sealed class FailsAfterBytesStream : Stream
        {
            private readonly byte[] bytes;
            private readonly int limit;
            private int position;
            private bool failed;

            public FailsAfterBytesStream(byte[] bytes, int limit)
            {
                this.bytes = bytes;
                this.limit = limit;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => bytes.Length;
            public override long Position { get => position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count) =>
                ReadCore(buffer, offset, count);

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(ReadCore(buffer, offset, count));
            }

            private int ReadCore(byte[] buffer, int offset, int count)
            {
                if (position >= limit)
                {
                    if (!failed)
                    {
                        failed = true;
                        throw new IOException("Simulated connection interruption.");
                    }

                    return 0;
                }

                int read = Math.Min(count, limit - position);
                read = Math.Min(read, bytes.Length - position);
                Buffer.BlockCopy(bytes, position, buffer, offset, read);
                position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() { }
        }
    }
}
