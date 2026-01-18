using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media;
using Material.Icons;

namespace AkademiTrack.Services
{
    public class ThemeManager : INotifyPropertyChanged
    {
        private static ThemeManager? _instance;
        private bool _isDarkMode;
        private readonly string _settingsFilePath;

        public static ThemeManager Instance => _instance ??= new ThemeManager();

        public event PropertyChangedEventHandler? PropertyChanged;

        private ThemeManager()
        {
            string appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );
            Directory.CreateDirectory(appDataDir);
            _settingsFilePath = Path.Combine(appDataDir, "theme_settings.json");

            LoadThemeSettings();
        }

        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                if (_isDarkMode != value)
                {
                    _isDarkMode = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ThemeIcon));
                    OnPropertyChanged(nameof(ThemeIconKind));
                    OnPropertyChanged(nameof(ThemeIconColor));
                    OnPropertyChanged(nameof(WindowBackground));
                    OnPropertyChanged(nameof(TextPrimary));
                    OnPropertyChanged(nameof(TextSecondary));
                    OnPropertyChanged(nameof(CardBackground));
                    OnPropertyChanged(nameof(CardBorder));
                    OnPropertyChanged(nameof(InputBackground));
                    OnPropertyChanged(nameof(InputBorder));
                    OnPropertyChanged(nameof(InputBorderFocus));
                    OnPropertyChanged(nameof(HoverBackground));
                    SaveThemeSettings();
                }
            }
        }

        public string ThemeIcon => _isDarkMode ? "🌙" : "☀️";

        public MaterialIconKind ThemeIconKind => _isDarkMode
            ? MaterialIconKind.WeatherNight
            : MaterialIconKind.WeatherSunny;

        public IBrush ThemeIconColor => new SolidColorBrush(_isDarkMode ? Color.Parse("#FFD700") : Color.Parse("#FFA500"));

        public IBrush WindowBackground => new SolidColorBrush(_isDarkMode ? Color.Parse("#1E1E1E") : Color.Parse("#F8F9FA"));
        public IBrush TextPrimary => new SolidColorBrush(_isDarkMode ? Color.Parse("#E0E0E0") : Color.Parse("#212529"));
        public IBrush TextSecondary => new SolidColorBrush(_isDarkMode ? Color.Parse("#A0A0A0") : Color.Parse("#6C757D"));
        public IBrush CardBackground => new SolidColorBrush(_isDarkMode ? Color.Parse("#2D2D2D") : Colors.White);
        public IBrush CardBorder => new SolidColorBrush(_isDarkMode ? Color.Parse("#404040") : Color.Parse("#E9ECEF"));
        public IBrush InputBackground => new SolidColorBrush(_isDarkMode ? Color.Parse("#2D2D2D") : Colors.White);
        public IBrush InputBorder => new SolidColorBrush(_isDarkMode ? Color.Parse("#505050") : Color.Parse("#CED4DA"));
        public IBrush InputBorderFocus => new SolidColorBrush(_isDarkMode ? Color.Parse("#007ACC") : Color.Parse("#007ACC"));
        public IBrush HoverBackground => new SolidColorBrush(_isDarkMode ? Color.Parse("#3A3A3A") : Color.Parse("#c9c9c9"));

        public void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
        }

        private void LoadThemeSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<ThemeSettings>(json);
                    _isDarkMode = settings?.IsDarkMode ?? true;
                }
                else
                {
                    _isDarkMode = true;
                }
            }
            catch
            {
                _isDarkMode = true;
            }
        }

        private void SaveThemeSettings()
        {
            try
            {
                var settings = new ThemeSettings { IsDarkMode = _isDarkMode };
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class ThemeSettings
        {
            public bool IsDarkMode { get; set; }
        }
    }
}