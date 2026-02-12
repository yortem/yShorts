using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace yShorts;

/// <summary>
/// ViewModel for the main window — tracks progress, log text, and UI state.
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _topic = "";
    private string _logText = "";
    private string _currentStep = "";
    private int _progressPercent;
    private double _progressValue;
    private bool _isGenerating;
    private string _outputFilePath = "";
    private bool _isComplete;
    private int _currentStepIndex; // 0-5
    private System.Collections.ObjectModel.ObservableCollection<yShorts.Models.LocalClip> _localClips = new();

    private static readonly SolidColorBrush DimBrush = new(Color.FromRgb(0x4B, 0x55, 0x63));
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(0x8B, 0x5C, 0xF6));
    private static readonly SolidColorBrush DoneBrush = new(Color.FromRgb(0x22, 0xC5, 0x5E));

    public MainViewModel()
    {
        _localClips.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(LocalClipsCount));
            OnPropertyChanged(nameof(HasLocalClips));
        };
        _stages.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CanGenerate));
        };
    }


    // ── Properties ──

    public string Topic
    {
        get => _topic;
        set { _topic = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
    }

    public string LogText
    {
        get => _logText;
        set { _logText = value; OnPropertyChanged(); }
    }

    public string CurrentStep
    {
        get => _currentStep;
        set { _currentStep = value; OnPropertyChanged(); }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            _progressValue = value;
            OnPropertyChanged();
            ProgressPercent = (int)value; // Start syncing
        }
    }

    public int ProgressPercent
    {
        get => _progressPercent;
        set { _progressPercent = value; OnPropertyChanged(); }
    }

    public bool CanGenerate => !_isGenerating && (IsAdvancedMode ? Stages.Any() : !string.IsNullOrWhiteSpace(Topic));

    public bool IsGenerating
    {
        get => _isGenerating;
        set
        {
            _isGenerating = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    public Visibility ProgressVisible => _isGenerating || _isComplete ? Visibility.Visible : Visibility.Collapsed;

    public string OutputFilePath
    {
        get => _outputFilePath;
        set { _outputFilePath = value; OnPropertyChanged(); }
    }

    public Visibility ResultVisible => _isComplete ? Visibility.Visible : Visibility.Collapsed;

    public System.Collections.ObjectModel.ObservableCollection<yShorts.Models.LocalClip> LocalClips
    {
        get => _localClips;
        set { _localClips = value; OnPropertyChanged(); OnPropertyChanged(nameof(LocalClipsCount)); }
    }

    public string LocalClipsCount => LocalClips.Count == 0 ? "Using Pexels Auto-download" : $"{LocalClips.Count} manual clips selected";
    public bool HasLocalClips => LocalClips.Count > 0;

    private bool _isAdvancedMode;
    public bool IsAdvancedMode
    {
        get => _isAdvancedMode;
        set { _isAdvancedMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGenerate)); }
    }

    private System.Collections.ObjectModel.ObservableCollection<yShorts.Models.VideoStage> _stages = new();
    public System.Collections.ObjectModel.ObservableCollection<yShorts.Models.VideoStage> Stages
    {
        get => _stages;
        set
        {
            _stages = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanGenerate));
        }
    }

    public void AddStage() => Stages.Add(new yShorts.Models.VideoStage());
    public void RemoveStage(yShorts.Models.VideoStage stage) => Stages.Remove(stage);

    private string _seoTitle = "";
    private string _seoDescription = "";

    public string SeoTitle
    {
        get => _seoTitle;
        set { _seoTitle = value; OnPropertyChanged(); }
    }

    public string SeoDescription
    {
        get => _seoDescription;
        set { _seoDescription = value; OnPropertyChanged(); }
    }

    public void RemoveClip(yShorts.Models.LocalClip clip)
    {
        LocalClips.Remove(clip);
    }



    // ── Step indicator colors ──

    public SolidColorBrush Step1Color => GetStepColor(0);
    public SolidColorBrush Step2Color => GetStepColor(1);
    public SolidColorBrush Step3Color => GetStepColor(2);
    public SolidColorBrush Step4Color => GetStepColor(3);
    public SolidColorBrush Step5Color => GetStepColor(4);
    public SolidColorBrush Step6Color => GetStepColor(5);

    private SolidColorBrush GetStepColor(int step)
    {
        if (step < _currentStepIndex) return DoneBrush;
        if (step == _currentStepIndex && _isGenerating) return ActiveBrush;
        return DimBrush;
    }

    // ── Methods ──

    public void AppendLog(string message)
    {
        LogText += message + "\n";
    }

    public void SetStep(int stepIndex, string description, int percent)
    {
        _currentStepIndex = stepIndex;
        CurrentStep = description;
        ProgressPercent = percent;
        RefreshStepColors();
    }

    public void SetComplete(string outputPath)
    {
        _isComplete = true;
        _currentStepIndex = 6;
        ProgressPercent = 100;
        CurrentStep = "Complete!";
        OutputFilePath = outputPath;
        IsGenerating = false;
        OnPropertyChanged(nameof(ResultVisible));
        OnPropertyChanged(nameof(ProgressVisible));
        RefreshStepColors();
    }

    public void Reset()
    {
        _isComplete = false;
        _currentStepIndex = 0;
        ProgressPercent = 0;
        CurrentStep = "";
        OutputFilePath = "";
        LogText = "";
        OnPropertyChanged(nameof(ResultVisible));
        OnPropertyChanged(nameof(ProgressVisible));
        RefreshStepColors();
    }

    private void RefreshStepColors()
    {
        OnPropertyChanged(nameof(Step1Color));
        OnPropertyChanged(nameof(Step2Color));
        OnPropertyChanged(nameof(Step3Color));
        OnPropertyChanged(nameof(Step4Color));
        OnPropertyChanged(nameof(Step5Color));
        OnPropertyChanged(nameof(Step6Color));
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
