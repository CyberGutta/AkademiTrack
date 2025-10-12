using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class DataExportService
    {
        public static async Task<ExportData> CollectAllDataAsync(string userEmail, string appVersion)
        {
            Debug.WriteLine("=== COLLECTING LOCAL USER DATA ===");

            var exportData = new ExportData
            {
                ExportMetadata = new ExportMetadata
                {
                    ExportDate = DateTime.Now,
                    AppVersion = appVersion,
                    UserEmail = userEmail,
                    MachineName = Environment.MachineName
                },
                Local = await CollectLocalDataAsync(),
                RemoteDataMessage = "For database data (STU registrations, profile, etc.), please visit:\nhttps://cybergutta.github.io/AkademietTrack/index.html\n\nLog in with your credentials, go to Settings, and scroll down to find the data export option."
            };

            Debug.WriteLine($"✓ Local data collection complete. Files: {exportData.Local.Files.Count}");

            return exportData;
        }

        private static async Task<LocalData> CollectLocalDataAsync()
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AkademiTrack"
            );

            Debug.WriteLine($"Scanning local directory: {appDataDir}");

            var localData = new LocalData
            {
                Settings = new Dictionary<string, object>(),
                Credentials = new Dictionary<string, string>(),
                Activation = new Dictionary<string, string>(),
                Files = new List<FileInfo>(),
                FileContents = new Dictionary<string, string>()
            };

            var settingsPath = Path.Combine(appDataDir, "settings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(settingsPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (settings != null)
                    {
                        foreach (var kvp in settings)
                        {
                            localData.Settings[kvp.Key] = kvp.Value.ValueKind switch
                            {
                                JsonValueKind.String => kvp.Value.GetString(),
                                JsonValueKind.Number => kvp.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Null => null,
                                _ => kvp.Value.ToString()
                            };
                        }
                    }
                    Debug.WriteLine($"✓ Settings collected: {localData.Settings.Count} items");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Error loading settings: {ex.Message}");
                }
            }

            var activationPath = Path.Combine(appDataDir, "activation.json");
            if (File.Exists(activationPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(activationPath);
                    var activation = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (activation != null)
                    {
                        foreach (var kvp in activation)
                        {
                            localData.Activation[kvp.Key] = kvp.Value.ValueKind switch
                            {
                                JsonValueKind.String => kvp.Value.GetString() ?? "",
                                JsonValueKind.True => "True",
                                JsonValueKind.False => "False",
                                JsonValueKind.Null => "",
                                _ => kvp.Value.ToString()
                            };
                        }
                    }
                    Debug.WriteLine("✓ Activation info collected");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠️ Error loading activation: {ex.Message}");
                }
            }

            localData.Credentials["Note"] = "Credentials are encrypted for security. Original values not exported.";
            if (localData.Settings.ContainsKey("EncryptedLoginEmail"))
                localData.Credentials["HasEmail"] = (!string.IsNullOrEmpty(localData.Settings["EncryptedLoginEmail"]?.ToString())).ToString();
            if (localData.Settings.ContainsKey("EncryptedLoginPassword"))
                localData.Credentials["HasPassword"] = (!string.IsNullOrEmpty(localData.Settings["EncryptedLoginPassword"]?.ToString())).ToString();
            if (localData.Settings.ContainsKey("EncryptedSchoolName"))
                localData.Credentials["HasSchoolName"] = (!string.IsNullOrEmpty(localData.Settings["EncryptedSchoolName"]?.ToString())).ToString();

            Debug.WriteLine("✓ Credential metadata collected");

            if (Directory.Exists(appDataDir))
            {
                var files = Directory.GetFiles(appDataDir, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(file);
                        var relativePath = file.Replace(appDataDir, "[APP_DATA]");

                        localData.Files.Add(new FileInfo
                        {
                            FileName = fileInfo.Name,
                            FilePath = relativePath,
                            SizeBytes = fileInfo.Length,
                            CreatedDate = fileInfo.CreationTime,
                            ModifiedDate = fileInfo.LastWriteTime
                        });

                        try
                        {
                            var extension = fileInfo.Extension.ToLower();
                            if (extension == ".json" || extension == ".txt" || extension == ".log" || extension == ".xml" || extension == ".config")
                            {
                                var content = await File.ReadAllTextAsync(file);

                                content = SanitizeSensitiveData(content, fileInfo.Name);

                                localData.FileContents[relativePath] = content;
                                Debug.WriteLine($"✓ Read and sanitized content from: {fileInfo.Name}");
                            }
                        }
                        catch (Exception contentEx)
                        {
                            Debug.WriteLine($"⚠️ Could not read content from {fileInfo.Name}: {contentEx.Message}");
                            localData.FileContents[relativePath] = $"[Could not read file: {contentEx.Message}]";
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ Could not process file {file}: {ex.Message}");
                    }
                }
                Debug.WriteLine($"✓ File information collected: {localData.Files.Count} files");
                Debug.WriteLine($"✓ File contents collected: {localData.FileContents.Count} files");
            }

            return localData;
        }

        private static string SanitizeSensitiveData(string content, string fileName)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(content);
                var sanitized = SanitizeJsonElement(json);
                return JsonSerializer.Serialize(sanitized, new JsonSerializerOptions { WriteIndented = true });
            }
            catch
            {
                return "[File content redacted for privacy - contains sensitive information]";
            }
        }

        private static object SanitizeJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var obj = new Dictionary<string, object>();
                    foreach (var prop in element.EnumerateObject())
                    {
                        var key = prop.Name.ToLower();

                        if (key.Contains("password") || key.Contains("token") || key.Contains("secret") ||
                            key.Contains("cookie") || key.Contains("session") || key.Contains("auth") ||
                            key == "value" || key == "jsessionid" || key.Contains("encrypted"))
                        {
                            obj[prop.Name] = "[REDACTED FOR PRIVACY]";
                        }
                        else if (key.Contains("email"))
                        {
                            var email = prop.Value.GetString() ?? "";
                            obj[prop.Name] = MaskEmail(email);
                        }
                        else if (key.Contains("activationkey") || key.Contains("activation_key"))
                        {
                            var key_val = prop.Value.GetString() ?? "";
                            obj[prop.Name] = MaskActivationKey(key_val);
                        }
                        else
                        {
                            obj[prop.Name] = SanitizeJsonElement(prop.Value);
                        }
                    }
                    return obj;

                case JsonValueKind.Array:
                    var arr = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        arr.Add(SanitizeJsonElement(item));
                    }
                    return arr;

                case JsonValueKind.String:
                    return element.GetString() ?? "";
                case JsonValueKind.Number:
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.ToString();
            }
        }

        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                return "[REDACTED]";

            var parts = email.Split('@');
            if (parts.Length != 2) return "[REDACTED]";

            var localPart = parts[0];
            var domain = parts[1];

            if (localPart.Length <= 4)
                return $"{localPart[0]}***@{domain}";

            var masked = $"{localPart.Substring(0, 2)}***{localPart.Substring(localPart.Length - 2)}@{domain}";
            return masked;
        }

        private static string MaskActivationKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "[REDACTED]";

            var parts = key.Split('-');
            if (parts.Length != 4) return "[REDACTED]";

            return $"{parts[0].Substring(0, 2)}**-****-****-**{parts[3].Substring(parts[3].Length - 2)}";
        }

        public static async Task<string> ExportAsJsonAsync(ExportData data)
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"
            );

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            var filename = $"akademitrack-local-data-{timestamp}.json";
            var filepath = Path.Combine(downloadsPath, filename);

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            await File.WriteAllTextAsync(filepath, json);
            Debug.WriteLine($"✓ JSON export saved to: {filepath}");

            return filepath;
        }

        public static async Task<string> ExportAsCsvAsync(ExportData data)
        {
            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads"
            );

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            var filename = $"akademitrack-local-data-{timestamp}.csv";
            var filepath = Path.Combine(downloadsPath, filename);

            var csv = new StringBuilder();

            csv.AppendLine("=== EXPORT METADATA ===");
            csv.AppendLine($"Export Date,{data.ExportMetadata.ExportDate:yyyy-MM-dd HH:mm:ss}");
            csv.AppendLine($"App Version,{data.ExportMetadata.AppVersion}");
            csv.AppendLine($"User Email,{data.ExportMetadata.UserEmail}");
            csv.AppendLine($"Machine Name,{data.ExportMetadata.MachineName}");
            csv.AppendLine();

            csv.AppendLine("=== IMPORTANT NOTICE ===");
            csv.AppendLine("\"This export contains ONLY local data stored on your computer.\"");
            csv.AppendLine();
            csv.AppendLine("\"For database data (STU registrations, profile information, etc.), please visit:\"");
            csv.AppendLine("\"https://cybergutta.github.io/AkademietTrack/index.html\"");
            csv.AppendLine();
            csv.AppendLine("\"Instructions:\"");
            csv.AppendLine("\"1. Log in with your credentials\"");
            csv.AppendLine("\"2. Go to Settings\"");
            csv.AppendLine("\"3. Scroll down to find the data export option\"");
            csv.AppendLine();

            csv.AppendLine("=== LOCAL SETTINGS ===");
            csv.AppendLine("Setting,Value");
            foreach (var setting in data.Local.Settings)
            {
                csv.AppendLine($"{setting.Key},\"{setting.Value}\"");
            }
            csv.AppendLine();

            csv.AppendLine("=== ACTIVATION INFO ===");
            csv.AppendLine("Field,Value");
            foreach (var item in data.Local.Activation)
            {
                csv.AppendLine($"{item.Key},\"{item.Value}\"");
            }
            csv.AppendLine();

            csv.AppendLine("=== LOCAL FILES ===");
            csv.AppendLine($"Total Files,{data.Local.Files.Count}");
            csv.AppendLine("File Name,Size (KB),Created,Modified");
            foreach (var file in data.Local.Files)
            {
                var sizeKB = (file.SizeBytes / 1024.0).ToString("0.##");
                csv.AppendLine($"{file.FileName},{sizeKB},\"{file.CreatedDate:yyyy-MM-dd HH:mm:ss}\",\"{file.ModifiedDate:yyyy-MM-dd HH:mm:ss}\"");
            }
            csv.AppendLine();

            await File.WriteAllTextAsync(filepath, csv.ToString());
            Debug.WriteLine($"✓ CSV export saved to: {filepath}");

            return filepath;
        }
    }

    public class ExportData
    {
        [JsonPropertyName("export_metadata")]
        public ExportMetadata ExportMetadata { get; set; } = new();

        [JsonPropertyName("local_data")]
        public LocalData Local { get; set; } = new();

        [JsonPropertyName("database_data_instructions")]
        public string RemoteDataMessage { get; set; } = "";
    }

    public class ExportMetadata
    {
        [JsonPropertyName("export_date")]
        public DateTime ExportDate { get; set; }

        [JsonPropertyName("app_version")]
        public string AppVersion { get; set; } = "";

        [JsonPropertyName("user_email")]
        public string UserEmail { get; set; } = "";

        [JsonPropertyName("machine_name")]
        public string MachineName { get; set; } = "";
    }

    public class LocalData
    {
        [JsonPropertyName("settings")]
        public Dictionary<string, object> Settings { get; set; } = new();

        [JsonPropertyName("credentials")]
        public Dictionary<string, string> Credentials { get; set; } = new();

        [JsonPropertyName("activation")]
        public Dictionary<string, string> Activation { get; set; } = new();

        [JsonPropertyName("files")]
        public List<FileInfo> Files { get; set; } = new();

        [JsonPropertyName("file_contents")]
        public Dictionary<string, string> FileContents { get; set; } = new();
    }

    public class FileInfo
    {
        [JsonPropertyName("file_name")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("file_path")]
        public string FilePath { get; set; } = "";

        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("created_date")]
        public DateTime CreatedDate { get; set; }

        [JsonPropertyName("modified_date")]
        public DateTime ModifiedDate { get; set; }
    }

    public class ActivationData
    {
        [JsonPropertyName("IsActivated")]
        public bool IsActivated { get; set; }

        [JsonPropertyName("ActivatedAt")]
        public string? ActivatedAt { get; set; }

        [JsonPropertyName("Email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("ActivationKey")]
        public string ActivationKey { get; set; } = "";
    }
}