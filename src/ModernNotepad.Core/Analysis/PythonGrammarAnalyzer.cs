using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Text.Json;
using ModernNotepad.Core.Models;

namespace ModernNotepad.Core.Analysis;

public enum PythonGrammarEngine
{
    Spacy,
    Nltk
}

/// <summary>
/// Hosts the Python spaCy/NLTK grammar worker and exchanges locally-tokenized text
/// over either a Windows named pipe or a named shared-memory mapping.
/// </summary>
public sealed class PythonGrammarAnalyzer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PythonGrammarEngine _engine;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPythonGrammarWorker? _worker;
    private PythonGrammarTransport? _workerTransport;
    private bool _disposed;

    public PythonGrammarAnalyzer(PythonGrammarEngine engine)
    {
        _engine = engine;
    }

    public string DisplayName => _engine == PythonGrammarEngine.Spacy
        ? "Python spaCy"
        : "Python NLTK";

    public async Task<GrammarAnalysis> AnalyzeAsync(
        string text,
        IReadOnlyList<TextToken>? tokens,
        PythonGrammarTransport transport,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(text);
        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        if (tokens.Count == 0)
        {
            return ProviderGrammarAnalysis.Empty();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new PythonGrammarRequest(
                text,
                tokens.Select(token => new PythonGrammarToken(
                    token.Text,
                    token.Span.Start,
                    token.Span.Length)).ToArray()),
            JsonOptions);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_worker is null
                || _workerTransport != transport
                || !_worker.IsAlive)
            {
                DisposeWorker();
                _worker = await CreateWorkerAsync(transport, cancellationToken).ConfigureAwait(false);
                _workerTransport = transport;
            }

            byte[] responseBytes;
            try
            {
                responseBytes = await _worker.ExchangeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                DisposeWorker();
                throw;
            }

            return ParseResponse(responseBytes, tokens, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static GrammarAnalysis ParseResponse(
        ReadOnlyMemory<byte> responseBytes,
        IReadOnlyList<TextToken> tokens,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(responseBytes);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var okElement)
            || okElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new InvalidDataException("Python grammar worker returned an invalid response envelope.");
        }

        if (!okElement.GetBoolean())
        {
            var error = root.TryGetProperty("error", out var errorElement)
                ? errorElement.GetString()
                : null;
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "Python grammar provider failed without an error message."
                    : error);
        }

        if (!root.TryGetProperty("assignments", out var assignmentsElement)
            || assignmentsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Python grammar worker omitted token assignments.");
        }

        var assignments = new List<GrammarCategory>(assignmentsElement.GetArrayLength());
        foreach (var element in assignmentsElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = element.GetString();
            if (string.IsNullOrWhiteSpace(name)
                || !Enum.TryParse<GrammarCategory>(name, ignoreCase: true, out var category)
                || !Enum.IsDefined(typeof(GrammarCategory), category))
            {
                throw new InvalidDataException(
                    $"Python grammar worker returned unknown category '{name}'.");
            }

            assignments.Add(category);
        }

        return ProviderGrammarAnalysis.Create(tokens, assignments, cancellationToken);
    }

    internal static string ResolveWorkerScriptPath(string? environmentValue, string baseDirectory)
    {
        var configured = environmentValue?.Trim();
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(baseDirectory, "grammar_provider.py")
            : configured;
    }

    internal static string ResolvePythonExecutable(string? environmentValue) =>
        string.IsNullOrWhiteSpace(environmentValue)
            ? "python"
            : environmentValue.Trim();

    private async Task<IPythonGrammarWorker> CreateWorkerAsync(
        PythonGrammarTransport transport,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Python grammar IPC providers are supported by the Windows desktop application only.");
        }

        var pythonExecutable = ResolvePythonExecutable(
            Environment.GetEnvironmentVariable("MODERNNOTEPAD_PYTHON"));
        var workerPath = ResolveWorkerScriptPath(
            Environment.GetEnvironmentVariable("MODERNNOTEPAD_GRAMMAR_WORKER"),
            AppContext.BaseDirectory);
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException(
                "The Python grammar worker was not found. Reinstall Modern Notepad or set MODERNNOTEPAD_GRAMMAR_WORKER.",
                workerPath);
        }

        return transport switch
        {
            PythonGrammarTransport.NamedPipes => await NamedPipePythonGrammarWorker.CreateAsync(
                pythonExecutable,
                workerPath,
                _engine,
                cancellationToken).ConfigureAwait(false),
            PythonGrammarTransport.SharedMemory => new SharedMemoryPythonGrammarWorker(
                pythonExecutable,
                workerPath,
                _engine),
            _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null)
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PythonGrammarAnalyzer));
        }
    }

    private void DisposeWorker()
    {
        _worker?.Dispose();
        _worker = null;
        _workerTransport = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // Do not dispose the semaphore here: an in-flight AnalyzeAsync may still
        // unwind through its finally block during application shutdown.
        DisposeWorker();
    }

    private sealed record PythonGrammarRequest(string Text, IReadOnlyList<PythonGrammarToken> Tokens);
    private sealed record PythonGrammarToken(string Text, int Start, int Length);

    private interface IPythonGrammarWorker : IDisposable
    {
        bool IsAlive { get; }
        Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancellationToken);
    }

    private abstract class PythonGrammarWorkerBase : IPythonGrammarWorker
    {
        private readonly Task<string> _stderrTask;
        private readonly Task<string> _stdoutTask;
        private bool _disposed;

        protected PythonGrammarWorkerBase(Process process)
        {
            WorkerProcess = process;
            _stderrTask = process.StandardError.ReadToEndAsync();
            _stdoutTask = process.StandardOutput.ReadToEndAsync();
        }

        protected Process WorkerProcess { get; }
        public bool IsAlive => !_disposed && !WorkerProcess.HasExited;

        public abstract Task<byte[]> ExchangeAsync(byte[] request, CancellationToken cancellationToken);

        protected InvalidOperationException CreateExitedException()
        {
            var details = _stderrTask.IsCompletedSuccessfully
                ? _stderrTask.Result.Trim()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(details) && _stdoutTask.IsCompletedSuccessfully)
            {
                details = _stdoutTask.Result.Trim();
            }

            return new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? $"Python grammar worker exited with code {WorkerProcess.ExitCode}."
                    : $"Python grammar worker exited with code {WorkerProcess.ExitCode}: {details}");
        }

        public virtual void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (!WorkerProcess.HasExited)
                {
                    WorkerProcess.Kill(entireProcessTree: true);
                    WorkerProcess.WaitForExit(1000);
                }
            }
            catch
            {
                // Process cleanup is best effort during provider switches/app shutdown.
            }
            finally
            {
                WorkerProcess.Dispose();
            }
        }

        protected static Process StartProcess(
            string pythonExecutable,
            string workerPath,
            PythonGrammarEngine engine,
            params string[] transportArguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add(workerPath);
            startInfo.ArgumentList.Add("--engine");
            startInfo.ArgumentList.Add(engine == PythonGrammarEngine.Spacy ? "spacy" : "nltk");
            foreach (var argument in transportArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                return System.Diagnostics.Process.Start(startInfo)
                    ?? throw new InvalidOperationException("Python grammar worker could not be started.");
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new InvalidOperationException(
                    $"Could not start Python executable '{pythonExecutable}'. " +
                    "Install Python or set MODERNNOTEPAD_PYTHON to python.exe.",
                    exception);
            }
        }
    }

    private sealed class NamedPipePythonGrammarWorker : PythonGrammarWorkerBase
    {
        private const int MaxFrameBytes = 64 * 1024 * 1024;
        private readonly NamedPipeServerStream _pipe;

        private NamedPipePythonGrammarWorker(Process process, NamedPipeServerStream pipe)
            : base(process)
        {
            _pipe = pipe;
        }

        public static async Task<NamedPipePythonGrammarWorker> CreateAsync(
            string pythonExecutable,
            string workerPath,
            PythonGrammarEngine engine,
            CancellationToken cancellationToken)
        {
            var pipeName = $"ModernNotepadGrammar_{Guid.NewGuid():N}";
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            Process? process = null;
            try
            {
                process = StartProcess(
                    pythonExecutable,
                    workerPath,
                    engine,
                    "--transport", "named-pipe", "--pipe", pipeName);

                using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupCts.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    await pipe.WaitForConnectionAsync(startupCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Python grammar worker did not connect to the named pipe.");
                }

                return new NamedPipePythonGrammarWorker(process, pipe);
            }
            catch
            {
                pipe.Dispose();
                if (process is not null)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Preserve the provider startup error.
                    }
                    process.Dispose();
                }

                throw;
            }
        }

        public override async Task<byte[]> ExchangeAsync(
            byte[] request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAlive)
            {
                throw CreateExitedException();
            }
            if (!_pipe.IsConnected)
            {
                throw new IOException("Python grammar named pipe is disconnected.");
            }
            if (request.Length > MaxFrameBytes)
            {
                throw new InvalidDataException("Grammar request is too large for the Python named-pipe transport.");
            }

            var lengthPrefix = BitConverter.GetBytes(request.Length);
            await _pipe.WriteAsync(lengthPrefix.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _pipe.WriteAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            var responseLengthBytes = new byte[sizeof(int)];
            await ReadExactlyAsync(_pipe, responseLengthBytes, cancellationToken).ConfigureAwait(false);
            var responseLength = BitConverter.ToInt32(responseLengthBytes, 0);
            if (responseLength <= 0 || responseLength > MaxFrameBytes)
            {
                throw new InvalidDataException(
                    $"Python grammar named pipe returned invalid frame length {responseLength}.");
            }

            var response = new byte[responseLength];
            await ReadExactlyAsync(_pipe, response, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }

        private static async Task ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(offset, buffer.Length - offset),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Python grammar named pipe closed unexpectedly.");
                }

                offset += read;
            }
        }

        public override void Dispose()
        {
            _pipe.Dispose();
            base.Dispose();
        }
    }

    private sealed class SharedMemoryPythonGrammarWorker : PythonGrammarWorkerBase
    {
        private const int StateOffset = 0;
        private const int RequestLengthOffset = 4;
        private const int ResponseLengthOffset = 8;
        private const int PayloadOffset = 16;
        private const int StateIdle = 0;
        private const int StateRequest = 1;
        private const int StateResponse = 2;
        private const long SharedMemoryCapacity = 32L * 1024 * 1024;
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromMinutes(2);

        private readonly MemoryMappedFile _mapping;
        private readonly MemoryMappedViewAccessor _accessor;

        public SharedMemoryPythonGrammarWorker(
            string pythonExecutable,
            string workerPath,
            PythonGrammarEngine engine)
            : this(CreateResources(pythonExecutable, workerPath, engine))
        {
        }

        private SharedMemoryPythonGrammarWorker(SharedMemoryResources resources)
            : base(resources.WorkerProcess)
        {
            _mapping = resources.Mapping;
            _accessor = resources.Accessor;
        }

        private static SharedMemoryResources CreateResources(
            string pythonExecutable,
            string workerPath,
            PythonGrammarEngine engine)
        {
            var mappingName = $"ModernNotepadGrammar_{Guid.NewGuid():N}";
            var mapping = MemoryMappedFile.CreateNew(
                mappingName,
                SharedMemoryCapacity,
                MemoryMappedFileAccess.ReadWrite);
            var accessor = mapping.CreateViewAccessor(
                0,
                SharedMemoryCapacity,
                MemoryMappedFileAccess.ReadWrite);
            accessor.Write(StateOffset, StateIdle);
            accessor.Write(RequestLengthOffset, 0);
            accessor.Write(ResponseLengthOffset, 0);
            accessor.Flush();

            try
            {
                var process = StartProcess(
                    pythonExecutable,
                    workerPath,
                    engine,
                    "--transport", "shared-memory",
                    "--mapping", mappingName,
                    "--size", SharedMemoryCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return new SharedMemoryResources(process, mapping, accessor);
            }
            catch
            {
                accessor.Dispose();
                mapping.Dispose();
                throw;
            }
        }

        public override async Task<byte[]> ExchangeAsync(
            byte[] request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAlive)
            {
                throw CreateExitedException();
            }

            var maxPayload = checked((int)(SharedMemoryCapacity - PayloadOffset));
            if (request.Length > maxPayload)
            {
                throw new InvalidDataException("Grammar request is too large for the shared-memory transport.");
            }

            _accessor.Write(RequestLengthOffset, request.Length);
            _accessor.Write(ResponseLengthOffset, 0);
            _accessor.WriteArray(PayloadOffset, request, 0, request.Length);
            _accessor.Flush();
            _accessor.Write(StateOffset, StateRequest);
            _accessor.Flush();

            var stopwatch = Stopwatch.StartNew();
            while (_accessor.ReadInt32(StateOffset) != StateResponse)
            {
                if (!IsAlive)
                {
                    throw CreateExitedException();
                }
                if (stopwatch.Elapsed > ResponseTimeout)
                {
                    throw new TimeoutException("Python grammar shared-memory worker did not respond in time.");
                }

                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            }

            var responseLength = _accessor.ReadInt32(ResponseLengthOffset);
            if (responseLength <= 0 || responseLength > maxPayload)
            {
                throw new InvalidDataException(
                    $"Python grammar shared-memory worker returned invalid payload length {responseLength}.");
            }

            var response = new byte[responseLength];
            _accessor.ReadArray(PayloadOffset, response, 0, responseLength);
            _accessor.Write(StateOffset, StateIdle);
            _accessor.Flush();
            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }

        public override void Dispose()
        {
            _accessor.Dispose();
            _mapping.Dispose();
            base.Dispose();
        }

        private sealed record SharedMemoryResources(
            Process WorkerProcess,
            MemoryMappedFile Mapping,
            MemoryMappedViewAccessor Accessor);
    }
}
