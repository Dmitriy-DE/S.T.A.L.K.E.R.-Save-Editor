using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using StalkerSaveEditor.Desktop.Styles;

namespace StalkerSaveEditor.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());

        // Apply authentic S.T.A.L.K.E.R. industrial palette globally
        Resources["SystemControlBackgroundAltHighBrush"] = StalkerTheme.BrushBgBase;
        Resources["ScrollBarThumbFill"] = new SolidColorBrush(Color.Parse("#3D4837"));
        Resources["ScrollBarThumbFillPointerOver"] = StalkerTheme.BrushAccentAmber;
        Resources["ScrollBarTrackFill"] = new SolidColorBrush(Color.Parse("#101310"));
        Resources["TextControlBackground"] = StalkerTheme.BrushBgInput;
        Resources["TextControlForeground"] = StalkerTheme.BrushTextPrimary;
        Resources["TextControlBorderBrush"] = StalkerTheme.BrushBorder;
        Resources["TextControlBorderBrushFocused"] = StalkerTheme.BrushAccentAmber;
        Resources["ProgressBarForeground"] = StalkerTheme.BrushAccentAmber;
        Resources["ProgressBarBackground"] = new SolidColorBrush(Color.Parse("#181D15"));

        // Disabled buttons stay readable (Fluent's defaults are near-invisible on the dark palette).
        Resources["ButtonBackgroundDisabled"] = new SolidColorBrush(Color.Parse("#12150F"));
        Resources["ButtonForegroundDisabled"] = StalkerTheme.BrushTextMuted;
        Resources["ButtonBorderBrushDisabled"] = StalkerTheme.BrushBorderSubtle;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.Exit += (_, _) => Services.GameAudioService.ShutdownIfStarted();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            // Web host: the same interface without a window.
            var viewModel = new ViewModels.SaveLibraryViewModel();
            foreach (var path in Services.HostPlatform.StartupFiles) viewModel.AddPreviewSave(path);
            singleView.MainView = MainWindow.BuildRoot(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
