using System.Windows;
using Topiary.Services;

namespace Topiary.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ISettingsService _settingsService;
        private string _currentApiKey;

        public SettingsWindow(ISettingsService settingsService)
        {
            InitializeComponent();
            _settingsService = settingsService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            _currentApiKey = _settingsService.GetOpenAIKey();
            if (!string.IsNullOrEmpty(_currentApiKey))
            {
                ApiKeyBox.Password = _currentApiKey;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settingsService.SaveOpenAIKey(ApiKeyBox.Password);
                MessageBox.Show("Settings saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch
            {
                MessageBox.Show(
                    "There was an error saving your settings. Please try again.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void ClearApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "Are you sure you want to clear your API key? AI insights will be disabled.",
                "Confirm",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                ApiKeyBox.Clear();
                _settingsService.ClearOpenAIKey();
            }
        }

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // will add validation here
        }
    }
}