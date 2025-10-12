using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AkademiTrack.Services
{
    public enum Season
    {
        None,
        Christmas
    }

    public class ChristmasThemeManager : INotifyPropertyChanged
    {
        private static ChristmasThemeManager ?_instance;
        private Season _currentSeason;

        public static ChristmasThemeManager Instance => _instance ??= new ChristmasThemeManager();

        public event PropertyChangedEventHandler? PropertyChanged;

        private ChristmasThemeManager()
        {
            UpdateCurrentSeason();
        }

        public Season CurrentSeason
        {
            get => _currentSeason;
            private set
            {
                if (_currentSeason != value)
                {
                    _currentSeason = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsChristmas));
                    OnPropertyChanged(nameof(ChristmasLightsVisible));
                }
            }
        }

        public bool IsChristmas => _currentSeason == Season.Christmas;

        public bool ChristmasLightsVisible => IsChristmas;

        public void UpdateCurrentSeason()
        {
            var now = DateTime.Now;
            var month = now.Month;
            var day = now.Day;

            // Christmas: December 1 - January 6
            if ((month == 12) || (month == 1 && day <= 6))
            {
                CurrentSeason = Season.Christmas;
            }
            else
            {
                CurrentSeason = Season.None;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}