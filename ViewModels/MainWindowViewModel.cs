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
using System.Diagnostics;
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

            // Initialize commands
            StartAutomationCommand = new SimpleCommand(StartAutomationAsync, () => !IsAutomationRunning);
            StopAutomationCommand = new SimpleCommand(StopAutomationAsync, () => IsAutomationRunning);
            OpenSettingsCommand = new SimpleCommand(OpenSettingsAsync);
            GetCookiesCommand = new SimpleCommand(GetCookiesFromBrowserAsync);
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

        // Commands - All declared here
        public ICommand StartAutomationCommand { get; }
        public ICommand StopAutomationCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand GetCookiesCommand { get; }

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

        private async Task GetCookiesFromBrowserAsync()
        {
            try
            {
                UpdateStatus("Opening browser for login. Please complete the login process...");

                var loginUrl = "https://iskole.net/elev/?ojr=login";

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = loginUrl,
                            UseShellExecute = true
                        });
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        Process.Start("open", loginUrl);
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        Process.Start("xdg-open", loginUrl);
                    }

                    UpdateStatus("Browser opened. After logging in, follow these steps:\n\n" +
                               "1. Press F12 to open Developer Tools\n" +
                               "2. Go to Application → Cookies → https://iskole.net\n" +
                               "3. Find and copy these cookies:\n" +
                               "   - JSESSIONID\n" +
                               "   - _WL_AUTHCOOKIE_JSESSIONID\n" +
                               "   - X-Oracle-BMC-LBS-Route\n" +
                               "4. Click 'Settings' to open cookies.json file\n" +
                               "5. Replace placeholder values with real cookie values\n" +
                               "6. Click 'Start Automation'");
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Could not open browser: {ex.Message}\n\nManually go to: {loginUrl}\n\nThen follow cookie extraction instructions above.");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
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
            const string cookiesPath = "cookies.json";

            if (!File.Exists(cookiesPath))
            {
                await CreateSampleCookiesFileAsync(cookiesPath);
                UpdateStatus($"Created sample cookies.json at: {Path.GetFullPath(cookiesPath)}");
            }
            else
            {
                UpdateStatus($"Opening cookies file location: {Path.GetFullPath(cookiesPath)}");
            }

            try
            {
                var fullPath = Path.GetFullPath(cookiesPath);
                var directory = Path.GetDirectoryName(fullPath);

                if (OperatingSystem.IsWindows())
                {
                    Process.Start("explorer.exe", directory);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", directory);
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", directory);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Could not open folder: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task RunAutomationLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    UpdateStatus("Fetching schedule data...");

                    var scheduleData = await GetScheduleDataAsync(cancellationToken);

                    if (scheduleData?.Items != null && scheduleData.Items.Any())
                    {
                        UpdateStatus($"Found {scheduleData.Items.Count} schedule items");

                        foreach (var item in scheduleData.Items)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            if (ShouldMarkAttendance(item))
                            {
                                UpdateStatus($"Marking attendance for {item.Fag} at {item.StartKl}");
                                await MarkAttendanceAsync(item, cancellationToken);
                                UpdateStatus($"Successfully marked attendance for {item.Fag}");
                                await Task.Delay(2000, cancellationToken);
                            }
                        }

                        UpdateStatus("Completed processing all schedule items");
                    }
                    else
                    {
                        UpdateStatus("No schedule data found");
                    }

                    UpdateStatus("Waiting 5 minutes for next check...");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    UpdateStatus("Automation cancelled by user");
                    throw;
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error: {ex.Message}");
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
                    throw new Exception("Failed to load cookies from cookies.json. Use 'Get Cookies' button to extract cookies from browser.");
                }

                var startDate = DateTime.Now.ToString("yyyyMMdd");
                var endDate = DateTime.Now.AddDays(7).ToString("yyyyMMdd");

                var baseUrl = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev";
                var finder = $"RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312,startDate={startDate},endDate={endDate}";
                var url = $"{baseUrl}?finder={Uri.EscapeDataString(finder)}&onlyData=true&limit=1000&totalResults=true";

                var request = new HttpRequestMessage(HttpMethod.Get, url);

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
                    new { fravaerstype = "M" },
                    new { ip = await GetPublicIpAsync() }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/vnd.oracle.adf.action+json");

            var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

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
                    await CreateSampleCookiesFileAsync(cookiesPath);
                    throw new FileNotFoundException($"Created cookies.json template at: {Path.GetFullPath(cookiesPath)}. Please use 'Get Cookies' button to get real cookies from browser.");
                }

                var jsonString = await File.ReadAllTextAsync(cookiesPath);

                if (string.IsNullOrWhiteSpace(jsonString))
                {
                    throw new Exception("cookies.json file is empty");
                }

                var cookiesArray = JsonSerializer.Deserialize<Cookie[]>(jsonString);

                if (cookiesArray == null || !cookiesArray.Any())
                {
                    throw new Exception("No cookies found in cookies.json");
                }

                var cookieDict = cookiesArray.ToDictionary(c => c.Name, c => c.Value);

                var requiredCookies = new[] { "JSESSIONID", "_WL_AUTHCOOKIE_JSESSIONID" };
                var invalidCookies = requiredCookies.Where(rc => !cookieDict.ContainsKey(rc) ||
                    cookieDict[rc].StartsWith("YOUR_") ||
                    string.IsNullOrWhiteSpace(cookieDict[rc])).ToArray();

                if (invalidCookies.Any())
                {
                    throw new Exception($"Invalid cookies detected: {string.Join(", ", invalidCookies)}. Please use 'Get Cookies' button to extract real cookies from browser.");
                }

                return cookieDict;
            }
            catch (Exception ex)
            {
                throw new Exception($"Cookie error: {ex.Message}", ex);
            }
        }

        private async Task CreateSampleCookiesFileAsync(string cookiesPath)
        {
            var sampleCookies = new Cookie[]
            {
                new Cookie { Name = "JSESSIONID", Value = "YOUR_JSESSIONID_HERE" },
                new Cookie { Name = "_WL_AUTHCOOKIE_JSESSIONID", Value = "YOUR_WL_AUTHCOOKIE_JSESSIONID_HERE" },
                new Cookie { Name = "X-Oracle-BMC-LBS-Route", Value = "YOUR_ORACLE_BMC_LBS_ROUTE_HERE" }
            };

            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonString = JsonSerializer.Serialize(sampleCookies, jsonOptions);
            await File.WriteAllTextAsync(cookiesPath, jsonString);
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
                return "0.0.0.0";
            }
        }

        private bool ShouldMarkAttendance(ScheduleItem item)
        {
            try
            {
                if (!DateTime.TryParseExact(item.Dato, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var itemDate))
                {
                    return false;
                }

                if (!TimeSpan.TryParseExact(item.StartKl, "HHmm", null, out var startTime))
                {
                    return false;
                }

                var classDateTime = itemDate.Add(startTime);
                var now = DateTime.Now;
                var timeDiff = classDateTime - now;

                return timeDiff.TotalMinutes >= -45 && timeDiff.TotalMinutes <= 15;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _httpClient?.Dispose();
        }
    }

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