using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarTextOverlayWpf
{
    public class SettingsService
    {
        private readonly string _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskbarTextOverlayWpf");
        private readonly string _file;

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true
        };

        public SettingsService()
        {
            Directory.CreateDirectory(_dir);
            _file = Path.Combine(_dir, "config.json");
        }

        public Settings Load()
        {
            try
            {
                if (File.Exists(_file))
                {
                    var json = File.ReadAllText(_file);
                    var s = JsonSerializer.Deserialize<Settings>(json, _opts);
                    if (s != null) return s;
                }
            }
            catch
            {
                // 解析失败则回退默认设置（可在此添加备份逻辑）
            }
            return new Settings();
        }

        public void Save(Settings s)
        {
            var json = JsonSerializer.Serialize(s, _opts);
            File.WriteAllText(_file, json);
        }
    }
}
