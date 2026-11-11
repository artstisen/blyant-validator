using System.Collections.Concurrent;
using System.Threading.Channels;
using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;
using Blyant.Validator.Markdown;
using Blyant.Validator.Typst;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Cli.Tui;

/// <summary>The result of validating a file, as it was when it was validated.</summary>
/// <param name="Written">When the file was written last (with Length: tells whether the result is still current).</param>
/// <param name="Length">The size of the file.</param>
/// <param name="Diagnostics">The problems found (empty: none).</param>
/// <param name="Failure">Why the file could not be validated (it could not be read, vnu is not available …).</param>
/// <param name="Fragment">The file was checked as an HTML fragment.</param>
/// <param name="Ignored">How many problems were found that are ignored (not in Diagnostics).</param>
internal sealed record FileResult(DateTime Written, long Length, IReadOnlyList<Diagnostic> Diagnostics, string? Failure,
    bool Fragment = false, int Ignored = 0)
{
    public int Errors => Diagnostics.Count(d => d.Severity == Severity.Error);
    public int Warnings => Diagnostics.Count(d => d.Severity == Severity.Warning);
}

/// <summary>
/// The results of the files shown in the TUI. Files are validated one at a time on a background thread when they
/// are asked for; a result is kept until the file changes (its time of writing or size). It does not touch the
/// console, so it can be tested.
/// </summary>
internal sealed class ResultStore : IDisposable
{
    private readonly ConcurrentDictionary<string, FileResult> _results = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.Ordinal);
    private readonly Channel<string> _requests = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private int _changed;
    private volatile string _engine;

    // Counts the changes of engine and rules: a result made before one is not kept (it was made the old way)
    private int _generation;
    private readonly VnuOptions _vnu;

    public ResultStore(string engine, VnuOptions vnu, FolderRules? rules = null)
    {
        _engine = engine;
        _vnu = vnu;
        Rules = rules ?? new FolderRules();
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>"native" or "vnu". Changing it validates the files again.</summary>
    public string Engine
    {
        get => _engine;
        set
        {
            if (_engine == value) return;
            _engine = value;
            Interlocked.Increment(ref _generation);
            _results.Clear();
        }
    }

    /// <summary>How the files of each folder are checked (:fragment, :ignore).</summary>
    public FolderRules Rules { get; }

    /// <summary>Set the rules of a folder; the files are checked again.</summary>
    public void SetRules(string directory, CheckRules rules)
    {
        Rules.Set(directory, rules);
        Interlocked.Increment(ref _generation);
        _results.Clear();
    }

    /// <summary>True for the files that are validated (HTML, CSS, XML, Markdown and Typst).</summary>
    public static bool IsValidated(string path) => SourceDocument.DetectKind(path) != DocumentKind.Unknown;

    /// <summary>
    /// The result of a file if it is current; otherwise null, and the file is validated (the result comes later:
    /// TakeChanged)
    /// </summary>
    public FileResult? Get(string path)
    {
        if (!IsValidated(path)) return null;
        // (a file that is gone, or cannot be looked at, is not asked for again and again)
        if (Stamp(path) is not { } stamp) return null;
        if (_results.TryGetValue(path, out var result) && result.Written == stamp.Written && result.Length == stamp.Length)
            return result;
        Request(path);
        return null;
    }

    /// <summary>The result kept for a file, current or not (null if it was never validated).</summary>
    public FileResult? Last(string path) => _results.TryGetValue(path, out var result) ? result : null;

    /// <summary>True while a file waits to be validated, or is being validated.</summary>
    public bool IsPending(string path) => _queued.ContainsKey(path);

    /// <summary>Validate a file again, even if nothing seems to have changed.</summary>
    public void Refresh(string path)
    {
        _results.TryRemove(path, out _);
        Request(path);
    }

    /// <summary>True (once) when results have come in since this was asked last.</summary>
    public bool TakeChanged() => Interlocked.Exchange(ref _changed, 0) == 1;

    /// <summary>Wait until no file waits to be validated (for tests).</summary>
    public async Task WaitIdleAsync(TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (!_queued.IsEmpty && DateTime.UtcNow < end) await Task.Delay(10).ConfigureAwait(false);
    }

    private void Request(string path)
    {
        if (_queued.TryAdd(path, 0)) _requests.Writer.TryWrite(path);
    }

    private static (DateTime Written, long Length)? Stamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? (info.LastWriteTimeUtc, info.Length) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WorkAsync()
    {
        try
        {
            await foreach (var path in _requests.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                var stale = false;
                try
                {
                    var generation = Volatile.Read(ref _generation);
                    var result = await ValidateAsync(path, _stop.Token).ConfigureAwait(false);
                    // The engine or the rules changed while the file was checked: it is checked again, the new way
                    stale = generation != Volatile.Read(ref _generation);
                    if (result is not null && !stale) _results[path] = result;
                }
                finally
                {
                    _queued.TryRemove(path, out _);
                    if (stale) Request(path);
                    Interlocked.Exchange(ref _changed, 1);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Closed
        }
    }

    private async Task<FileResult?> ValidateAsync(string path, CancellationToken ct)
    {
        if (Stamp(path) is not { } stamp) return null;
        try
        {
            var document = await DocumentIo.LoadAsync(path, ct).ConfigureAwait(false);
            var rules = Rules.ForFile(path);
            // (Fragments are checked by the native engine only)
            var fragment = rules.Fragment && document.Kind == DocumentKind.Html && _engine != "vnu";
            // (vnu checks HTML and CSS; XML is always checked here)
            IValidator validator = document.Kind switch
            {
                DocumentKind.Xml => new XmlValidator(),
                DocumentKind.Markdown => new MarkdownValidator(DocumentIo.IsFileOrFolder),
                DocumentKind.Typst => new TypstValidator(DocumentIo.TypstFiles),
                _ when _engine == "vnu" => new VnuValidator(_vnu),
                DocumentKind.Css => new CssValidator(),
                _ => new HtmlValidator(fragment),
            };
            var found = await validator.ValidateAsync(document, ct).ConfigureAwait(false);
            var diagnostics = IgnoreList.Apply(found, rules.Ignore);
            return new FileResult(stamp.Written, stamp.Length, diagnostics, null, fragment, found.Count - diagnostics.Count);
        }
        catch (NotUtf8Exception e)
        {
            // A problem of the file, shown as the command line reports it
            var rules = Rules.ForFile(path);
            var diagnostics = IgnoreList.Apply([e.ToDiagnostic(path)], rules.Ignore);
            return new FileResult(stamp.Written, stamp.Length, diagnostics, null, false, 1 - diagnostics.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Anything else (a mistake in a check, a file too large for memory) is told about for this file, and
            // the other files are still checked: the worker must not stop
            var why = e is IOException or UnauthorizedAccessException or VnuNotAvailableException or System.Text.Json.JsonException
                ? e.Message
                : $"Internal error ({e.GetType().Name}): {e.Message}";
            return new FileResult(stamp.Written, stamp.Length, [], why);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _requests.Writer.TryComplete();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Closed
        }

        _stop.Dispose();
    }
}
