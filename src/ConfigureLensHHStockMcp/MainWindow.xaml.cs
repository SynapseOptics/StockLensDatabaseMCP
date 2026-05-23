using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace ConfigureLensHHStockMcp
{
    public partial class MainWindow : Window
    {
        // Name registered in Claude's mcpServers map. Intentionally different
        // from the main "lenshh-lt" server so both can coexist.
        private const string ServerName = "lenshh-stock";

        // Environment variable read by StockCatalog.ResolveDbPath at MCP
        // startup. Optional — if left blank, the MCP falls back to probing
        // paths relative to its own exe (production install layout).
        private const string CatalogsEnvVar = "LENSHH_CATALOGS_DIR";

        private string _serverExePath = "";
        private string _catalogsDir = "";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _serverExePath = FindServerExe();
            txtServerPath.Text = _serverExePath;

            _catalogsDir = FindCatalogsDir();
            txtCatalogsPath.Text = _catalogsDir;

            UpdateAllStatuses();
        }

        // ── Server Detection ──────────────────────────────────────────

        private string FindServerExe()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                // From ConfigureLensHHStockMcp bin/Debug/net8.0-windows -> LensHH.StockMcp bin
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "LensHH.StockMcp", "bin", "Debug", "net8.0", "LensHH.StockMcp.exe")),
                Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "LensHH.StockMcp", "bin", "Release", "net8.0", "LensHH.StockMcp.exe")),
                // Installed layout: stock-mcp subfolder sibling to this exe's parent
                Path.GetFullPath(Path.Combine(exeDir, "..", "stock-mcp", "LensHH.StockMcp.exe")),
                Path.GetFullPath(Path.Combine(exeDir, "..", "mcp", "LensHH.StockMcp.exe")),
                // Sibling to this exe
                Path.Combine(exeDir, "LensHH.StockMcp.exe"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private void BrowseServer_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select LensHH.StockMcp.exe",
                Filter = "LensHH Stock-MCP Server|LensHH.StockMcp.exe|All executables|*.exe",
                FileName = "LensHH.StockMcp.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                _serverExePath = dialog.FileName;
                txtServerPath.Text = _serverExePath;
                UpdateAllStatuses();
            }
        }

        // ── Catalogs Directory Detection ──────────────────────────────

        // The MCP itself probes for the catalog using:
        //   1. LENSHH_CATALOGS_DIR env var (set by us when configuring)
        //   2. exeDir\..\catalogs\
        //   3. exeDir\catalogs\
        //   4. dev tree five levels up
        // We do the same probe so the UI shows a sensible default the user
        // can confirm with a single click.
        private string FindCatalogsDir()
        {
            string envOverride = Environment.GetEnvironmentVariable(CatalogsEnvVar);
            if (!string.IsNullOrWhiteSpace(envOverride) &&
                File.Exists(Path.Combine(envOverride, "stock-lens-catalog.sqlite")))
                return envOverride;

            string baseDir = !string.IsNullOrEmpty(_serverExePath)
                ? Path.GetDirectoryName(_serverExePath) ?? AppDomain.CurrentDomain.BaseDirectory
                : AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine(baseDir, "..", "catalogs")),
                Path.GetFullPath(Path.Combine(baseDir, "catalogs")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "catalogs")),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(Path.Combine(candidate, "stock-lens-catalog.sqlite")))
                    return candidate;
            }

            return "";
        }

        private void BrowseCatalogs_Click(object sender, RoutedEventArgs e)
        {
            // WPF doesn't ship a folder picker in net8.0-windows by default.
            // Use the OpenFileDialog trick: ask the user to pick the SQLite
            // file itself, then strip to its directory.
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select stock-lens-catalog.sqlite (in your catalogs directory)",
                Filter = "Stock-lens catalog|stock-lens-catalog.sqlite|All files|*.*",
                FileName = "stock-lens-catalog.sqlite"
            };

            if (dialog.ShowDialog() == true)
            {
                _catalogsDir = Path.GetDirectoryName(dialog.FileName) ?? "";
                txtCatalogsPath.Text = _catalogsDir;
                UpdateAllStatuses();
            }
        }

        // ── Claude Desktop Configuration ──────────────────────────────

        private string GetClaudeDesktopConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Claude", "claude_desktop_config.json");
        }

        private bool IsClaudeDesktopConfigured()
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                    return false;

                string json = File.ReadAllText(configPath);
                var root = JsonNode.Parse(json)?.AsObject();
                var servers = root?["mcpServers"]?.AsObject();
                return servers != null && servers[ServerName] != null;
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                string configPath = GetClaudeDesktopConfigPath();
                string configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                    Directory.CreateDirectory(configDir);

                JsonObject root;
                if (File.Exists(configPath))
                {
                    string existing = File.ReadAllText(configPath);
                    root = JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
                }
                else
                {
                    root = new JsonObject();
                }

                if (root["mcpServers"] == null)
                    root["mcpServers"] = new JsonObject();

                var servers = root["mcpServers"].AsObject();

                var entry = new JsonObject
                {
                    ["command"] = _serverExePath,
                    ["args"] = new JsonArray()
                };

                // Only emit the env block if the user provided a catalogs
                // directory. Empty string → leave it out so the MCP falls
                // back to its own probe order at startup.
                if (!string.IsNullOrWhiteSpace(_catalogsDir))
                {
                    entry["env"] = new JsonObject
                    {
                        [CatalogsEnvVar] = _catalogsDir
                    };
                }

                servers[ServerName] = entry;

                var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, root.ToJsonString(writeOptions));

                SetStatus("Claude Desktop configured successfully.", true);
                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Desktop: " + ex.Message, false);
            }
        }

        private void RemoveClaudeDesktop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = GetClaudeDesktopConfigPath();
                if (!File.Exists(configPath))
                {
                    SetStatus("Claude Desktop config file not found.", false);
                    return;
                }

                string json = File.ReadAllText(configPath);
                var root = JsonNode.Parse(json)?.AsObject();
                var servers = root?["mcpServers"]?.AsObject();

                if (servers != null && servers[ServerName] != null)
                {
                    servers.Remove(ServerName);
                    var writeOptions = new JsonSerializerOptions { WriteIndented = true };
                    File.WriteAllText(configPath, root.ToJsonString(writeOptions));
                    SetStatus("Removed " + ServerName + " from Claude Desktop.", true);
                }
                else
                {
                    SetStatus(ServerName + " was not configured in Claude Desktop.", false);
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Desktop: " + ex.Message, false);
            }
        }

        // ── Claude Code Configuration ─────────────────────────────────

        // Reused verbatim from ConfigureLensHHMcp. See its file for full
        // notes on the .cmd/.bat shim handling and PATH probe rationale.
        private static string ResolveClaudePath()
        {
            string envOverride = Environment.GetEnvironmentVariable("CLAUDE_EXE");
            if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
                return envOverride;

            string[] names = { "claude.exe", "claude.cmd", "claude.bat" };

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                foreach (var name in names)
                {
                    try
                    {
                        string full = Path.Combine(dir.Trim(), name);
                        if (File.Exists(full)) return full;
                    }
                    catch { /* malformed PATH entry */ }
                }
            }

            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string[] fallbacks =
            {
                Path.Combine(userProfile, ".local", "bin", "claude.exe"),
                Path.Combine(appData, "npm", "claude.cmd"),
                Path.Combine(appData, "npm", "claude.exe"),
                Path.Combine(localAppData, "Programs", "claude", "claude.exe"),
            };

            foreach (var fb in fallbacks)
            {
                if (File.Exists(fb)) return fb;
            }

            return null;
        }

        private static ProcessStartInfo CreateClaudeProcess(string args)
        {
            string path = ResolveClaudePath();
            if (path == null) return null;

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".cmd" || ext == ".bat")
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c \"\"{path}\" {args}\"";
            }
            else
            {
                psi.FileName = path;
                psi.Arguments = args;
            }
            return psi;
        }

        private bool IsClaudeCodeConfigured()
        {
            try
            {
                var psi = CreateClaudeProcess("mcp list");
                if (psi == null) return false;

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    return output.Contains(ServerName);
                }
            }
            catch
            {
                return false;
            }
        }

        private void ConfigureClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_serverExePath) || !File.Exists(_serverExePath))
                {
                    SetStatus("Server executable not found. Use Browse to select it.", false);
                    return;
                }

                // Build claude mcp add command. The --env flag is only added
                // when the user has provided a catalogs directory; without it
                // the MCP falls back to its built-in probe order.
                string envFlag = !string.IsNullOrWhiteSpace(_catalogsDir)
                    ? $" --env {CatalogsEnvVar}=\"{_catalogsDir}\""
                    : "";
                string args = $"mcp add --transport stdio --scope user{envFlag} {ServerName} -- \"{_serverExePath}\"";

                var psi = CreateClaudeProcess(args);
                if (psi == null)
                {
                    string cmd = $"claude {args}";
                    Clipboard.SetText(cmd);
                    SetStatus("'claude' not found in PATH or known install paths. Command copied to clipboard for manual use.", false);
                    return;
                }

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Claude Code configured successfully.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error configuring Claude Code: " + ex.Message, false);
            }
        }

        private void RemoveClaudeCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = CreateClaudeProcess($"mcp remove --scope user {ServerName}");
                if (psi == null)
                {
                    SetStatus("'claude' not found in PATH or known install paths.", false);
                    return;
                }

                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(10000);

                    if (proc.ExitCode == 0)
                    {
                        SetStatus("Removed " + ServerName + " from Claude Code.", true);
                    }
                    else
                    {
                        string msg = string.IsNullOrEmpty(stderr) ? stdout : stderr;
                        SetStatus("Claude Code error: " + msg.Trim(), false);
                    }
                }

                UpdateAllStatuses();
            }
            catch (Exception ex)
            {
                SetStatus("Error removing from Claude Code: " + ex.Message, false);
            }
        }

        // ── Status Updates ────────────────────────────────────────────

        private void UpdateAllStatuses()
        {
            bool serverFound = !string.IsNullOrEmpty(_serverExePath) && File.Exists(_serverExePath);
            SetIndicator(txtServerStatus, "LensHH.StockMcp.exe", serverFound);

            // Catalogs is OPTIONAL — show neutral message if blank, green if
            // the sqlite is present at the chosen path, red if a path was
            // entered but doesn't contain the file.
            if (string.IsNullOrEmpty(_catalogsDir))
            {
                txtCatalogsStatus.Text = "ℹ catalogs directory not set — MCP will probe paths relative to its exe at startup";
                txtCatalogsStatus.Foreground = new SolidColorBrush(Color.FromRgb(96, 96, 96));
            }
            else
            {
                bool dbFound = File.Exists(Path.Combine(_catalogsDir, "stock-lens-catalog.sqlite"));
                SetIndicator(txtCatalogsStatus, "stock-lens-catalog.sqlite", dbFound,
                    "found at this path", "NOT found at this path");
            }

            bool desktopConfigured = IsClaudeDesktopConfigured();
            SetIndicator(txtDesktopStatus, "Claude Desktop", desktopConfigured, "configured", "not configured");

            bool codeConfigured = IsClaudeCodeConfigured();
            SetIndicator(txtCodeStatus, "Claude Code", codeConfigured, "configured", "not configured");
        }

        private static void SetIndicator(System.Windows.Controls.TextBlock indicator, string label, bool ok,
            string trueText = "found", string falseText = "not found")
        {
            if (ok)
            {
                indicator.Text = "✔ " + label + " " + trueText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(0, 128, 0));
            }
            else
            {
                indicator.Text = "✘ " + label + " " + falseText;
                indicator.Foreground = new SolidColorBrush(Color.FromRgb(192, 0, 0));
            }
        }

        private void SetStatus(string message, bool success)
        {
            txtStatus.Text = message;
            txtStatus.Foreground = success
                ? new SolidColorBrush(Color.FromRgb(0, 128, 0))
                : new SolidColorBrush(Color.FromRgb(192, 0, 0));
        }
    }
}
