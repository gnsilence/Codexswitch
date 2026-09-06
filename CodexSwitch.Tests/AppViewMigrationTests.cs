using System.Runtime.CompilerServices;

namespace CodexSwitch.Tests;

public sealed class AppViewMigrationTests
{
    [Fact]
    public void ActiveViewsUseCodexSwitchUiFormInputs()
    {
        var viewsRoot = FindRepoDirectory("CodexSwitch", "Views");
        var viewFiles = Directory.EnumerateFiles(viewsRoot, "*.axaml", SearchOption.AllDirectories);

        foreach (var file in viewFiles)
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotContain("<ui:CsInput", source);
            Assert.DoesNotContain("<ui:CsSelect", source);
            Assert.DoesNotContain("<ui:CsTextarea", source);
            Assert.DoesNotContain("<ui:CsSwitch", source);
            Assert.DoesNotContain("<ui:CsSegmentedControl", source);
            Assert.DoesNotContain("<ui:CsSegmentedButton", source);
            Assert.DoesNotContain("<ui:CsSection", source);
        }
    }

    [Fact]
    public void ProviderEditorDialogUsesCodexSwitchUiFormInputs()
    {
        var source = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "ProviderEditorDialog.axaml"));

        Assert.DoesNotContain("<ui:CsInput", source);
        Assert.DoesNotContain("<ui:CsSelect", source);
        Assert.DoesNotContain("<ui:CsTextarea", source);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding Id, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", source);
        Assert.Contains("<cui:CodexSelect ItemsSource=\"{Binding ProtocolOptions}\"", source);
        Assert.Contains("<cui:CodexSwitch Content=\"{i18n:Tr providerDialog.supportsCodex}\"", source);
        Assert.Contains("Text=\"{Binding SelectedBillingMultiplier, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", source);
        Assert.Contains("IsReadOnly=\"{Binding IsDefault}\"", source);
        Assert.Contains("IsEnabled=\"{Binding CanEditTarget}\"", source);
    }

    [Fact]
    public void SettingsPageUsesCodexSwitchUiFormInputs()
    {
        var source = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "SettingsPage.axaml"));

        Assert.DoesNotContain("<ui:CsInput", source);
        Assert.DoesNotContain("<ui:CsSelect", source);
        Assert.DoesNotContain("<ui:CsTextarea", source);
        Assert.DoesNotContain("<ui:CsButton", source);
        Assert.DoesNotContain("<ui:CsIconButton", source);
        Assert.Contains("<cui:CodexSelect ItemsSource=\"{Binding SupportedLanguages}\"", source);
        Assert.Contains("<cui:CodexSelect.ItemTemplate>", source);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding ProxyListenHost}\"", source);
        Assert.Contains("<cui:CodexSelect ItemsSource=\"{Binding HttpVersionOptions}\"", source);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding InboundApiKey}\"", source);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding BillingUnitTokens}\"", source);
        Assert.DoesNotContain("DefaultFastMultiplier", source);
        Assert.DoesNotContain("Gpt55FastMultiplier", source);
        Assert.Contains("<cui:CodexSwitch Content=\"{i18n:Tr settings.desktop.startWithWindows}\"", source);
        Assert.Contains("<cui:CodexSegmentedControl Grid.Row=\"1\">", source);
        Assert.Contains("<cui:CodexSegmentedButton IsSelected=\"{Binding IsGeneralSettingsVisible}\"", source);
        Assert.Contains("<cui:CodexSegmentedButton IsSelected=\"{Binding IsLightThemeSelected}\"", source);
        Assert.Contains("<cui:CodexSegmentedButton IsSelected=\"{Binding IsSystemNetworkProxySelected}\"", source);
        Assert.Contains("<cui:CodexSection Title=\"{i18n:Tr settings.language.title}\"", source);
        Assert.Contains("<cui:CodexIconButton Variant=\"Outline\"", source);
        Assert.Contains("Command=\"{Binding BackFromSettingsCommand}\"", source);
        Assert.Contains("Command=\"{Binding ApplyCommand}\"", source);
        Assert.Contains("Command=\"{Binding SaveCommand}\"", source);
    }

    [Fact]
    public void SettingsAboutPageShowsOnlyRequestedCopy()
    {
        var source = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "SettingsPage.axaml"));
        var zhCn = File.ReadAllText(FindRepoFile("CodexSwitch", "Assets", "i18n", "zh-CN.json"));

        Assert.Contains("<StackPanel IsVisible=\"{Binding IsAboutSettingsVisible}\"", source);
        Assert.Contains("settings.about.acknowledgement", source);
        Assert.Contains("settings.about.repository", source);
        Assert.Contains("settings.about.modified", source);
        Assert.Contains("settings.about.relay", source);
        Assert.Contains("settings.about.relaySuffix", source);
        Assert.Contains("Command=\"{Binding OpenExternalUrlCommand}\"", source);
        Assert.Contains("CommandParameter=\"https://github.com/AIDotNet/CodexSwitch\"", source);
        Assert.Contains("CommandParameter=\"https://aioss.cc\"", source);
        Assert.DoesNotContain("{i18n:Tr settings.version.", source);
        Assert.DoesNotContain("CheckForUpdatesCommand", source);
        Assert.DoesNotContain("OpenDownloadedUpdateCommand", source);
        Assert.DoesNotContain("OpenLatestReleaseCommand", source);
        Assert.Contains("感谢原作者开源提供帮助，下面是原仓库地址，可以给个star和关注", zhCn);
        Assert.Contains("项目原仓库地址：", zhCn);
        Assert.Contains("已经做了大量修改和原仓库不兼容，如有需求定制可参考该项目", zhCn);
        Assert.Contains("中转地址：", zhCn);
        Assert.Contains("欢迎稳定用户来体验", zhCn);
    }

    [Fact]
    public void SidebarPaletteParticipatesInThemeSwitching()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoDirectory("CodexSwitch", "Services"), "AppThemeService.cs"));
        var sidebarResources = new[]
        {
            "CsSidebarSurfaceBrush",
            "CsNavRestForegroundBrush",
            "CsNavHoverBrush",
            "CsNavActiveBrush",
            "CsNavActiveBorderBrush",
            "CsNavAccentBrush",
            "CsNavActiveForegroundBrush",
            "CsNavActiveIconBrush",
            "CsGroupLabelBrush",
            "CsProviderIconShellBrush",
            "CsProviderIconShellBorderBrush"
        };

        foreach (var resource in sidebarResources)
            Assert.Contains($"[\"{resource}\"] = new(", source);
    }

    [Fact]
    public void ApplicationThemeSuppliesLightAndDarkComponentPalettes()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoDirectory("CodexSwitch", "Services"), "AppThemeService.cs"));

        Assert.Contains("private static readonly CodexSwitchPalette LightPalette", source);
        Assert.Contains("private static readonly CodexSwitchPalette DarkPalette", source);
        Assert.Contains("LightPalette = LightPalette", source);
        Assert.Contains("DarkPalette = DarkPalette", source);
        Assert.Contains("Primary: \"#FF2F6BFF\"", source);
    }

    [Fact]
    public void PrimaryPagesUseUnifiedHeadersAndModelTableFitsItsViewport()
    {
        var pagesRoot = FindRepoDirectory("CodexSwitch", Path.Combine("Views", "Pages"));
        foreach (var page in new[] { "HomePage.axaml", "ProvidersPage.axaml", "CodexSessionsPage.axaml", "UsagePage.axaml" })
        {
            var source = File.ReadAllText(Path.Combine(pagesRoot, page));
            Assert.Contains("<ui:CodexSection Classes=\"page-header\"", source);
        }

        var models = File.ReadAllText(Path.Combine(pagesRoot, "ModelsPage.axaml"));
        Assert.Contains("x:Name=\"TableViewport\"", models);
        Assert.Contains("Width=\"{Binding #TableViewport.Bounds.Width}\"", models);
        Assert.Contains("Content=\"{i18n:Tr models.aliases}\"", models);
        Assert.Contains("Content=\"{i18n:Tr models.price.cacheCreate5m}\"", models);
        Assert.Contains("Content=\"{i18n:Tr models.price.cacheCreate1h}\"", models);
        Assert.Contains("Text=\"{Binding ActiveModelPricingProviderText}\"", models);
        Assert.Contains("Text=\"{Binding InputOfficialPriceText}\"", models);
        Assert.DoesNotContain("ProvidersText", models);
        Assert.DoesNotContain("FastMultiplierText", models);
    }

    [Fact]
    public void ProvidersPageExposesHoverPinActions()
    {
        var source = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "ProvidersPage.axaml"));

        Assert.Contains("Grid.provider-list-row:pointerover ui|CodexIconButton.provider-pin-button", source);
        Assert.Contains("Command=\"{Binding TogglePinCommand}\"", source);
        Assert.Contains("ToolTip.Tip=\"{i18n:Tr providers.pin}\"", source);
        Assert.Contains("ToolTip.Tip=\"{i18n:Tr providers.unpin}\"", source);
        Assert.Contains("Kind=\"Pin\"", source);
        Assert.Contains("Kind=\"PinOff\"", source);
        Assert.Contains("Text=\"{Binding BillingMultiplierText}\"", source);
    }

    [Fact]
    public void ModelCatalogRefreshesAfterProviderActivationAndWhenPageOpens()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoDirectory("CodexSwitch", "ViewModels"),
            "MainWindowViewModel.cs"));
        var activateStart = source.IndexOf("private async Task ActivateProviderAsync", StringComparison.Ordinal);
        var activateEnd = source.IndexOf("private async Task ChangeProviderDefaultModelAsync", activateStart, StringComparison.Ordinal);
        var pageChangedStart = source.IndexOf("partial void OnCurrentPageChanged", StringComparison.Ordinal);
        var pageChangedEnd = source.IndexOf("partial void OnCodexSessionMigratableCountChanged", pageChangedStart, StringComparison.Ordinal);

        Assert.True(activateStart >= 0 && activateEnd > activateStart);
        Assert.True(pageChangedStart >= 0 && pageChangedEnd > pageChangedStart);
        Assert.Contains("RefreshModelCatalogRows();", source[activateStart..activateEnd]);
        Assert.Contains("if (IsModelsPageVisible)", source[pageChangedStart..pageChangedEnd]);
        Assert.Contains("RefreshModelCatalogRows();", source[pageChangedStart..pageChangedEnd]);
        Assert.Contains("_pricing.Models.OrderBy(rule =>", source);
        Assert.Contains("rule.Id.StartsWith(\"gpt-\", StringComparison.OrdinalIgnoreCase) ? 0 : 1", source);
    }

    [Fact]
    public void LastFormInputSurfacesUseCodexSwitchUiControls()
    {
        var importDialog = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "CodexAuthImportDialog.axaml"));
        var claudePage = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "ClaudePage.axaml"));
        var addProviderPage = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "AddProviderPage.axaml"));

        Assert.Contains("<cui:CodexTextarea Text=\"{Binding CodexAuthImportJson, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", importDialog);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding ClaudeCodeModel, Mode=TwoWay}\"", claudePage);
        Assert.Contains("<cui:CodexSwitch Content=\"{i18n:Tr claude.think}\"", claudePage);
        Assert.Contains("<cui:CodexTextBox Text=\"{Binding DisplayName}\"", addProviderPage);
        Assert.Contains("<cui:CodexSwitch Content=\"{i18n:Tr providerDialog.supportsCodex}\"", addProviderPage);
        Assert.Contains("Text=\"{Binding SelectedBillingMultiplier, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", addProviderPage);
    }

    [Fact]
    public void ClaudePageUsesCodexSwitchUiActionButtons()
    {
        var source = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Pages", "ClaudePage.axaml"));

        Assert.DoesNotContain("<ui:CsButton", source);
        Assert.Contains("<cui:CodexButton Grid.Column=\"1\"", source);
        Assert.Contains("Command=\"{Binding ShowProvidersCommand}\"", source);
        Assert.Contains("Command=\"{Binding SelectCommand}\"", source);
        Assert.Contains("Command=\"{Binding EditCommand}\"", source);
        Assert.Contains("Command=\"{Binding #Root.DataContext.SelectClaudeCodeModelCommand}\"", source);
        Assert.Contains("Command=\"{Binding SaveClaudeCodeSettingsCommand}\"", source);
        Assert.Contains("<cui:CodexButton.LeadingIcon>", source);
    }

    [Fact]
    public void DeleteDialogsUseCodexSwitchUiActionButtons()
    {
        var deleteModelDialog = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "DeleteModelDialog.axaml"));
        var deleteProviderDialog = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "DeleteProviderDialog.axaml"));

        Assert.DoesNotContain("<ui:CsButton", deleteModelDialog);
        Assert.DoesNotContain("<ui:CsButton", deleteProviderDialog);
        Assert.Contains("<cui:CodexButton Variant=\"Outline\"", deleteModelDialog);
        Assert.Contains("<cui:CodexButton Variant=\"Destructive\"", deleteModelDialog);
        Assert.Contains("Command=\"{Binding CancelRemovePricingModelCommand}\"", deleteModelDialog);
        Assert.Contains("Command=\"{Binding ConfirmRemovePricingModelCommand}\"", deleteModelDialog);
        Assert.Contains("<cui:CodexButton Variant=\"Outline\"", deleteProviderDialog);
        Assert.Contains("<cui:CodexButton Variant=\"Destructive\"", deleteProviderDialog);
        Assert.Contains("Command=\"{Binding CancelRemoveProviderCommand}\"", deleteProviderDialog);
        Assert.Contains("Command=\"{Binding ConfirmRemoveProviderCommand}\"", deleteProviderDialog);
    }

    [Fact]
    public void EditorAndImportDialogsUseCodexSwitchUiActionButtons()
    {
        var modelEditorDialog = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "ModelEditorDialog.axaml"));
        var importDialog = File.ReadAllText(FindRepoFile("CodexSwitch", "Views", "Dialogs", "CodexAuthImportDialog.axaml"));

        Assert.DoesNotContain("<ui:CsButton", modelEditorDialog);
        Assert.DoesNotContain("<ui:CsIconButton", modelEditorDialog);
        Assert.Contains("<cui:CodexIconButton Grid.Column=\"1\"", modelEditorDialog);
        Assert.Contains("Variant=\"Ghost\"", modelEditorDialog);
        Assert.Contains("Command=\"{Binding CloseModelDialogCommand}\"", modelEditorDialog);
        Assert.Contains("Command=\"{Binding SaveModelCommand}\"", modelEditorDialog);
        Assert.Contains("<cui:CodexButton Variant=\"Outline\"", modelEditorDialog);
        Assert.Contains("<cui:CodexButton.LeadingIcon>", modelEditorDialog);

        Assert.DoesNotContain("<ui:CsButton", importDialog);
        Assert.DoesNotContain("<ui:CsIconButton", importDialog);
        Assert.Contains("<cui:CodexIconButton Grid.Column=\"1\"", importDialog);
        Assert.Contains("Variant=\"Ghost\"", importDialog);
        Assert.Contains("Command=\"{Binding CancelCodexAuthImportCommand}\"", importDialog);
        Assert.Contains("Command=\"{Binding ImportCodexAuthJsonCommand}\"", importDialog);
        Assert.Contains("<cui:CodexButton Grid.Column=\"1\"", importDialog);
        Assert.Contains("<cui:CodexButton.LeadingIcon>", importDialog);
    }

    private static string FindRepoFile(string part1, string part2, string part3, string part4, [CallerFilePath] string sourceFile = "")
    {
        var parts = new[] { part1, part2, part3, part4 };
        var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? "";
        foreach (var start in new[] { sourceDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException("Could not find repo file.", Path.Combine(parts));
    }

    private static string FindRepoDirectory(string part1, string part2, [CallerFilePath] string sourceFile = "")
    {
        var parts = new[] { part1, part2 };
        var sourceDirectory = Path.GetDirectoryName(sourceFile) ?? "";
        foreach (var start in new[] { sourceDirectory, Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException($"Could not find repo directory: {Path.Combine(parts)}");
    }
}
