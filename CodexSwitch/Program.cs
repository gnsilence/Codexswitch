using CodexSwitch.Services;
using Avalonia.Media;

namespace CodexSwitch;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (StartupLaunchOptions.ShouldBootstrapClaudeConfig(args))
        {
            ClaudeBootstrapConfigWriter.TryApplyForCurrentUser();
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = AppFonts.DefaultFontFamily,
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily(AppFonts.DefaultFontFamily) }
                ]
            })
            .LogToTrace();
}
