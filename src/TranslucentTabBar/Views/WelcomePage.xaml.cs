using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TranslucentTabBar.Services;

namespace TranslucentTabBar.Views;

public sealed partial class WelcomePage : Page
{
    private readonly LocalizationService _localizationService;

    public event Action? OkRequested;

    public WelcomePage(LocalizationService localizationService)
    {
        this.InitializeComponent();
        _localizationService = localizationService;
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (TitleBlock != null)
            TitleBlock.Text = _localizationService.GetString("Welcome_Title.Text");
        if (SubtitleBlock != null)
            SubtitleBlock.Text = _localizationService.GetString("Welcome_Subtitle.Text");
        if (DescriptionBlock != null)
            DescriptionBlock.Text = _localizationService.GetString("Welcome_Description.Text");
        if (GitHubButton != null)
            GitHubButton.Content = _localizationService.GetString("Welcome_GitHub.Content");
        if (VersionBlock != null)
            VersionBlock.Text = _localizationService.GetString("Welcome_Version.Text");
        if (OkButton != null)
            OkButton.Content = _localizationService.GetString("Welcome_OK.Content");
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        OkRequested?.Invoke();
    }
}
