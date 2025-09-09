using Avalonia.Data.Converters;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    // Converters
    public class BoolToStringConverter : IValueConverter
    {
        public static readonly BoolToStringConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && parameter is string paramString)
            {
                var parts = paramString.Split('|');
                if (parts.Length == 2)
                {
                    return boolValue ? parts[1] : parts[0];
                }
            }
            return value?.ToString() ?? string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class StringEqualityConverter : IValueConverter
    {
        public static readonly StringEqualityConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string stringValue && parameter is string paramString)
            {
                return string.Equals(stringValue, paramString, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // Simple Command implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    // Application info class
    public class ApplicationInfo
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }

        public ApplicationInfo()
        {
            var assembly = Assembly.GetExecutingAssembly();
            Name = assembly.GetName().Name ?? "AkademiTrack";
            Version = assembly.GetName().Version?.ToString() ?? "1.0.0.0";
            Description = "Academic tracking application";
        }
    }

    // ViewModel
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CloseRequested;

        public ApplicationInfo ApplicationInfo { get; }
        public ICommand CloseCommand { get; }
        public ICommand OpenProgramFolderCommand { get; }

        public SettingsViewModel()
        {
            ApplicationInfo = new ApplicationInfo();
            CloseCommand = new RelayCommand(CloseWindow);
            OpenProgramFolderCommand = new RelayCommand(OpenProgramFolder);
        }

        private void CloseWindow()
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenProgramFolder()
        {
            try
            {
                var programPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(programPath) && Directory.Exists(programPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = programPath,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                // Handle error - you might want to show a message to the user
                Debug.WriteLine($"Error opening program folder: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}