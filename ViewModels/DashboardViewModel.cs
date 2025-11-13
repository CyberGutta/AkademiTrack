using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Timers;
using AkademiTrack.Services;
using Avalonia.Threading;

namespace AkademiTrack.ViewModels
{
    public class DashboardViewModel : INotifyPropertyChanged
    {
        private readonly AttendanceDataService _attendanceService;
        private readonly Timer _refreshTimer;

        // Today's STU sessions
        private string _todayDisplay = "0/0";
        private string _todayStatus = "Venter på data...";
        
        // Next class
        private string _nextClassName = "Ingen time";
        private string _nextClassTime = "--:-- - --:--";
        private string _nextClassRoom = "";
        
        // Over/Undertid
        private string _overtimeValue = "0.0";
        private string _overtimeStatus = "Balansert";
        private string _overtimeColor = "orange";

        public event PropertyChangedEventHandler? PropertyChanged;

        public DashboardViewModel()
        {
            _attendanceService = new AttendanceDataService();
            
            // Refresh data every 30 seconds
            _refreshTimer = new Timer(30000);
            _refreshTimer.Elapsed += async (s, e) => await RefreshDataAsync();
            _refreshTimer.AutoReset = true;
        }

        // Today's STU Sessions
        public string TodayDisplay
        {
            get => _todayDisplay;
            set
            {
                _todayDisplay = value;
                OnPropertyChanged(nameof(TodayDisplay));
            }
        }

        public string TodayStatus
        {
            get => _todayStatus;
            set
            {
                _todayStatus = value;
                OnPropertyChanged(nameof(TodayStatus));
            }
        }

        // Next Class
        public string NextClassName
        {
            get => _nextClassName;
            set
            {
                _nextClassName = value;
                OnPropertyChanged(nameof(NextClassName));
            }
        }

        public string NextClassTime
        {
            get => _nextClassTime;
            set
            {
                _nextClassTime = value;
                OnPropertyChanged(nameof(NextClassTime));
            }
        }

        public string NextClassRoom
        {
            get => _nextClassRoom;
            set
            {
                _nextClassRoom = value;
                OnPropertyChanged(nameof(NextClassRoom));
            }
        }

        // Over/Undertid (Overtime)
        public string OvertimeValue
        {
            get => _overtimeValue;
            set
            {
                _overtimeValue = value;
                OnPropertyChanged(nameof(OvertimeValue));
            }
        }

        public string OvertimeStatus
        {
            get => _overtimeStatus;
            set
            {
                _overtimeStatus = value;
                OnPropertyChanged(nameof(OvertimeStatus));
            }
        }

        public string OvertimeColor
        {
            get => _overtimeColor;
            set
            {
                _overtimeColor = value;
                OnPropertyChanged(nameof(OvertimeColor));
            }
        }

        public void SetCredentials(Services.UserParameters parameters, Dictionary<string, string> cookies)
        {
            _attendanceService.SetCredentials(parameters, cookies);
        }

        public async Task StartRefreshingAsync()
        {
            await RefreshDataAsync();
            _refreshTimer.Start();
        }

        public void StopRefreshing()
        {
            _refreshTimer.Stop();
        }

        public async Task RefreshDataAsync()
        {
            try
            {
                // Fetch attendance summary (over/undertid)
                var summary = await _attendanceService.GetAttendanceSummaryAsync();
                if (summary != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateOvertimeDisplay(summary);
                    });
                }

                // Fetch today's schedule
                var todayData = await _attendanceService.GetTodayScheduleAsync();
                if (todayData != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        UpdateTodayDisplay(todayData);
                        UpdateNextClassDisplay(todayData);
                    });
                }
            }
            catch (Exception)
            {
                // Log error if needed
            }
        }


        private void UpdateTodayDisplay(TodayScheduleData data)
        {
            TodayDisplay = $"{data.RegisteredStuSessions}/{data.TotalStuSessions}";
            
            if (data.RegisteredStuSessions == data.TotalStuSessions && data.TotalStuSessions > 0)
            {
                TodayStatus = "✓ Alle registrert";
            }
            else if (data.RegisteredStuSessions == 0 && data.TotalStuSessions > 0)
            {
                TodayStatus = "Ingen registrert ennå";
            }
            else if (data.RegisteredStuSessions > 0)
            {
                int remaining = data.TotalStuSessions - data.RegisteredStuSessions;
                TodayStatus = $"{remaining} gjenstår";
            }
            else
            {
                TodayStatus = "Ingen STU-økter i dag";
            }
        }

        private void UpdateNextClassDisplay(TodayScheduleData data)
        {
            // Prioritize NextClass, but fall back to CurrentClass if no next class
            var displayClass = data.NextClass ?? data.CurrentClass;
            
            if (displayClass != null)
            {
                // Get full subject name from Fag field if available
                NextClassName = GetSubjectDisplayName(displayClass);
                
                // Format time (from "1015" to "10:15")
                if (!string.IsNullOrEmpty(displayClass.StartKl) && !string.IsNullOrEmpty(displayClass.SluttKl))
                {
                    string startTime = FormatTime(displayClass.StartKl);
                    string endTime = FormatTime(displayClass.SluttKl);
                    NextClassTime = $"{startTime} - {endTime}";
                }
                else
                {
                    NextClassTime = "--:-- - --:--";
                }
                
                // Room information isn't in the API response, so we'll leave it empty or use a placeholder
                NextClassRoom = ""; // Can be updated if room data becomes available
            }
            else
            {
                NextClassName = "Ingen flere timer";
                NextClassTime = "--:-- - --:--";
                NextClassRoom = "";
            }
        }

        private string GetSubjectDisplayName(Services.ScheduleItem item)
        {
            // Map short codes to full names
            var subjectMap = new Dictionary<string, string>
            {
                { "NOR", "Norsk" },
                { "MAT", "Matematikk" },
                { "ENG", "Engelsk" },
                { "NAT", "Naturfag" },
                { "SAM", "Samfunnsfag" },
                { "HIS", "Historie" },
                { "KRO", "Kroppsøving" },
                { "MUS", "Musikk" },
                { "KHV", "Kunst og håndverk" },
                { "MAH", "Mat og helse" },
                { "STU", "Studietid" },
                { "2PY", "Matematikk 2P-Y" },
                { "MR1", "Matematikk R1" },
                { "MOM", "Morgenmøte" },
                { "FDA", "Fagdag" },
                { "FDS", "Fagdag Studietid" }
            };

            if (item.KNavn != null && subjectMap.ContainsKey(item.KNavn))
            {
                return subjectMap[item.KNavn];
            }

            // If not in map, return the KNavn as-is
            return item.KNavn ?? "Ukjent fag";
        }

        private string FormatTime(string time)
        {
            if (string.IsNullOrEmpty(time) || time.Length != 4)
                return time;

            return $"{time.Substring(0, 2)}:{time.Substring(2, 2)}";
        }

        private void UpdateOvertimeDisplay(AttendanceSummary summary)
        {
            double saldo = summary.Saldo;
            
            // Format to 1 decimal place, with sign
            OvertimeValue = saldo >= 0 ? $"+{saldo:F1}" : $"{saldo:F1}";
            
            // Determine status and color
            if (saldo > 0)
            {
                OvertimeStatus = "Du er over målet! ✓";
                OvertimeColor = "green";
            }
            else if (saldo < 0)
            {
                OvertimeStatus = "Du er under grensen!";
                OvertimeColor = "orange";
            }
            else
            {
                OvertimeStatus = "Balansert";
                OvertimeColor = "blue";
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            _attendanceService?.Dispose();
        }
    }
}