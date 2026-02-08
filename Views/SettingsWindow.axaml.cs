using AkademiTrack.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using System;
using System.Diagnostics;

namespace AkademiTrack.Views
{
    public partial class SettingsWindow : UserControl
    {
        private string _selectedCategory = "login";
        private bool _isPasswordVisible = false;

        public SettingsWindow()
        {
            InitializeComponent();

            // The DataContext will be set by MainWindow.xaml binding

            // Just setup UI interactions
            this.AttachedToVisualTree += async (_, _) =>
            {
                if (DataContext is SettingsViewModel viewModel)
                {
                    await viewModel.LoadSettingsAsync();
                }
            };

            SetupCategoryButtons();
            UpdateCategorySelection();
        }

        private RefactoredMainWindowViewModel? GetMainWindowViewModel()
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    var mainWindow = desktop.MainWindow as MainWindow;
                    var viewModel = mainWindow?.DataContext as RefactoredMainWindowViewModel;

                    if (viewModel != null)
                    {
                        Debug.WriteLine($"✓ Found RefactoredMainWindowViewModel with {viewModel.LogEntries?.Count ?? 0} log entries");
                    }

                    return viewModel;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting RefactoredMainWindowViewModel: {ex.Message}");
            }
            return null;
        }

        private void SetupCategoryButtons()
        {
            var loginBtn = this.FindControl<Button>("LoginButton");
            var automationBtn = this.FindControl<Button>("AutomationButton");
            var systemBtn = this.FindControl<Button>("SystemButton");
            var advancedBtn = this.FindControl<Button>("AdvancedButton");
            var updatesBtn = this.FindControl<Button>("UpdatesButton");
            var aboutBtn = this.FindControl<Button>("AboutButton");
            var helpBtn = this.FindControl<Button>("HelpButton");

            if (loginBtn != null) loginBtn.Click += (_, _) => SelectCategory("login");
            if (automationBtn != null) automationBtn.Click += (_, _) => SelectCategory("automation");
            if (systemBtn != null) systemBtn.Click += (_, _) => SelectCategory("system");
            if (advancedBtn != null) advancedBtn.Click += (_, _) => SelectCategory("advanced");
            if (updatesBtn != null) updatesBtn.Click += (_, _) => SelectCategory("updates");
            if (aboutBtn != null) aboutBtn.Click += (_, _) => SelectCategory("about");
            if (helpBtn != null) helpBtn.Click += (_, _) => SelectCategory("help");
        }

        private void SelectCategory(string category)
        {
            _selectedCategory = category;
            UpdateCategorySelection();
        }

        private void UpdateCategorySelection()
        {
            UpdateButtonClass("LoginButton", "login");
            UpdateButtonClass("AutomationButton", "automation");
            UpdateButtonClass("SystemButton", "system");
            UpdateButtonClass("AdvancedButton", "advanced");
            UpdateButtonClass("UpdatesButton", "updates");
            UpdateButtonClass("AboutButton", "about");
            UpdateButtonClass("HelpButton", "help");

            UpdateSectionVisibility("LoginSection", "login");
            UpdateSectionVisibility("AutomationSection", "automation");
            UpdateSectionVisibility("SystemSection", "system");
            UpdateSectionVisibility("AdvancedSection", "advanced");
            UpdateSectionVisibility("UpdatesSection", "updates");
            UpdateSectionVisibility("AboutSection", "about");
            UpdateSectionVisibility("HelpSection", "help");
        }

        private void UpdateButtonClass(string buttonName, string category)
        {
            var button = this.FindControl<Button>(buttonName);
            if (button != null)
            {
                button.Classes.Remove("selected");
                if (category == _selectedCategory)
                    button.Classes.Add("selected");
            }
        }

        private void UpdateSectionVisibility(string sectionName, string category)
        {
            var section = this.FindControl<StackPanel>(sectionName);
            if (section != null)
            {
                section.IsVisible = category == _selectedCategory;
            }
        }

        private async void TogglePasswordVisibility(object? sender, RoutedEventArgs e)
        {
            var passwordTextBox = this.FindControl<TextBox>("PasswordTextBox");
            var eyeIcon = this.FindControl<Path>("EyeIcon");

            if (passwordTextBox == null || eyeIcon == null)
                return;

            if (!_isPasswordVisible)
            {
                var authService = PlatformAuthFactory.Create();
                bool verified = await authService.AuthenticateAsync("Bekreft identitet for å vise passord");

                if (!verified)
                {
                    var box = MessageBoxManager.GetMessageBoxStandard(
                        "Sikkerhet",
                        "Autentisering avbrutt eller mislyktes.\n\nPassordet kan ikke vises uten bekreftelse.",
                        ButtonEnum.Ok);

                    await box.ShowAsync();
                    return;
                }
            }

            _isPasswordVisible = !_isPasswordVisible;

            if (_isPasswordVisible)
            {
                passwordTextBox.PasswordChar = '\0';
                eyeIcon.Data = Geometry.Parse(
                    "M12 7c2.76 0 5 2.24 5 5 0 .65-.13 1.26-.36 1.83l2.92 2.92c1.51-1.26 2.7-2.89 3.43-4.75-1.73-4.39-6-7.5-11-7.5-1.4 0-2.74.25-3.98.7l2.16 2.16C10.74 7.13 11.35 7 12 7zM2 4.27l2.28 2.28.46.46C3.08 8.3 1.78 10.02 1 12c1.73 4.39 6 7.5 11 7.5 1.55 0 3.03-.3 4.38-.84l.42.42L19.73 22 21 20.73 3.27 3 2 4.27zM7.53 9.8l1.55 1.55c-.05.21-.08.43-.08.65 0 1.66 1.34 3 3 3 .22 0 .44-.03.65-.08l1.55 1.55c-.67.33-1.41.53-2.2.53-2.76 0-5-2.24-5-5 0-.79.2-1.53.53-2.2zm4.31-.78l3.15 3.15.02-.16c0-1.66-1.34-3-3-3l-.17.01z");
            }
            else
            {
                passwordTextBox.PasswordChar = '•';
                eyeIcon.Data = Geometry.Parse(
                    "M12 4.5C7 4.5 2.73 7.61 1 12c1.73 4.39 6 7.5 11 7.5s9.27-3.11 11-7.5c-1.73-4.39-6-7.5-11-7.5zM12 17c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z");
            }
        }

        
    }
}