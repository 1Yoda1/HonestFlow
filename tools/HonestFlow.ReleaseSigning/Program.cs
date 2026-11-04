using System.Buffers;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace HonestFlow.ReleaseSigning;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "keygen")
            {
                Generate(args[1]);
                return 0;
            }
            if (args.Length == 7 && args[0] == "sign")
            {
                Sign(args[1], args[2], args[3], args[4], args[5], args[6]);
                return 0;
            }
            if ((args.Length == 7 || args.Length == 8) && args[0] == "publish")
            {
                Publish(args[1], args[2], args[3], args[4], args[5], args[6],
                    args.Length == 8 && args[7] == "--set-current").GetAwaiter().GetResult();
                return 0;
            }
            if ((args.Length == 8 || args.Length == 9) && args[0] == "publish-components")
            {
                PublishComponents(args[1], args[2], args[3], args[4], args[5], args[6], args[7],
                    args.Length == 9 && args[8] == "--set-current").GetAwaiter().GetResult();
                return 0;
            }
            if (args.Length == 4 && args[0] == "set-current")
            {
                SetCurrent(args[1], args[2], args[3]).GetAwaiter().GetResult();
                return 0;
            }
            Console.Error.WriteLine("Usage: keygen <external-key.hfrelkey> | sign <key.hfrelkey> <component> <file> <version> <architecture> <output.json> | publish <key.hfrelkey> <component> <file> <version> <architecture> <api-base> [--set-current] | publish-components <key.hfrelkey> <directory> <lm-version> <atol-version> <esm-version> <controller-version> <api-base> [--set-current] | set-current <component> <version> <api-base>");
            return 2;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                                   CryptographicException or HttpRequestException or OperationCanceledException)
        {
            Console.Error.WriteLine(DescribeException(ex));
            return 1;
        }
    }

    private static void Generate(string keyPath)
    {
        string fullPath = Path.GetFullPath(keyPath);
        EnsureKeyOutsideGit(fullPath);
        string publicPath = fullPath + ".public.json";
        if (File.Exists(fullPath) || File.Exists(publicPath))
            throw new IOException("The key or public-key file already exists; no files were replaced.");

        char[] password = ReadPassword("New release-key password: ");
        char[] confirmation = ReadPassword("Repeat release-key password: ");
        EncryptedReleaseKey? key = null;
        try
        {
            if (!password.AsSpan().SequenceEqual(confirmation))
                throw new ArgumentException("Passwords do not match; no key was created.");
            key = ReleaseSigner.CreateKey(password);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            using (FileStream privateFile = new(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                privateFile.Write(key.PrivateKey);
            using (FileStream publicFile = new(publicPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(publicFile, new
                {
                    keyId = key.KeyId,
                    publicKeyBase64 = Convert.ToBase64String(key.PublicKey)
                });
            Console.WriteLine($"Key ID: {key.KeyId}");
            Console.WriteLine($"Encrypted private key: {fullPath}");
            Console.WriteLine($"Public key: {publicPath}");
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key.PrivateKey);
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(password.AsSpan()));
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(confirmation.AsSpan()));
        }
    }

    private static void Sign(string keyPath, string component, string binaryPath, string version,
        string architecture, string outputPath)
    {
        string fullKeyPath = Path.GetFullPath(keyPath);
        EnsureKeyOutsideGit(fullKeyPath);
        long keyFileLength = new FileInfo(fullKeyPath).Length;
        if (keyFileLength is <= 0 or > 4096) throw new IOException("Invalid release key file size.");
        string? actualVersion = FileVersionInfo.GetVersionInfo(binaryPath).FileVersion;
        if (component == "HonestFlow" && !SameReleaseVersion(actualVersion, version))
            throw new ArgumentException("Executable FileVersion does not match the release version.");
        byte[] encryptedKey = File.ReadAllBytes(fullKeyPath);
        char[] password = ReadPassword("Release-key password: ");
        try
        {
            SignedReleaseEnvelope envelope = ReleaseSigner.Sign(
                encryptedKey, password, binaryPath, component, version, architecture);
            using FileStream output = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            JsonSerializer.Serialize(output, envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Console.WriteLine($"Signed release manifest: {outputPath}");
            Console.WriteLine($"Key ID: {envelope.KeyId}");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(password.AsSpan()));
            CryptographicOperations.ZeroMemory(encryptedKey);
        }
    }

    private static async Task Publish(string keyPath, string component, string binaryPath,
        string version, string architecture, string apiBase, bool setCurrent)
    {
        await PublishMany(keyPath,
            [new ReleaseSpec(component, binaryPath, version, architecture)], apiBase, setCurrent);
    }

    private static async Task PublishComponents(string keyPath, string directory, string lmVersion,
        string atolVersion, string esmVersion, string controllerVersion, string apiBase, bool setCurrent)
    {
        string fullDirectory = Path.GetFullPath(directory);
        var releases = new[]
        {
            new ReleaseSpec("LmModule", Path.Combine(fullDirectory, $"regime-{lmVersion}.msi"), lmVersion, "any"),
            new ReleaseSpec("AtolDriver", Path.Combine(fullDirectory,
                $"KKT10-{atolVersion}-windows64-setup-signed.exe"), atolVersion, "x64"),
            new ReleaseSpec("AtolDriver", Path.Combine(fullDirectory,
                $"KKT10-{atolVersion}-windows32-setup-signed.exe"), atolVersion, "x86"),
            new ReleaseSpec("ESM", Path.Combine(fullDirectory,
                $"esm_{esmVersion}-windows-signed-setup.exe"), esmVersion, "any"),
            new ReleaseSpec("Controller", Path.Combine(fullDirectory,
                $"esm-lm-controller_{controllerVersion}-windows-setup.exe"), controllerVersion, "any")
        };
        await PublishMany(keyPath, releases, apiBase, setCurrent);
    }

    private static async Task PublishMany(string keyPath, IReadOnlyList<ReleaseSpec> releases,
        string apiBase, bool setCurrent)
    {
        string fullKeyPath = Path.GetFullPath(keyPath);
        EnsureKeyOutsideGit(fullKeyPath);
        foreach (ReleaseSpec release in releases)
        {
            FileInfo file = new(release.BinaryPath);
            if (!file.Exists || file.Length <= 0)
                throw new FileNotFoundException("Release file is missing or empty.", release.BinaryPath);
            ValidateHonestFlowFileVersion(release.Component, release.BinaryPath, release.Version);
        }
        Console.WriteLine($"Validated {releases.Count} release file(s). No network changes have been made yet.");

        byte[] encryptedKey = ReadKeyFile(fullKeyPath);
        char[] password = ReadPassword("Release-key password: ");
        char[] adminKeyChars = ReadPassword("HonestLicenseServer admin key (Enter = use HonestDesk): ");
        if (adminKeyChars.Length == 0)
            adminKeyChars = ReadHonestDeskAdminKey();
        string adminKey = new(adminKeyChars);
        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri(apiBase.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromHours(2)
            };
            client.DefaultRequestHeaders.Add("X-Admin-Key", adminKey);
            using var storageClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            for (int index = 0; index < releases.Count; index++)
            {
                ReleaseSpec release = releases[index];
                Console.WriteLine();
                Console.WriteLine($"[{index + 1}/{releases.Count}] {release.Component} {release.Version} ({release.Architecture})");
                await PublishOne(client, storageClient, encryptedKey, password, release);
            }
            if (setCurrent)
            {
                Console.WriteLine();
                Console.WriteLine("All release files are available. Updating current component versions...");
                foreach (ReleaseSpec release in releases
                             .GroupBy(x => x.Component, StringComparer.OrdinalIgnoreCase)
                             .Select(x => x.First()))
                    await SetCurrentVersion(client, release);
            }
            Console.WriteLine();
            Console.WriteLine($"Published {releases.Count} release file(s) successfully.");
            Console.WriteLine(setCurrent
                ? "All requested versions are now current."
                : "Current component versions were not changed.");
        }
        finally
        {
            adminKey = string.Empty;
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(password.AsSpan()));
            CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(adminKeyChars.AsSpan()));
            CryptographicOperations.ZeroMemory(encryptedKey);
        }
    }

    private static async Task PublishOne(HttpClient client, HttpClient storageClient,
        byte[] encryptedKey, char[] password, ReleaseSpec release)
    {
        Console.WriteLine("  [1/4] Hashing and signing...");
        SignedReleaseEnvelope envelope = ReleaseSigner.Sign(
            encryptedKey, password, release.BinaryPath, release.Component, release.Version, release.Architecture);

        Console.WriteLine("  [2/4] Checking the release catalog...");
        if (await IsAlreadyPublished(client, envelope, release))
        {
            Console.WriteLine("  The identical signed release is already available; upload skipped.");
            return;
        }

        Console.WriteLine("        Requesting a protected Cloud.ru upload URL...");
        using HttpResponseMessage create = await client.PostAsJsonAsync("api/admin/release-uploads", new
        {
            keyId = envelope.KeyId,
            manifestBase64 = envelope.ManifestBase64,
            signatureBase64 = envelope.SignatureBase64,
            useMultipart = true
        });
        await EnsureSuccess(create, "create release upload");
        ReleaseUploadResponse upload = (await create.Content.ReadFromJsonAsync<ReleaseUploadResponse>())
            ?? throw new InvalidDataException("Server returned an empty release upload response.");

        if (upload.PartSizeBytes is null or <= 0 || upload.PartCount is null or <= 0)
            throw new InvalidDataException("Server did not create a multipart release upload.");

        Console.WriteLine($"  [3/4] Uploading {upload.PartCount} part(s) directly to Cloud.ru...");
        IReadOnlyList<CompletedReleasePart> parts = await UploadMultipartAsync(
            client, storageClient, upload, release.BinaryPath);

        Console.WriteLine("  [4/4] Cloud.ru is assembling the parts; server is checking object metadata...");
        using HttpResponseMessage complete = await AwaitWithElapsedStatus(
            client.PostAsJsonAsync($"api/admin/release-uploads/{upload.Id}/complete", new { parts }),
            "multipart completion");
        await EnsureSuccess(complete, "verify release object");
        Console.WriteLine($"  Published. Key ID: {envelope.KeyId}");
        Console.WriteLine("  The asset is uploaded; current version was not changed yet.");
    }

    private static async Task<bool> IsAlreadyPublished(HttpClient client,
        SignedReleaseEnvelope envelope, ReleaseSpec release)
    {
        string requestUri = "api/admin/assets?component=" + Uri.EscapeDataString(release.Component) +
            "&architecture=" + Uri.EscapeDataString(release.Architecture);
        using HttpResponseMessage response = await client.GetAsync(requestUri);
        await EnsureSuccess(response, "read the release catalog");
        ExistingAssetResponse[] assets =
            (await response.Content.ReadFromJsonAsync<ExistingAssetResponse[]>()) ?? [];
        ExistingAssetResponse? existing = assets.SingleOrDefault(x =>
            string.Equals(x.Version, release.Version, StringComparison.Ordinal) &&
            string.Equals(x.Architecture, release.Architecture, StringComparison.OrdinalIgnoreCase));
        if (existing is null || string.IsNullOrWhiteSpace(existing.ReleaseKeyId) ||
            string.IsNullOrWhiteSpace(existing.ManifestBase64) ||
            string.IsNullOrWhiteSpace(existing.ReleaseSignatureBase64) ||
            string.IsNullOrWhiteSpace(existing.ObjectKey))
            return false;

        if (string.Equals(existing.ReleaseKeyId, envelope.KeyId, StringComparison.Ordinal) &&
            string.Equals(existing.ManifestBase64, envelope.ManifestBase64, StringComparison.Ordinal))
            return true;

        throw new InvalidDataException(
            $"A different signed release already exists for {release.Component} " +
            $"{release.Version} ({release.Architecture}). Published releases are immutable.");
    }

    private static async Task SetCurrentVersion(HttpClient client, ReleaseSpec release)
    {
        using HttpResponseMessage publish = await client.PutAsJsonAsync(
            $"api/admin/versions/{Uri.EscapeDataString(release.Component)}",
            new { currentVersion = release.Version });
        await EnsureSuccess(publish, "publish current component version");
    }

    private static async Task<IReadOnlyList<CompletedReleasePart>> UploadMultipartAsync(
        HttpClient apiClient, HttpClient storageClient, ReleaseUploadResponse upload, string filePath)
    {
        long partSize = upload.PartSizeBytes!.Value;
        int partCount = upload.PartCount!.Value;
        long fileSize = new FileInfo(filePath).Length;
        if ((fileSize + partSize - 1) / partSize != partCount)
            throw new InvalidDataException("Server returned an inconsistent multipart layout.");

        var completed = new List<CompletedReleasePart>(partCount);
        for (int partNumber = 1; partNumber <= partCount; partNumber++)
        {
            long offset = (partNumber - 1L) * partSize;
            long length = Math.Min(partSize, fileSize - offset);
            string etag = await UploadPartWithRetryAsync(
                apiClient, storageClient, upload.Id, filePath, partNumber, partCount, offset, length);
            completed.Add(new CompletedReleasePart(partNumber, etag));
            long confirmed = offset + length;
            Console.WriteLine($"    Confirmed {partNumber}/{partCount}; total " +
                              $"{confirmed * 100d / fileSize:F1}% ({FormatBytes(confirmed)} / {FormatBytes(fileSize)}).");
        }
        return completed;
    }

    private static async Task<string> UploadPartWithRetryAsync(HttpClient apiClient,
        HttpClient storageClient, int uploadId, string filePath, int partNumber, int partCount,
        long offset, long length)
    {
        const int attempts = 5;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using HttpResponseMessage partUrlResponse = await apiClient.PostAsync(
                    $"api/admin/release-uploads/{uploadId}/parts/{partNumber}", null);
                await EnsureSuccess(partUrlResponse, $"create upload URL for part {partNumber}");
                ReleaseUploadPartResponse part =
                    (await partUrlResponse.Content.ReadFromJsonAsync<ReleaseUploadPartResponse>())
                    ?? throw new InvalidDataException("Server returned an empty part upload response.");

                Console.WriteLine($"    Part {partNumber}/{partCount}, attempt {attempt}/{attempts}, " +
                                  $"{FormatBytes(length)}...");
                await using FileStream file = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                file.Position = offset;
                using var content = new ProgressStreamContent(file, length);
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
                Task<HttpResponseMessage> uploadTask = storageClient.PutAsync(
                    part.UploadUrl, content, timeout.Token);
                Task firstCompleted = await Task.WhenAny(uploadTask, content.SerializationCompleted);
                HttpResponseMessage response;
                if (firstCompleted == content.SerializationCompleted)
                {
                    await content.SerializationCompleted;
                    if (!uploadTask.IsCompleted)
                    {
                        Console.WriteLine("      Part is in the local network buffer; waiting for Cloud.ru confirmation.");
                        response = await AwaitWithElapsedStatus(uploadTask, $"part {partNumber} confirmation");
                    }
                    else response = await uploadTask;
                }
                else response = await uploadTask;
                using (response)
                {
                if (response.IsSuccessStatusCode)
                {
                    string? etag = response.Headers.ETag?.Tag;
                    if (string.IsNullOrWhiteSpace(etag) &&
                        response.Headers.TryGetValues("ETag", out IEnumerable<string>? values))
                        etag = values.FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(etag))
                        throw new InvalidDataException($"Cloud.ru did not return an ETag for part {partNumber}.");
                    return etag;
                }

                string body = await response.Content.ReadAsStringAsync(timeout.Token);
                bool transient = response.StatusCode is HttpStatusCode.RequestTimeout or
                    HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or
                    HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout ||
                    (int)response.StatusCode >= 500;
                if (!transient || attempt == attempts)
                    throw new HttpRequestException(
                        $"Could not upload part {partNumber}: HTTP {(int)response.StatusCode}. {body}");
                }
            }
            catch (Exception ex) when (attempt < attempts &&
                                       ex is HttpRequestException or TaskCanceledException or IOException)
            {
                Console.WriteLine();
                Console.WriteLine($"    Part {partNumber} attempt {attempt} failed: {DescribeException(ex)}");
            }

            Console.WriteLine($"    Retrying only part {partNumber} ({attempt + 1}/{attempts})...");
            await Task.Delay(TimeSpan.FromSeconds(attempt * 3));
        }
        throw new IOException($"Could not upload part {partNumber}.");
    }

    private static async Task<T> AwaitWithElapsedStatus<T>(Task<T> operation, string operationName)
    {
        var stopwatch = Stopwatch.StartNew();
        bool wroteStatus = false;
        while (!operation.IsCompleted)
        {
            if (await Task.WhenAny(operation, Task.Delay(TimeSpan.FromSeconds(1))) == operation)
                break;
            wroteStatus = true;
            Console.Write($"\r    Waiting for {operationName}: {stopwatch.Elapsed:hh\\:mm\\:ss}   ");
        }
        if (wroteStatus) Console.WriteLine();
        return await operation;
    }

    private static string DescribeException(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) &&
                !messages.Contains(current.Message, StringComparer.Ordinal))
                messages.Add(current.Message);
        }
        return string.Join(" -> ", messages);
    }

    private static async Task SetCurrent(string component, string version, string apiBase)
    {
        char[] adminKeyChars = ReadPassword("HonestLicenseServer admin key (Enter = use HonestDesk): ");
        if (adminKeyChars.Length == 0) adminKeyChars = ReadHonestDeskAdminKey();
        string adminKey = new(adminKeyChars);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(apiBase.TrimEnd('/') + "/") };
            client.DefaultRequestHeaders.Add("X-Admin-Key", adminKey);
            using HttpResponseMessage response = await client.PutAsJsonAsync(
                $"api/admin/versions/{Uri.EscapeDataString(component)}", new { currentVersion = version });
            await EnsureSuccess(response, "publish current component version");
            Console.WriteLine($"{component} {version} is now current.");
        }
        finally
        {
            adminKey = string.Empty;
            CryptographicOperations.ZeroMemory(
                System.Runtime.InteropServices.MemoryMarshal.AsBytes(adminKeyChars.AsSpan()));
        }
    }

    private static byte[] ReadKeyFile(string fullKeyPath)
    {
        long length = new FileInfo(fullKeyPath).Length;
        if (length is <= 0 or > 4096) throw new IOException("Invalid release key file size.");
        return File.ReadAllBytes(fullKeyPath);
    }

    private static void ValidateHonestFlowFileVersion(string component, string binaryPath, string version)
    {
        string? actualVersion = FileVersionInfo.GetVersionInfo(binaryPath).FileVersion;
        if (component == "HonestFlow" && !SameReleaseVersion(actualVersion, version))
            throw new ArgumentException("Executable FileVersion does not match the release version.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode) return;
        string body = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Could not {operation}: HTTP {(int)response.StatusCode}. {body}");
    }

    private sealed class ReleaseUploadResponse
    {
        public int Id { get; set; }
        public long? PartSizeBytes { get; set; }
        public int? PartCount { get; set; }
    }

    private sealed class ReleaseUploadPartResponse
    {
        public string UploadUrl { get; set; } = string.Empty;
    }

    private sealed record CompletedReleasePart(int PartNumber, string ETag);

    private sealed class ExistingAssetResponse
    {
        public string Version { get; set; } = string.Empty;
        public string Architecture { get; set; } = string.Empty;
        public string? ObjectKey { get; set; }
        public string? ReleaseKeyId { get; set; }
        public string? ManifestBase64 { get; set; }
        public string? ReleaseSignatureBase64 { get; set; }
    }

    private sealed record ReleaseSpec(
        string Component, string BinaryPath, string Version, string Architecture);

    private sealed class ProgressStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly long _length;
        private readonly TaskCompletionSource<bool> _serializationCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SerializationCompleted => _serializationCompleted.Task;

        public ProgressStreamContent(Stream source, long length)
        {
            _source = source;
            _length = length;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            Headers.ContentLength = length;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _length;
            return true;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            CopySourceToAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => CopySourceToAsync(stream, cancellationToken);

        private async Task CopySourceToAsync(Stream destination, CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
            var stopwatch = Stopwatch.StartNew();
            long transferred = 0;
            long lastReportMilliseconds = -1000;
            try
            {
                while (true)
                {
                    long remaining = _length - transferred;
                    if (remaining <= 0) break;
                    int read = await _source.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                    if (read == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    transferred += read;
                    if (stopwatch.ElapsedMilliseconds - lastReportMilliseconds >= 1000 || transferred == _length)
                    {
                        WriteProgress(transferred, stopwatch.Elapsed);
                        lastReportMilliseconds = stopwatch.ElapsedMilliseconds;
                    }
                }
                if (transferred != _length)
                    throw new EndOfStreamException(
                        $"Release part ended early: expected {_length} bytes, read {transferred}.");
                Console.WriteLine();
                _serializationCompleted.TrySetResult(true);
            }
            catch (Exception ex)
            {
                _serializationCompleted.TrySetException(ex);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private void WriteProgress(long transferred, TimeSpan elapsed)
        {
            double seconds = Math.Max(elapsed.TotalSeconds, 0.001);
            double bytesPerSecond = transferred / seconds;
            double percent = _length == 0 ? 100 : transferred * 100d / _length;
            TimeSpan remaining = bytesPerSecond <= 0
                ? TimeSpan.Zero
                : TimeSpan.FromSeconds(Math.Max(0, (_length - transferred) / bytesPerSecond));
            Console.Write($"\r    {percent,6:F1}%  {FormatBytes(transferred)} / {FormatBytes(_length)}  " +
                          $"{FormatBytes(bytesPerSecond)}/s  ETA {FormatDuration(remaining)}   ");
        }

        private static string FormatDuration(TimeSpan value) =>
            value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return $"{bytes:F1} {units[unit]}";
    }

    private static bool SameReleaseVersion(string? actualText, string expectedText)
    {
        if (!Version.TryParse(actualText, out Version? actual) ||
            !Version.TryParse(expectedText, out Version? expected)) return false;
        return actual.Major == expected.Major && actual.Minor == expected.Minor &&
            Math.Max(actual.Build, 0) == Math.Max(expected.Build, 0) &&
            Math.Max(actual.Revision, 0) == Math.Max(expected.Revision, 0);
    }

    private static char[] ReadPassword(string prompt)
    {
        if (Console.IsInputRedirected) throw new IOException("An interactive console is required for the password.");
        Console.Write(prompt);
        var characters = new List<char>();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && characters.Count > 0)
            {
                characters[^1] = '\0';
                characters.RemoveAt(characters.Count - 1);
            }
            else if (!char.IsControl(key.KeyChar)) characters.Add(key.KeyChar);
        }
        Console.WriteLine();
        char[] password = characters.ToArray();
        for (int index = 0; index < characters.Count; index++) characters[index] = '\0';
        return password;
    }

    private static char[] ReadHonestDeskAdminKey()
    {
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HonestDesk", "settings.json");
        if (!File.Exists(settingsPath))
            throw new IOException($"HonestDesk settings were not found: {settingsPath}");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        if (!document.RootElement.TryGetProperty("ProtectedAdminApiKey", out JsonElement value) ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new IOException("HonestDesk does not contain a saved admin API key.");

        byte[] encrypted = Convert.FromBase64String(value.GetString()!);
        byte[] decrypted = UnprotectCurrentUser(encrypted);
        try
        {
            char[] result = Encoding.UTF8.GetChars(decrypted);
            if (result.Length == 0)
                throw new IOException("The HonestDesk admin API key is empty.");
            Console.WriteLine("Using the DPAPI-protected admin key saved by HonestDesk.");
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    private static byte[] UnprotectCurrentUser(byte[] encrypted)
    {
        DATA_BLOB input = CreateBlob(encrypted);
        DATA_BLOB output = default;
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, 0, ref output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            byte[] result = new byte[output.cbData];
            Marshal.Copy(output.pbData, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
            if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
        }
    }

    private static DATA_BLOB CreateBlob(byte[] bytes)
    {
        DATA_BLOB blob = new() { cbData = bytes.Length, pbData = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, blob.pbData, bytes.Length);
        return blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB input, IntPtr description,
        IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DATA_BLOB output);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static void EnsureKeyOutsideGit(string keyPath)
    {
        for (DirectoryInfo? directory = new(Path.GetDirectoryName(keyPath)!); directory is not null;
             directory = directory.Parent)
        {
            string marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                throw new ArgumentException("Keep the release private key outside every Git repository.");
        }
    }
}
