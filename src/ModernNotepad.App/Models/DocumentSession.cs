using System.Windows.Documents;
using ModernNotepad.App.Views;
using ModernNotepad.Core.Analysis;
using ModernNotepad.Core.Models;

namespace ModernNotepad.App.Models;

public sealed class DocumentSession : BindableBase
{
    private string? _filePath;
    private DocumentFormat _format;
    private TextEncodingInfo _encoding;
    private LineEndingProfile _lineEndings;
    private bool _isDirty;
    private bool _isRecovered;
    private string _saveStatus = "Saved";
    private int _zoomPercent = 100;
    private DateTime? _sourceLastWriteTimeUtc;
    private TextStatistics _statistics = TextStatistics.Empty;
    private IReadOnlyList<TextFinding> _findings = Array.Empty<TextFinding>();

    public DocumentSession(
        FlowDocument document,
        string? filePath,
        DocumentFormat format,
        TextEncodingInfo encoding,
        LineEndingProfile lineEndings,
        string? recoveryId = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        _filePath = filePath;
        _format = format;
        _encoding = encoding;
        _lineEndings = lineEndings;
        RecoveryId = string.IsNullOrWhiteSpace(recoveryId)
            ? Guid.NewGuid().ToString("N")
            : recoveryId;
    }

    public FlowDocument Document { get; }
    public string RecoveryId { get; }
    public EditorDocumentView? View { get; set; }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public DocumentFormat Format
    {
        get => _format;
        set => SetProperty(ref _format, value);
    }

    public TextEncodingInfo Encoding
    {
        get => _encoding;
        set => SetProperty(ref _encoding, value);
    }

    public LineEndingProfile LineEndings
    {
        get => _lineEndings;
        set => SetProperty(ref _lineEndings, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public bool IsRecovered
    {
        get => _isRecovered;
        set
        {
            if (SetProperty(ref _isRecovered, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(HeaderText));
            }
        }
    }

    public string SaveStatus
    {
        get => _saveStatus;
        set => SetProperty(ref _saveStatus, value);
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        set => SetProperty(ref _zoomPercent, Math.Clamp(value, 50, 300));
    }

    public DateTime? SourceLastWriteTimeUtc
    {
        get => _sourceLastWriteTimeUtc;
        set => SetProperty(ref _sourceLastWriteTimeUtc, value);
    }

    public TextStatistics Statistics
    {
        get => _statistics;
        private set => SetProperty(ref _statistics, value);
    }

    public IReadOnlyList<TextFinding> Findings
    {
        get => _findings;
        private set => SetProperty(ref _findings, value);
    }

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(FilePath) ? "Untitled" : Path.GetFileName(FilePath);
            return IsRecovered ? $"Recovered — {name}" : name;
        }
    }

    public string HeaderText => IsDirty ? $"{DisplayName} •" : DisplayName;

    public void MarkDirty()
    {
        IsDirty = true;
        SaveStatus = "Unsaved changes";
    }

    public void MarkSaved(DateTime? lastWriteTimeUtc = null)
    {
        IsDirty = false;
        IsRecovered = false;
        SaveStatus = "Saved";
        if (lastWriteTimeUtc is not null)
        {
            SourceLastWriteTimeUtc = lastWriteTimeUtc;
        }
    }

    public void SetAnalysis(DocumentAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        Statistics = analysis.Statistics;
        Findings = analysis.Findings;
    }
}
