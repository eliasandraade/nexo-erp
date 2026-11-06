using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Nexo.Application.Features.Auth;
using Nexo.Application.Integrations.Contracts;
using Nexo.Application.Integrations.DTOs;
using Nexo.IntegrationTests.Common;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.Storage;

/// <summary>
/// Upload tests run on a real Kestrel socket, one at a time: TestServer has no request body limit feature, and these tests watch a
/// temp folder shared by the whole process.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UploadKestrelCollection : ICollectionFixture<UploadKestrelFactory>
{
    public const string Name = "UploadKestrel";
}

/// <summary>
/// The dedicated folder ASP.NET Core spools multipart files to. The framework caches ASPNETCORE_TEMP on first use, so it is set when
/// this assembly loads, before anything can read it.
/// </summary>
internal static class UploadTempFolder
{
    public static string Path { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nexo-upload-it-" + Environment.ProcessId);
        Directory.CreateDirectory(Path);
        Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", Path);
        // the whole process uses this folder, other collections included: it can only go away with the process
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* something still open: it stays in the machine's temp folder */ }
        };
    }

    /// <summary>What an upload leaves on disk: the framework's spool files and the Build buffers.</summary>
    public static FileInfo[] UploadFiles()
        => new DirectoryInfo(Path).GetFiles()
            .Where(f => f.Name.StartsWith("ASPNETCORE_", StringComparison.Ordinal) || f.Name.StartsWith("orken-", StringComparison.Ordinal))
            .ToArray();
}

/// <summary>
/// One Postgres for the collection; each <see cref="StartAsync"/> starts a real Kestrel host with its own limits. WebApplicationFactory
/// insists on returning a TestServer host, so the same builder is built twice: the TestServer one is what the factory keeps, the Kestrel
/// one is what the tests call.
/// </summary>
public sealed class UploadKestrelFactory : TestWebApplicationFactory
{
    private readonly ConcurrentBag<IHost> _kestrelHosts = [];
    private Uri? _lastAddress;
    private string? _previousTmp, _previousTemp, _previousTmpDir;

    public override async Task InitializeAsync()
    {
        // Build buffers use the machine's temp folder: pointed at the watched folder while this collection runs
        _previousTmp = Environment.GetEnvironmentVariable("TMP");
        _previousTemp = Environment.GetEnvironmentVariable("TEMP");
        _previousTmpDir = Environment.GetEnvironmentVariable("TMPDIR");
        await base.InitializeAsync();
        Environment.SetEnvironmentVariable("TMP", UploadTempFolder.Path);
        Environment.SetEnvironmentVariable("TEMP", UploadTempFolder.Path);
        Environment.SetEnvironmentVariable("TMPDIR", UploadTempFolder.Path);
    }

    public override async Task DisposeAsync()
    {
        Environment.SetEnvironmentVariable("TMP", _previousTmp);
        Environment.SetEnvironmentVariable("TEMP", _previousTemp);
        Environment.SetEnvironmentVariable("TMPDIR", _previousTmpDir);
        foreach (var host in _kestrelHosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
        await base.DisposeAsync();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var testServerHost = builder.Build();
        builder.ConfigureWebHost(web => web.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0)));
        var kestrelHost = builder.Build();
        kestrelHost.Start();
        _kestrelHosts.Add(kestrelHost);
        _lastAddress = new Uri(kestrelHost.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        testServerHost.Start();
        return testServerHost;
    }

    /// <summary>A real Kestrel host with storage on, an in-memory object store and a counter on every request body.</summary>
    public Task<UploadHost> StartAsync(RecordingStorage? storage = null, params (string Key, string Value)[] settings)
    {
        var recording = storage ?? new RecordingStorage();
        var probe = new BodyProbe();
        var host = WithWebHostBuilder(b =>
        {
            b.UseSetting("Integrations:Features:StorageEnabled", "true");
            // limits are off unless a test asks for them, so a refusal test proves only what it names
            b.UseSetting("RateLimiting:Uploads:Enabled", "false");
            b.UseSetting("Uploads:InFlight:Enabled", "false");
            foreach (var (key, value) in settings) b.UseSetting(key, value);
            b.ConfigureTestServices(s =>
            {
                s.RemoveAll<IStorageProvider>();
                s.AddSingleton<IStorageProvider>(recording);
                s.AddSingleton<IStartupFilter>(new BodyProbeFilter(probe));
            });
        });
        _ = host.Services;   // builds and starts both hosts
        return Task.FromResult(new UploadHost(host, _lastAddress!, recording, probe));
    }
}

/// <summary>A running Kestrel host and what the tests observe about it.</summary>
public sealed class UploadHost(WebApplicationFactory<Program> factory, Uri address, RecordingStorage storage, BodyProbe probe) : IAsyncDisposable
{
    public RecordingStorage Storage { get; } = storage;
    public BodyProbe Probe { get; } = probe;
    public WebApplicationFactory<Program> Factory { get; } = factory;

    public HttpClient Client(string? token = null, IEnumerable<KeyValuePair<string, string>>? headers = null)
    {
        var client = new HttpClient(new EarlyAnswerHandler()) { BaseAddress = address, Timeout = TimeSpan.FromSeconds(60) };
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        foreach (var header in headers ?? []) client.DefaultRequestHeaders.Add(header.Key, header.Value);
        return client;
    }

    public async Task<string> TokenAsync(string login, string password)
    {
        using var client = Client();
        var response = await client.PostAsJsonAsync("/api/auth/login", TestCredentials.LoginPayload(login, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    public async Task<HttpClient> SignedInAsync(SyntheticAccount account) => Client(await TokenAsync(account.Login, account.Password));

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}

/// <summary>The in-memory object store, counting the calls: a refusal must never reach it.</summary>
public sealed class RecordingStorage(bool refusePrivate = false) : IStorageProvider
{
    private readonly InMemoryStorageProvider _inner = new() { RefusePrivate = refusePrivate };
    private int _uploadCalls;

    public int UploadCalls => Volatile.Read(ref _uploadCalls);
    public IReadOnlyCollection<string> Keys => _inner.Objects.Keys.ToArray();

    /// <summary>While set, the upload call waits: the request stays in flight with its file on disk.</summary>
    public TaskCompletionSource? HoldUploads { get; set; }

    public bool CanStorePrivateFiles => _inner.CanStorePrivateFiles;

    public async Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _uploadCalls);
        if (HoldUploads is { } hold) await hold.Task.WaitAsync(ct);
        return await _inner.UploadAsync(request, ct);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) => _inner.DeleteAsync(key, ct);
    public Task PingAsync(CancellationToken ct = default) => _inner.PingAsync(ct);
    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => _inner.OpenReadAsync(key, ct);
    public string GetSignedUrl(string key, TimeSpan ttl) => _inner.GetSignedUrl(key, ttl);
}

/// <summary>Counts the bytes the application reads from request bodies: zero means nothing was read.</summary>
public sealed class BodyProbe
{
    private long _bytes;
    public long Bytes => Interlocked.Read(ref _bytes);
    public void Add(int count) => Interlocked.Add(ref _bytes, count);
    public void Reset() => Interlocked.Exchange(ref _bytes, 0);
}

internal sealed class BodyProbeFilter(BodyProbe probe) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (HttpContext context, RequestDelegate nextMiddleware) =>
        {
            // uploads only: the JSON calls that set a test up are not what is being measured
            if (context.Request.ContentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true)
                context.Request.Body = new CountingReadStream(context.Request.Body, probe);
            await nextMiddleware(context);
        });
        next(app);
    };

    private sealed class CountingReadStream(Stream inner, BodyProbe probe) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            probe.Add(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var read = await inner.ReadAsync(buffer, ct);
            probe.Add(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    }
}

/// <summary>
/// HTTP/1.1 over a plain socket that reads the answer while it sends the body. SocketsHttpHandler waits for the whole request body before
/// returning a response (and fails if the server closes meanwhile), so it cannot show a server answering without waiting for the body.
/// </summary>
internal sealed class EarlyAnswerHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri!;
        using var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(uri.Host, uri.Port, ct);
        var stream = tcp.GetStream();

        var content = request.Content;
        var chunked = content is not null && content.Headers.ContentLength is null;
        var head = new StringBuilder($"{request.Method} {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Authority}\r\nConnection: close\r\n");
        foreach (var (name, values) in request.Headers) head.Append($"{name}: {string.Join(", ", values)}\r\n");
        foreach (var (name, values) in content?.Headers.AsEnumerable() ?? []) head.Append($"{name}: {string.Join(", ", values)}\r\n");
        if (chunked) head.Append("Transfer-Encoding: chunked\r\n");
        if (content is null) head.Append("Content-Length: 0\r\n");
        head.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct);

        using var sending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var send = content is null ? Task.CompletedTask : SendBodyAsync(stream, content, chunked, sending.Token);
        try
        {
            return await ReadResponseAsync(stream, request, ct);
        }
        finally
        {
            await sending.CancelAsync();
            try { await send; }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or HttpRequestException or SocketException) { /* the answer is what counts */ }
        }
    }

    private static async Task SendBodyAsync(Stream stream, HttpContent content, bool chunked, CancellationToken ct)
    {
        await content.CopyToAsync(chunked ? new ChunkedWriteStream(stream) : stream, ct);
        if (chunked) await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<HttpResponseMessage> ReadResponseAsync(Stream stream, HttpRequestMessage request, CancellationToken ct)
    {
        var reader = new ResponseReader(stream);
        var status = await reader.ReadLineAsync(ct) ?? throw new HttpRequestException("The server closed the connection without answering.");
        var response = new HttpResponseMessage((HttpStatusCode)int.Parse(status.Split(' ')[1])) { RequestMessage = request, Content = new ByteArrayContent([]) };
        var headers = new List<(string Name, string Value)>();
        while (await reader.ReadLineAsync(ct) is { Length: > 0 } line)
        {
            var colon = line.IndexOf(':');
            headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        byte[] body;
        var length = headers.FirstOrDefault(h => h.Name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)).Value;
        if (headers.Any(h => h.Name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)))
            body = await reader.ReadChunkedAsync(ct);
        else if (length is not null)
            body = await reader.ReadExactlyAsync(int.Parse(length), ct);
        else
            body = await reader.ReadToEndAsync(ct);

        response.Content = new ByteArrayContent(body);
        foreach (var (name, value) in headers)
        {
            if (name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
            if (!response.Headers.TryAddWithoutValidation(name, value)) response.Content.Headers.TryAddWithoutValidation(name, value);
        }
        return response;
    }

    private sealed class ChunkedWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.IsEmpty) return;
            await inner.WriteAsync(Encoding.ASCII.GetBytes($"{buffer.Length:X}\r\n"), ct);
            await inner.WriteAsync(buffer, ct);
            await inner.WriteAsync("\r\n"u8.ToArray(), ct);
        }
    }

    private sealed class ResponseReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[16 * 1024];
        private int _start, _end;

        private async Task<bool> FillAsync(CancellationToken ct)
        {
            _start = 0;
            _end = await stream.ReadAsync(_buffer, ct);
            return _end > 0;
        }

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_start == _end && !await FillAsync(ct)) return line.Length == 0 ? null : line.ToString();
                var b = _buffer[_start++];
                if (b == '\n') return line.ToString().TrimEnd('\r');
                line.Append((char)b);
            }
        }

        public async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct)
        {
            var bytes = new byte[count];
            var read = 0;
            while (read < count)
            {
                if (_start == _end && !await FillAsync(ct)) throw new HttpRequestException("The server closed the connection in the middle of its answer.");
                var n = Math.Min(count - read, _end - _start);
                Array.Copy(_buffer, _start, bytes, read, n);
                _start += n;
                read += n;
            }
            return bytes;
        }

        public async Task<byte[]> ReadChunkedAsync(CancellationToken ct)
        {
            using var body = new MemoryStream();
            while (true)
            {
                var size = Convert.ToInt32((await ReadLineAsync(ct))!.Split(';')[0], 16);
                if (size == 0) return body.ToArray();
                body.Write(await ReadExactlyAsync(size, ct));
                await ReadLineAsync(ct);
            }
        }

        public async Task<byte[]> ReadToEndAsync(CancellationToken ct)
        {
            using var body = new MemoryStream();
            do body.Write(_buffer, _start, _end - _start);
            while (await FillAsync(ct));
            return body.ToArray();
        }
    }
}

/// <summary>Samples the temp folder while a test runs: the most it held at once, and whether it emptied.</summary>
public sealed class TempWatch : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private long _peakBytes;
    private int _peakFiles;
    private int _disposed;

    public TempWatch() => _loop = Task.Run(LoopAsync);

    public long PeakBytes => Interlocked.Read(ref _peakBytes);
    public int PeakFiles => Volatile.Read(ref _peakFiles);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            Sample();
            await Task.Delay(2);
        }
        Sample();
    }

    private void Sample()
    {
        var files = UploadTempFolder.UploadFiles();
        long bytes = 0;
        foreach (var file in files)
        {
            try { bytes += file.Length; }
            catch (IOException) { /* removed between the listing and the size */ }
        }
        if (files.Length > PeakFiles) Volatile.Write(ref _peakFiles, files.Length);
        if (bytes > PeakBytes) Interlocked.Exchange(ref _peakBytes, bytes);
    }

    /// <summary>Waits for the upload files to be gone; temp files are removed when the response completes, just after the client sees it.</summary>
    public static async Task<int> FilesLeftAfterAsync(TimeSpan? wait = null)
    {
        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(5));
        while (UploadTempFolder.UploadFiles().Length > 0 && DateTime.UtcNow < deadline) await Task.Delay(20);
        return UploadTempFolder.UploadFiles().Length;
    }

    public static async Task WaitForFilesAsync(int count, TimeSpan? wait = null)
    {
        var deadline = DateTime.UtcNow + (wait ?? TimeSpan.FromSeconds(20));
        while (UploadTempFolder.UploadFiles().Length < count)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Expected {count} upload files on disk, found {UploadTempFolder.UploadFiles().Length}.");
            await Task.Delay(10);
        }
    }

    /// <summary>Stops sampling; a test reads the peaks after this. Safe to call again.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
    }
}

/// <summary>
/// A multipart body written on demand, in the order the browser sends it (the file first). Can declare any Content-Length or none
/// (chunked), and can stall after some bytes, so a test holds a request in flight or proves the server answered without waiting.
/// </summary>
public sealed class MultipartUpload : HttpContent
{
    public static readonly byte[] PngHead = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly byte[] _prefix;
    private readonly byte[] _suffix;
    private readonly byte[] _head;
    private readonly long _fileBytes;
    private readonly long? _declared;
    private readonly bool _chunked;
    private readonly long? _stallAfter;
    private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="fileBytes">Size of the file part.</param>
    /// <param name="declared">A Content-Length to claim instead of the real size.</param>
    /// <param name="chunked">Send without a Content-Length.</param>
    /// <param name="stallAfter">Stop after this many bytes until <see cref="Resume"/> (or until the client gives up).</param>
    public MultipartUpload(long fileBytes, string contentType = "image/png", string fileName = "foto.png", byte[]? head = null,
        (string Name, string Value)[]? fields = null, long? declared = null, bool chunked = false, long? stallAfter = null)
    {
        var boundary = "----upload" + Guid.NewGuid().ToString("N");
        _prefix = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\n");
        var suffix = new StringBuilder();
        foreach (var (name, value) in fields ?? [])
            suffix.Append($"\r\n--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"\r\n\r\n{value}");
        suffix.Append($"\r\n--{boundary}--\r\n");
        _suffix = Encoding.UTF8.GetBytes(suffix.ToString());
        _head = head ?? PngHead;
        _fileBytes = fileBytes;
        _declared = declared;
        _chunked = chunked;
        _stallAfter = stallAfter;
        Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");
        Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
    }

    public long TotalBytes => _prefix.Length + _fileBytes + _suffix.Length;

    public void Resume() => _resume.TrySetResult();

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken ct)
    {
        await stream.WriteAsync(_prefix, ct);
        await stream.WriteAsync(_head.AsMemory(0, (int)Math.Min(_head.Length, _fileBytes)), ct);
        var written = _prefix.Length + Math.Min(_head.Length, _fileBytes);
        var left = _fileBytes - Math.Min(_head.Length, _fileBytes);
        var zeros = new byte[64 * 1024];
        var stalled = false;
        while (left > 0)
        {
            if (!stalled && _stallAfter is { } limit && written >= limit)
            {
                stalled = true;
                await stream.FlushAsync(ct);
                await _resume.Task.WaitAsync(ct);
            }
            var n = (int)Math.Min(left, zeros.Length);
            await stream.WriteAsync(zeros.AsMemory(0, n), ct);
            left -= n;
            written += n;
        }
        if (!stalled && _stallAfter is not null)
        {
            await stream.FlushAsync(ct);
            await _resume.Task.WaitAsync(ct);
        }
        await stream.WriteAsync(_suffix, ct);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _declared ?? TotalBytes;
        return !_chunked;
    }
}
