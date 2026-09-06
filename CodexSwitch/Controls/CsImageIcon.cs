using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CodexSwitch.Controls;

public sealed class CsImageIcon : Image
{
    public static readonly StyledProperty<string> PathProperty =
        AvaloniaProperty.Register<CsImageIcon, string>(nameof(Path), "");

    public string Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == PathProperty)
            Source = TryLoad(Path);
    }

    private static IImage? TryLoad(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }

            if (!File.Exists(path))
                return null;

            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}
