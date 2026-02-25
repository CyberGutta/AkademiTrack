using System;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AkademiTrack.ViewModels;

namespace AkademiTrack.Views
{
    public partial class CalendarView : UserControl
    {
        public CalendarView()
        {
            InitializeComponent();
            
            // Set up commands for view model
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is CalendarViewModel vm)
                {
                    vm.SetViewCommand = new RelayCommand<string>(view =>
                    {
                        if (view != null)
                        {
                            vm.SelectedView = view;
                        }
                    });

                    vm.NavigatePreviousCommand = new RelayCommand(() => vm.NavigatePrevious());
                    vm.NavigateNextCommand = new RelayCommand(() => vm.NavigateNext());
                    vm.NavigateTodayCommand = new RelayCommand(() => vm.NavigateToday());
                }
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }

    // Simple relay command implementation
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

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T?> _execute;
        private readonly Func<T?, bool>? _canExecute;

        public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;

        public void Execute(object? parameter) => _execute((T?)parameter);

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
