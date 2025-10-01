using Avalonia;
using Avalonia.Controls;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class SettingsWindow : Window
    {
        private string _selectedCategory = "login";

        public SettingsWindow()
        {
            InitializeComponent();

            var viewModel = new SettingsViewModel();
            DataContext = viewModel;

            // Handle close request from ViewModel
            viewModel.CloseRequested += (sender, args) => Close();

            SetupCategoryButtons();
            UpdateCategorySelection();
        }

        public SettingsWindow(Window owner) : this()
        {
            // Set the owner for proper modal behavior
            if (owner != null)
            {
                this.Owner = owner;
            }
        }

        private void SetupCategoryButtons()
        {
            var loginBtn = this.FindControl<Button>("LoginButton");
            var automationBtn = this.FindControl<Button>("AutomationButton");
            var systemBtn = this.FindControl<Button>("SystemButton");
            var advancedBtn = this.FindControl<Button>("AdvancedButton");
            var aboutBtn = this.FindControl<Button>("AboutButton");

            if (loginBtn != null) loginBtn.Click += (s, e) => SelectCategory("login");
            if (automationBtn != null) automationBtn.Click += (s, e) => SelectCategory("automation");
            if (systemBtn != null) systemBtn.Click += (s, e) => SelectCategory("system");
            if (advancedBtn != null) advancedBtn.Click += (s, e) => SelectCategory("advanced");
            if (aboutBtn != null) aboutBtn.Click += (s, e) => SelectCategory("about");
        }

        private void SelectCategory(string category)
        {
            _selectedCategory = category;
            UpdateCategorySelection();
        }

        private void UpdateCategorySelection()
        {
            // Update button selection state
            UpdateButtonClass("LoginButton", "login");
            UpdateButtonClass("AutomationButton", "automation");
            UpdateButtonClass("SystemButton", "system");
            UpdateButtonClass("AdvancedButton", "advanced");
            UpdateButtonClass("AboutButton", "about");

            // Update content visibility
            UpdateSectionVisibility("LoginSection", "login");
            UpdateSectionVisibility("AutomationSection", "automation");
            UpdateSectionVisibility("SystemSection", "system");
            UpdateSectionVisibility("AdvancedSection", "advanced");
            UpdateSectionVisibility("AboutSection", "about");
        }

        private void UpdateButtonClass(string buttonName, string category)
        {
            var button = this.FindControl<Button>(buttonName);
            if (button != null)
            {
                button.Classes.Remove("selected");
                if (category == _selectedCategory)
                {
                    button.Classes.Add("selected");
                }
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
    }
}