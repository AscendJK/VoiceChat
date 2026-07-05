using System.IO;
using System.Text.Json;

namespace VoiceChat.App.Services;

/// <summary>
/// 用户设置持久化服务
/// </summary>
public class UserSettings
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceChat");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string CaptureDeviceId { get; set; } = string.Empty;
    public string PlaybackDeviceId { get; set; } = string.Empty;
    public bool NoiseGateEnabled { get; set; } = true;
    public float NoiseGateThreshold { get; set; } = 0.005f;
    public int QualityIndex { get; set; } = 2;
    public string UserName { get; set; } = Environment.UserName;
    public string RoomName { get; set; } = $"{Environment.UserName}的房间";

    private static UserSettings? _instance;
    public static UserSettings Instance => _instance ??= Load();

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
            }
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
