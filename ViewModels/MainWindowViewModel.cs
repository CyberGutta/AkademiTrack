using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;

namespace AkademiTrack.ViewModels
{
    public class SimpleCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public SimpleCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter)) return;

            _isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public partial class MainWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isAutomationRunning;
        private string _statusMessage;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindowViewModel()
        {
            _httpClient = new HttpClient();
            _statusMessage = "Ready to start automation";

            // Initialize simple commands
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync, () => !IsAutomationRunning);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync, () => IsAutomationRunning);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
        }

        public string Greeting { get; } = "AkademiTrack Automation System";

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAutomationRunning
        {
            get => _isAutomationRunning;
            private set
            {
                if (_isAutomationRunning != value)
                {
                    _isAutomationRunning = value;
                    OnPropertyChanged();

                    // Update command states
                    ((SimpleCommand)StartAutomationCommand).RaiseCanExecuteChanged();
                    ((SimpleCommand)StopAutomationCommand).RaiseCanExecuteChanged();
                }
            }
        }

        // Commands
        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        // Thread-safe UI update methods
        private void UpdateStatus(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                StatusMessage = message;
            }
            else
            {
                Dispatcher.UIThread.Post(() => StatusMessage = message);
            }
        }

        private void UpdateAutomationState(bool isRunning)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                IsAutomationRunning = isRunning;
            }
            else
            {
                Dispatcher.UIThread.Post(() => IsAutomationRunning = isRunning);
            }
        }

        private async Task StartAutomationAsync()
        {
            if (IsAutomationRunning)
                return;

            _cancellationTokenSource = new CancellationTokenSource();

            UpdateAutomationState(true);
            UpdateStatus("Starting automation...");

            try
            {
                // Run the automation loop on a background thread
                await Task.Run(() => RunAutomationLoop(_cancellationTokenSource.Token));
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Automation stopped by user");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Automation error: {ex.Message}");
            }
            finally
            {
                UpdateAutomationState(false);
            }
        }

        private async Task StopAutomationAsync()
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                UpdateStatus("Stopping automation...");
            }
        }

        private async Task OpenSettingsAsync()
        {
            UpdateStatus("Settings window would open here");
            // TODO: Implement settings window
            await Task.CompletedTask;
        }

        private async Task RunAutomationLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UpdateStatus("Fetching schedule data...");

                    // Get current week's data
                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    if (scheduleData?.Items != null && scheduleData.Items.Any())
                    {
                        UpdateStatus($"Found {scheduleData.Items.Count} schedule items");

                        // Process each schedule item
                        foreach (var item in scheduleData.Items)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            // Check if this is a current or upcoming class that needs attendance
                            if (ShouldMarkAttendance(item))
                            {
                                UpdateStatus($"Marking attendance for {item.Fag} at {item.StartKl}");
                                await MarkAttendanceAsync(item, cancellationToken);
                                UpdateStatus($"Successfully marked attendance for {item.Fag}");
                                await Task.Delay(2000, cancellationToken); // Small delay between requests
                            }
                        }

                        UpdateStatus("Completed processing all schedule items");
                    }
                    else
                    {
                        UpdateStatus("No schedule data found");
                    }

                    // Wait 5 minutes before next check
                    UpdateStatus("Waiting 5 minutes for next check...");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Automation cancelled by user");
                    throw; // Re-throw to exit the loop
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
                    // Wait 30 seconds before retry on error
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
            }
        }

        private async Task<ScheduleResponse> GetScheduleDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                var cookies = await LoadCookiesFromFileAsync();
                if (cookies == null || !cookies.Any())
                {
                    throw new Exception("Failed to load cookies from cookies.json. Make sure the file exists and contains valid cookies.");
                }

                // Use current date for start, and 7 days later for end
                var startDate = DateTime.Now.ToString("yyyyMMdd");
                var endDate = DateTime.Now.AddDays(7).ToString("yyyyMMdd");

                var baseUrl = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev";
                var finder = $"RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312,startDate={startDate},endDate={endDate}";

                var url = $"{baseUrl}?finder={Uri.EscapeDataString(finder)}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);

                // Add headers
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Sec-Ch-Ua", "\"Not)A;Brand\";v=\"8\", \"Chromium\";v=\"138\"");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
                request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
                request.Headers.Add("Sec-Fetch-Mode", "cors");
                request.Headers.Add("Sec-Fetch-Dest", "empty");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");
                request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
                request.Headers.Add("Priority", "u=1, i");
                request.Headers.Add("Connection", "keep-alive");

                // Add cookies
                var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await _httpClient.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    var scheduleResponse = JsonSerializer.Deserialize<ScheduleResponse>(jsonString, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return scheduleResponse;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to fetch schedule data. Status: {response.StatusCode}, Response: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error in GetScheduleDataAsync: {ex.Message}", ex);
            }
        }

        private async Task MarkAttendanceAsync(ScheduleItem item, CancellationToken cancellationToken)
        {
            var cookies = await LoadCookiesFromFileAsync();
            if (cookies == null)
            {
                throw new Exception("Failed to load cookies");
            }

            var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
            var authCookie = cookies.GetValueOrDefault("_WL_AUTHCOOKIE_JSESSIONID", "");
            var oracleRoute = cookies.GetValueOrDefault("X-Oracle-BMC-LBS-Route", "");

            // Clean JSESSIONID
            var jsessionIdClean = jsessionId.Split('!')[0];

            var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";

            var payload = new
            {
                name = "lagre_oppmote",
                parameters = new object[]
                {
                    new { fylkeid = "00" },
                    new { skoleid = "312" },
                    new { planperi = "2025-26" },
                    new { ansidato = item.Dato },
                    new { stkode = item.Stkode },
                    new { kl_trinn = item.KlTrinn },
                    new { kl_id = item.KlId },
                    new { k_navn = item.KNavn },
                    new { gruppe_nr = item.GruppeNr },
                    new { timenr = item.Timenr },
                    new { fravaerstype = "M" }, // M for present
                    new { ip = await GetPublicIpAsync() }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            // Add headers
            request.Headers.Add("Host", "iskole.net");
            request.Headers.Add("Cookie", $"X-Oracle-BMC-LBS-Route={oracleRoute}; JSESSIONID={jsessionIdClean}; _WL_AUTHCOOKIE_JSESSIONID={authCookie}");
            request.Headers.Add("Sec-Ch-Ua-Platform", "\"Windows\"");
            request.Headers.Add("Accept-Language", "nb-NO,nb;q=0.9");
            request.Headers.Add("Sec-Ch-Ua", "\"Chromium\";v=\"139\", \"Not;A=Brand\";v=\"99\"");
            request.Headers.Add("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            request.Headers.Add("Origin", "https://iskole.net");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar");
            request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            request.Headers.Add("Priority", "u=4, i");
            request.Headers.Add("Connection", "keep-alive");

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to mark attendance. Status: {response.StatusCode}, Content: {errorContent}");
            }
        }

        private async Task<Dictionary<string, string>> LoadCookiesFromFileAsync()
        {
            try
            {
                const string cookiesPath = "cookies.json";

                if (!File.Exists(cookiesPath))
                {
                    throw new FileNotFoundException($"cookies.json file not found at: {Path.GetFullPath(cookiesPath)}");
                }

                var jsonString = await File.ReadAllTextAsync(cookiesPath);

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    throw new Exception("cookies.json file is empty");
                }

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString);

                if (cookiesArray == null || !cookiesArray.Any())
                {
                    throw new Exception("No cookies found in cookies.json or invalid format");
                }

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);

                // Verify essential cookies are present
                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var missingCookies = requiredCookies.Where(rc => !cookieDict.ContainsKey(rc)).ToArray();

                if (missingCookies.Any())
                {
                    throw new Exception($"Missing required cookies: {string.Join(", ", missingCookies)}");
                }

                return cookieDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading cookies: {ex.Message}", ex);
            }
        }

        private async Task<string> GetPublicIpAsync()
        {
            try
            {
                var response = await _httpClient.GetStringAsync("https://api.ipify.org");
                return response.Trim();
            }
            catch
            {
                return "0.0.0.0"; // Fallback
            }
        }

        private bool ShouldMarkAttendance(ScheduleItem item)
        {
            try
            {
                // Parse the date and time
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate))
                {
                    Console.WriteLine($"Could not parse date: {item.Dato}");
                    return false;
                }

                if (!TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    Console.WriteLine($"Could not parse time: {item.StartKl}");
                    return false;
                }

                var classDateTime = itemDate.Add(startTime);
                var now = DateTime.Now;

                // Mark attendance if class is within the next 15 minutes or currently ongoing (up to 45 minutes after start)
                var timeDiff = classDateTime - now;

                bool shouldMark = timeDiff.TotalMinutes >= -45 && timeDiff.TotalMinutes <= 15;

                if (shouldMark)
                {
                    Console.WriteLine($"Should mark attendance for {item.Fag} - Class time: {classDateTime}, Now: {now}, Diff: {timeDiff.TotalMinutes:F1} minutes");
                }

                return shouldMark;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ShouldMarkAttendance: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _httpClient?.Dispose();
        }
    }

    // Data models remain the same...
    public class Cookie
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class ScheduleResponse
    {
        public List<ScheduleItem> Items { get; set; }
    }

    public class ScheduleItem
    {
        public int Id { get; set; }
        public string Fag { get; set; }
        public string Stkode { get; set; }
        public string KlTrinn { get; set; }
        public string KlId { get; set; }
        public string KNavn { get; set; }
        public string GruppeNr { get; set; }
        public string Dato { get; set; }
        public long Timenr { get; set; }
        public string StartKl { get; set; }
        public string SluttKl { get; set; }
        public int UndervisningPaagaar { get; set; }
        public string Typefravaer { get; set; }
        public int ElevForerTilstedevaerelse { get; set; }
        public int Kollisjon { get; set; }
        public string TidsromTilstedevaerelse { get; set; }
    }
}