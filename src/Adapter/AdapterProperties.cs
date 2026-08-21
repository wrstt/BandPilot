using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace BandPilot.Adapter
{
    public sealed class AdvancedProperty
    {
        public string DisplayName { get; set; }
        public string DisplayValue { get; set; }
        public string RegistryKeyword { get; set; }
        public List<string> ValidValues { get; set; }

        public bool IsBandRelated
        {
            get
            {
                string n = ((DisplayName ?? "") + " " + (RegistryKeyword ?? "")).ToLowerInvariant();
                return n.Contains("band") || n.Contains("roam") || n.Contains("wireless mode")
                    || n.Contains("channel width") || n.Contains("802.11");
            }
        }
    }

    public sealed class NetAdapterInfo
    {
        public string Name { get; set; }
        public string InterfaceDescription { get; set; }
        public string InterfaceGuid { get; set; }
        public string Status { get; set; }
    }

    /// <summary>
    /// Driver-level adapter settings, reached through the NetAdapter PowerShell
    /// module.
    ///
    /// Nothing here is hard-coded to a particular Intel driver revision: the
    /// valid values are whatever the installed driver reports, because the
    /// keyword names and the set of accepted values genuinely differ between
    /// driver versions for the same BE200 card.
    /// </summary>
    public static class AdapterProperties
    {
        private static readonly JsonSerializerOptions JsonOpts =
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public static List<NetAdapterInfo> ListAdapters()
        {
            string json = RunPowerShell(
                "Get-NetAdapter -ErrorAction Stop | Select-Object Name,InterfaceDescription,InterfaceGuid,Status | ConvertTo-Json -Compress");

            var result = new List<NetAdapterInfo>();
            foreach (JsonElement el in EnumerateArray(json))
            {
                result.Add(new NetAdapterInfo
                {
                    Name = GetString(el, "Name"),
                    InterfaceDescription = GetString(el, "InterfaceDescription"),
                    InterfaceGuid = GetString(el, "InterfaceGuid"),
                    Status = GetString(el, "Status")
                });
            }
            return result;
        }

        /// <summary>
        /// Maps a WLAN interface GUID to the adapter name the NetAdapter
        /// cmdlets expect. Falls back to a description match because a handful
        /// of drivers report the GUID without braces.
        /// </summary>
        public static string ResolveAdapterName(Guid guid, string description)
        {
            List<NetAdapterInfo> adapters = ListAdapters();
            string target = guid.ToString("B").ToUpperInvariant();

            foreach (NetAdapterInfo a in adapters)
            {
                if (string.IsNullOrEmpty(a.InterfaceGuid)) continue;
                if (string.Equals(a.InterfaceGuid.Trim().ToUpperInvariant(), target, StringComparison.Ordinal))
                    return a.Name;
            }
            foreach (NetAdapterInfo a in adapters)
            {
                if (!string.IsNullOrEmpty(description) &&
                    string.Equals(a.InterfaceDescription, description, StringComparison.OrdinalIgnoreCase))
                    return a.Name;
            }
            return null;
        }

        public static List<AdvancedProperty> GetAdvanced(string adapterName)
        {
            string script =
                "Get-NetAdapterAdvancedProperty -Name '" + Escape(adapterName) + "' -ErrorAction Stop | " +
                "Select-Object DisplayName,DisplayValue,RegistryKeyword,ValidDisplayValues | ConvertTo-Json -Compress -Depth 4";

            string json = RunPowerShell(script);
            var result = new List<AdvancedProperty>();

            foreach (JsonElement el in EnumerateArray(json))
            {
                var p = new AdvancedProperty
                {
                    DisplayName = GetString(el, "DisplayName"),
                    DisplayValue = GetString(el, "DisplayValue"),
                    RegistryKeyword = GetString(el, "RegistryKeyword"),
                    ValidValues = new List<string>()
                };

                JsonElement valid;
                if (el.TryGetProperty("ValidDisplayValues", out valid))
                {
                    if (valid.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement v in valid.EnumerateArray())
                        {
                            if (v.ValueKind == JsonValueKind.String) p.ValidValues.Add(v.GetString());
                        }
                    }
                    else if (valid.ValueKind == JsonValueKind.String)
                    {
                        p.ValidValues.Add(valid.GetString());
                    }
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Applies a driver setting. Changing these briefly resets the radio,
        /// so the caller should expect the link to drop for a second or two.
        /// </summary>
        public static void SetAdvanced(string adapterName, string registryKeyword, string displayValue)
        {
            string script =
                "Set-NetAdapterAdvancedProperty -Name '" + Escape(adapterName) + "' " +
                "-RegistryKeyword '" + Escape(registryKeyword) + "' " +
                "-DisplayValue '" + Escape(displayValue) + "' -NoRestart:$false -ErrorAction Stop";
            RunPowerShell(script);
        }

        private static IEnumerable<JsonElement> EnumerateArray(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) yield break;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch (JsonException ex)
            {
                // Swallowing this turned malformed output into an empty list,
                // which is indistinguishable from a genuine empty result.
                throw new InvalidOperationException(
                    "Could not read PowerShell's output: " + ex.Message);
            }

            using (doc)
            {
                // ConvertTo-Json emits a bare object rather than a one-element
                // array when the result set has a single row.
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement el in doc.RootElement.EnumerateArray()) yield return el.Clone();
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    yield return doc.RootElement.Clone();
                }
            }
        }

        private static string GetString(JsonElement el, string name)
        {
            JsonElement v;
            if (!el.TryGetProperty(name, out v)) return null;
            switch (v.ValueKind)
            {
                case JsonValueKind.String: return v.GetString();
                case JsonValueKind.Number: return v.ToString();
                case JsonValueKind.Null: return null;
                default: return v.ToString();
            }
        }

        private static string Escape(string s)
        {
            return (s ?? string.Empty).Replace("'", "''");
        }

        /// <summary>
        /// Runs a snippet under powershell.exe and returns stdout. Throws with
        /// stderr attached so the UI can show the real driver complaint rather
        /// than a generic failure.
        /// </summary>
        public static string RunPowerShell(string script)
        {
            return RunPowerShell(script, 60000);
        }

        public static string RunPowerShell(string script, int timeoutMs)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                            script.Replace("\"", "\\\"") + "\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using (var proc = Process.Start(psi))
            {
                // Both pipes are drained concurrently. Reading one to the end
                // before touching the other deadlocks as soon as the process
                // writes more than a pipe buffer to the stream nobody is
                // reading, and the timeout below never gets a chance to fire.
                Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = proc.StandardError.ReadToEndAsync();

                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(true); } catch (Exception) { }
                    throw new TimeoutException(
                        "PowerShell did not finish within " + (timeoutMs / 1000) + " seconds.");
                }

                string stdout = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();

                // Either signal is enough. powershell.exe exits 0 for
                // non-terminating errors, so requiring both a bad exit code AND
                // stderr meant a failed cmdlet came back as an empty result that
                // looked exactly like "this driver has no settings".
                if (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(stderr))
                {
                    string detail = string.IsNullOrWhiteSpace(stderr)
                        ? "PowerShell exited with code " + proc.ExitCode + "."
                        : stderr.Trim();
                    throw new InvalidOperationException(detail);
                }
                return stdout;
            }
        }
    }
}
