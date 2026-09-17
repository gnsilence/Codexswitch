using Avalonia.Markup.Xaml;

namespace CodexSwitch.Views.Dialogs;

public partial class CodexSessionMigrationDialog : UserControl
{
    public CodexSessionMigrationDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
