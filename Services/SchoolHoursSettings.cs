using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AkademiTrack.Services
{
    public class DaySchedule
    {
        public bool IsEnabled { get; set; } = true;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public DaySchedule()
        {
            StartTime = new TimeSpan(8, 15, 0);
            EndTime = new TimeSpan(15, 15, 0);
        }

        public DaySchedule(bool isEnabled, TimeSpan start, TimeSpan end)
        {
            IsEnabled = isEnabled;
            StartTime = start;
            EndTime = end;
        }
    }

    public class SchoolHoursSettings
    {
        public Dictionary<DayOfWeek, DaySchedule> WeekSchedule { get; set; }

        public SchoolHoursSettings()
        {
            WeekSchedule = new Dictionary<DayOfWeek, DaySchedule>
            {
                { DayOfWeek.Monday, new DaySchedule(true, new TimeSpan(9, 0, 0), new TimeSpan(15, 15, 0)) },
                { DayOfWeek.Tuesday, new DaySchedule(true, new TimeSpan(8, 15, 0), new TimeSpan(15, 15, 0)) },
                { DayOfWeek.Wednesday, new DaySchedule(true, new TimeSpan(8, 15, 0), new TimeSpan(15, 15, 0)) },
                { DayOfWeek.Thursday, new DaySchedule(true, new TimeSpan(8, 15, 0), new TimeSpan(15, 15, 0)) },
                { DayOfWeek.Friday, new DaySchedule(true, new TimeSpan(8, 15, 0), new TimeSpan(15, 15, 0)) },
                { DayOfWeek.Saturday, new DaySchedule(false, new TimeSpan(8, 0, 0), new TimeSpan(15, 0, 0)) },
                { DayOfWeek.Sunday, new DaySchedule(false, new TimeSpan(8, 0, 0), new TimeSpan(15, 0, 0)) }
            };
        }

        public static SchoolHoursSettings GetDefault()
        {
            return new SchoolHoursSettings();
        }

        public bool IsDayEnabled(DayOfWeek day)
        {
            return WeekSchedule.ContainsKey(day) && WeekSchedule[day].IsEnabled;
        }

        public (TimeSpan start, TimeSpan end) GetDayTimes(DayOfWeek day)
        {
            if (WeekSchedule.ContainsKey(day))
            {
                var schedule = WeekSchedule[day];
                return (schedule.StartTime, schedule.EndTime);
            }
            return (new TimeSpan(8, 15, 0), new TimeSpan(15, 15, 0));
        }
    }
}