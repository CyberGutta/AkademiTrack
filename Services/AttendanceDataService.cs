using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class AttendanceDataService
    {
        private readonly HttpClient _httpClient;
        private UserParameters? _userParameters;
        private Dictionary<string, string>? _cookies;

        public AttendanceDataService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public void SetCredentials(UserParameters parameters, Dictionary<string, string> cookies)
        {
            _userParameters = parameters;
            _cookies = cookies;
        }

        // Fetch summary statistics (over/undertid, total hours, etc.)
        public async Task<AttendanceSummary?> GetAttendanceSummaryAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoFravaer_oppmote_studietid_sum;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=25&offset=0&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var summaryResponse = JsonSerializer.Deserialize<AttendanceSummaryResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return summaryResponse?.Items?.FirstOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Fetch today's schedule with attendance status
        public async Task<TodayScheduleData?> GetTodayScheduleAsync()
        {
            try
            {
                if (_userParameters == null || _cookies == null)
                    return null;

                var jsessionId = _cookies.GetValueOrDefault("JSESSIONID", "");
                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += $"?finder=RESTFilter;fylkeid={_userParameters.FylkeId},planperi={_userParameters.PlanPeri},skoleid={_userParameters.SkoleId}&onlyData=true&limit=99&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");

                var cookieString = string.Join("; ", _cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (scheduleResponse?.Items == null)
                    return null;

                return ProcessTodaySchedule(scheduleResponse.Items);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private TimeSpan? ParseTimeString(string? timeStr)
        {
            if (string.IsNullOrEmpty(timeStr) || timeStr.Length != 4)
                return null;

            try
            {
                // Parse time in format "HHmm" (e.g., "0815", "1330")
                if (int.TryParse(timeStr.Substring(0, 2), out int hours) &&
                    int.TryParse(timeStr.Substring(2, 2), out int minutes))
                {
                    if (hours >= 0 && hours < 24 && minutes >= 0 && minutes < 60)
                    {
                        return new TimeSpan(hours, minutes, 0);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private TodayScheduleData ProcessTodaySchedule(List<ScheduleItem> items)
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            var todayItems = items.Where(i => i.Dato == today).ToList();

            // Get STU sessions
            var stuSessions = todayItems.Where(i => i.KNavn == "STU").ToList();
            var registeredStuCount = stuSessions.Count(s => s.Typefravaer == "M");
            var totalStuCount = stuSessions.Count;

            var now = DateTime.Now.TimeOfDay;

            // Get current class (if ongoing) - include ALL subjects
            var currentClasses = todayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl) && !string.IsNullOrEmpty(i.SluttKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeString(i.StartKl),
                    EndTime = ParseTimeString(i.SluttKl)
                })
                .Where(x => x.StartTime.HasValue && x.EndTime.HasValue && 
                           x.StartTime.Value <= now && x.EndTime.Value >= now)
                .OrderBy(x => x.StartTime)
                .ToList();

            ScheduleItem? currentClass = currentClasses.FirstOrDefault()?.Item;

            // Get next class - include ALL subjects (STU and regular classes)
            var upcomingClasses = todayItems
                .Where(i => !string.IsNullOrEmpty(i.StartKl))
                .Select(i => new
                {
                    Item = i,
                    StartTime = ParseTimeString(i.StartKl)
                })
                .Where(x => x.StartTime.HasValue && x.StartTime.Value > now)
                .OrderBy(x => x.StartTime)
                .ToList();

            ScheduleItem? nextClass = upcomingClasses.FirstOrDefault()?.Item;

            return new TodayScheduleData
            {
                RegisteredStuSessions = registeredStuCount,
                TotalStuSessions = totalStuCount,
                NextClass = nextClass,
                CurrentClass = currentClass,
                AllTodayItems = todayItems
            };
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Data Models
    public class UserParameters
    {
        public string? FylkeId { get; set; }
        public string? PlanPeri { get; set; }
        public string? SkoleId { get; set; }

        public bool IsComplete => !string.IsNullOrEmpty(FylkeId) &&
                                  !string.IsNullOrEmpty(PlanPeri) &&
                                  !string.IsNullOrEmpty(SkoleId);
    }

    public class AttendanceSummary
    {
        public int Elevnr { get; set; }
        public int DagerUndervist { get; set; }  // Days taught so far
        public double TimerMinimum { get; set; }  // Minimum hours required
        public double Tilstede { get; set; }      // Total hours attended
        public double Prosent { get; set; }       // Percentage
        public double Saldo { get; set; }         // Over/undertid (overtime/undertime in hours)
    }

    public class AttendanceSummaryResponse
    {
        public List<AttendanceSummary>? Items { get; set; }
        public int TotalResults { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string? Fag { get; set; }
        public string? Stkode { get; set; }
        public string? KlTrinn { get; set; }
        public string? KlId { get; set; }
        public string? KNavn { get; set; }
        public string? GruppeNr { get; set; }
        public string? Dato { get; set; }
        public int Timenr { get; set; }
        public string? StartKl { get; set; }
        public string? SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string? Typefravaer { get; set; }  // "M" means registered
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string? TidsromTilstedevaerelse { get; set; }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem>? Items { get; set; }
        public int TotalResults { get; set; }
    }

    public class TodayScheduleData
    {
        public int RegisteredStuSessions { get; set; }
        public int TotalStuSessions { get; set; }
        public ScheduleItem? NextClass { get; set; }
        public ScheduleItem? CurrentClass { get; set; }
        public List<ScheduleItem>? AllTodayItems { get; set; }
    }
}