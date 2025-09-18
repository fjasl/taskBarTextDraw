using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Application.Current.Dispatcher

namespace TaskbarTextOverlayWpf
{
    /// <summary>
    /// Spec v2：stdout 的“一行”=一帧；本版支持：
    /// - 暴露设置给 Python（TTO_SETTINGS_JSON / TTO_SETTINGS_FILE）
    /// - 解析控制指令：##SET {...} / ##RICH {...} / ##PLAIN
    /// 修复：在 BeginInvoke 里访问已释放的 JsonDocument 导致的 ObjectDisposedException。
    /// </summary>
    public sealed class PythonEngine : IDisposable
    {
        public event Action<string>? OnFrame;   // 普通文本帧
        public event Action<string>? OnError;   // 内部错误（记录，不打断 UI）

        public bool IsRunning => _proc != null && !_proc.HasExited;

        private readonly Settings _cfg;
        private Process? _proc;
        private string? _scriptPath;
        private string? _settingsFilePath;
        private DateTime _lastFrameAt = DateTime.MinValue;
        private string? _latestPendingFrame;
        private System.Timers.Timer? _throttleTimer;
        private readonly object _lock = new();

        private readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = false,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public PythonEngine(Settings cfg) { _cfg = cfg; }

        public async Task StartAsync(CancellationToken ct)
        {
            await StopAsync(); // 单实例

            // 写入临时脚本
            var tmpDir = Path.Combine(Path.GetTempPath(), "TaskbarTextOverlayWpf");
            Directory.CreateDirectory(tmpDir);
            _scriptPath = Path.Combine(tmpDir, $"snippet_{Guid.NewGuid():N}.py");
            await File.WriteAllTextAsync(_scriptPath, _cfg.PythonCode ?? string.Empty, Encoding.UTF8, ct);

            // 准备设置快照文件
            _settingsFilePath = Path.Combine(tmpDir, $"settings_{Guid.NewGuid():N}.json");
            WriteSettingsSnapshot();
            _cfg.PropertyChanged += CfgOnPropertyChanged;

            // 找 python
            var py = await ResolvePythonAsync(_cfg.PythonPath, ct);
            if (py == null) { RaiseError("找不到 Python 解释器（请在 PATH 中提供 python 或在设置指定 PythonPath）。"); return; }

            var psi = new ProcessStartInfo
            {
                FileName = py,
                Arguments = $"-I -X utf8 \"{_scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["TTO_API"] = "1";
            psi.Environment["TTO_SETTINGS_FILE"] = _settingsFilePath!;
            psi.Environment["TTO_SETTINGS_JSON"] = JsonSerializer.Serialize(_cfg, _json);

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += Proc_OutputDataReceived;
            _proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) RaiseError("STDERR: " + e.Data); };
            _proc.Exited += (s, e) => { /* 退出后保留最后一帧 */ };

            try
            {
                if (!_proc.Start()) { RaiseError("Python 进程启动失败。"); await StopAsync(); return; }
            }
            catch (Exception ex) { RaiseError("启动异常: " + ex.Message); await StopAsync(); return; }

            _proc.BeginOutputReadLine();

            // 节流：取最新一帧
            _throttleTimer = new System.Timers.Timer(Math.Max(10, _cfg.MinIntervalMs));
            _throttleTimer.Elapsed += (s, e) => FlushPendingFrame();
            _throttleTimer.AutoReset = true;
            _throttleTimer.Start();

            // 启动超时
            _ = Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(_cfg.StartTimeoutMs);
                while (IsRunning && DateTime.UtcNow < deadline)
                {
                    if (_lastFrameAt != DateTime.MinValue) return;
                    await Task.Delay(50);
                }
                if (_lastFrameAt == DateTime.MinValue) RaiseError("启动超时：在 start_timeout_ms 内未收到任何输出帧。");
            }, ct);

            // 静默监控
            _ = Task.Run(async () =>
            {
                while (IsRunning)
                {
                    var ttl = _cfg.InactivityTimeoutMs;
                    if (ttl > 0 && _lastFrameAt != DateTime.MinValue &&
                        (DateTime.UtcNow - _lastFrameAt).TotalMilliseconds > ttl)
                    {
                        RaiseError("静默超时：长时间未收到输出帧。");
                        await Task.Delay(Math.Min(ttl, 2000));
                    }
                    await Task.Delay(250);
                }
            }, ct);
        }

        private void CfgOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try { WriteSettingsSnapshot(); } catch { }
        }

        private void Proc_OutputDataReceived(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            var s = e.Data;

            // 指令前缀
            if (s.StartsWith("##SET ", StringComparison.Ordinal))
            {
                HandleSetCommand(s.AsSpan(6));
                return;
            }
            if (s.StartsWith("##RICH ", StringComparison.Ordinal))
            {
                HandleRichCommand(s.AsSpan(7));
                return;
            }
            if (s.Equals("##PLAIN", StringComparison.Ordinal))
            {
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() => { _cfg.UseRich = false; }));
                return;
            }

            // 普通文本帧
            var cleaned = CleanFrame(s);
            if (cleaned == null) return;
            lock (_lock) { _latestPendingFrame = cleaned; }
        }

        private void FlushPendingFrame()
        {
            string? toEmit = null;
            lock (_lock)
            {
                if (_latestPendingFrame != null)
                {
                    toEmit = _latestPendingFrame;
                    _latestPendingFrame = null;
                }
            }
            if (toEmit != null)
            {
                _lastFrameAt = DateTime.UtcNow;
                try { OnFrame?.Invoke(toEmit); } catch { }
            }
        }

        // -------- 指令解析：先物化，再调度到 UI 线程 --------

        // ##SET { ... }  —— 部分更新设置（白名单）
        private void HandleSetCommand(ReadOnlySpan<char> jsonSpan)
        {
            try
            {
                // 先把要更新的值读出来，做成“操作列表”，避免在 UI 线程里访问 JsonDocument
                var ops = new List<Action<Settings>>();

                using (var doc = JsonDocument.Parse(new string(jsonSpan)))
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return;

                    foreach (var prop in root.EnumerateObject())
                    {
                        switch (prop.Name)
                        {
                            case "Text":
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    var v = prop.Value.GetString() ?? string.Empty;
                                    ops.Add(s => s.Text = v);
                                }
                                break;
                            case "FontFamily":
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    var v = prop.Value.GetString() ?? "";
                                    ops.Add(s => s.FontFamily = v);
                                }
                                break;
                            case "FontSize":
                                if (prop.Value.TryGetDouble(out var fs))
                                {
                                    var v = fs;
                                    ops.Add(s => s.FontSize = v);
                                }
                                break;
                            case "IsBold":
                                if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                                {
                                    var v = prop.Value.GetBoolean();
                                    ops.Add(s => s.IsBold = v);
                                }
                                break;
                            case "ForegroundHex":
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    var v = prop.Value.GetString() ?? "";
                                    ops.Add(s => s.ForegroundHex = v);
                                }
                                break;
                            case "Alignment":
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                {
                                    var t = prop.Value.GetString() ?? "";
                                    if (Enum.TryParse<System.Windows.TextAlignment>(t, true, out var al))
                                    {
                                        var v = al;
                                        ops.Add(s => s.Alignment = v);
                                    }
                                }
                                else if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var ai))
                                {
                                    if (Enum.IsDefined(typeof(System.Windows.TextAlignment), ai))
                                    {
                                        var v = (System.Windows.TextAlignment)ai;
                                        ops.Add(s => s.Alignment = v);
                                    }
                                }
                                break;
                            case "ShadowOpacity":
                                if (prop.Value.TryGetDouble(out var op))
                                {
                                    var v = op;
                                    ops.Add(s => s.ShadowOpacity = v);
                                }
                                break;
                            case "ShadowBlur":
                                if (prop.Value.TryGetDouble(out var br))
                                {
                                    var v = br;
                                    ops.Add(s => s.ShadowBlur = v);
                                }
                                break;
                        }
                    }
                }

                if (ops.Count == 0) return;

                // 在 UI 线程执行更新
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    foreach (var op in ops)
                    {
                        try { op(_cfg); } catch { }
                    }
                }));
            }
            catch (Exception ex)
            {
                RaiseError("SET 解析失败: " + ex.Message);
            }
        }

        // ##RICH {"segments":[ {"t":"A","fg":"#FFFF0000"}, ... ]}
        private void HandleRichCommand(ReadOnlySpan<char> jsonSpan)
        {
            try
            {
                // 先把 segments 物化成列表（避免 JsonDocument 生命周期问题）
                var segs = new List<RichSegment>();
                using (var doc = JsonDocument.Parse(new string(jsonSpan)))
                {
                    if (!doc.RootElement.TryGetProperty("segments", out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return;

                    int count = 0;
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (count++ > 500) break;
                        if (el.ValueKind != JsonValueKind.Object) continue;

                        string text =
                            (el.TryGetProperty("t", out var tEl) && tEl.ValueKind == JsonValueKind.String)
                                ? (tEl.GetString() ?? "")
                                : ((el.TryGetProperty("text", out var t2) && t2.ValueKind == JsonValueKind.String)
                                    ? (t2.GetString() ?? "")
                                    : "");

                        string? fg =
                            (el.TryGetProperty("fg", out var fgEl) && fgEl.ValueKind == JsonValueKind.String)
                                ? fgEl.GetString()
                                : null;

                        var seg = new RichSegment { Text = text };
                        seg.ForegroundHex = !string.IsNullOrWhiteSpace(fg) ? fg! : _cfg.ForegroundHex;
                        segs.Add(seg);
                    }
                }

                // 在 UI 线程替换集合
                System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    _cfg.UseRich = true;
                    _cfg.RichSegments.Clear();
                    foreach (var seg in segs) _cfg.RichSegments.Add(seg);
                }));
            }
            catch (Exception ex)
            {
                RaiseError("RICH 解析失败: " + ex.Message);
            }
        }

        // 清洗与安全截断
        private string? CleanFrame(string s)
        {
            if (s is null) return null;

            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == '\r' || ch == '\n') { sb.Append(' '); continue; }
                if (ch < 0x20 && ch != '\t') continue;
                sb.Append(ch == '\t' ? ' ' : ch);
            }
            var str = sb.ToString();

            int limit = Math.Max(256, _cfg.MaxBytesPerFrame);
            var utf8 = Encoding.UTF8;
            if (utf8.GetByteCount(str) <= limit) return str;

            var acc = new StringBuilder();
            int bytes = 0;
            foreach (var rune in str.EnumerateRunes())
            {
                string rs = rune.ToString();
                int n = utf8.GetByteCount(rs);
                if (bytes + n > limit - 3) break;
                bytes += n;
                acc.Append(rs);
            }
            acc.Append('…');
            return acc.ToString();
        }

        private void WriteSettingsSnapshot()
        {
            if (_settingsFilePath == null) return;
            var tmp = _settingsFilePath + ".tmp";
            var json = JsonSerializer.Serialize(_cfg, _json);
            File.WriteAllText(tmp, json, Encoding.UTF8);
            try
            {
                if (File.Exists(_settingsFilePath)) File.Replace(tmp, _settingsFilePath, null);
                else File.Move(tmp, _settingsFilePath);
            }
            catch
            {
                File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);
                try { File.Delete(tmp); } catch { }
            }
        }

        public async Task StopAsync()
        {
            _throttleTimer?.Stop();
            _throttleTimer?.Dispose();
            _throttleTimer = null;

            _cfg.PropertyChanged -= CfgOnPropertyChanged;

            if (_proc != null)
            {
                try
                {
                    if (!_proc.HasExited)
                    {
                        try { _proc.CancelOutputRead(); } catch { }
                        try { _proc.Kill(entireProcessTree: true); } catch { }
                        await Task.Delay(50);
                    }
                }
                catch { }
                finally { _proc.Dispose(); _proc = null; }
            }

            if (_scriptPath != null) { try { File.Delete(_scriptPath); } catch { } _scriptPath = null; }
            if (_settingsFilePath != null) { try { File.Delete(_settingsFilePath); } catch { } _settingsFilePath = null; }
        }

        private static async Task<string?> ResolvePythonAsync(string? userPath, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath)) return userPath;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "py",
                    Arguments = "-3 -c \"import sys;print(sys.executable)\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using var p = Process.Start(psi)!;
                string path = await p.StandardOutput.ReadToEndAsync();
                p.WaitForExit(1500);
                path = path.Trim();
                if (File.Exists(path)) return path;
            }
            catch { }

            return "python";
        }

        private void RaiseError(string msg) { try { OnError?.Invoke(msg); } catch { } }

        public void Dispose() { _ = StopAsync(); }
    }
}
