using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace PCPForwarder.Win
{
    public partial class MainForm : Form
    {
        private HttpClient _http;
        private bool _verbose = false;
        private bool _tlsInsecure = true; // always allow self-signed (forced)
        private readonly string _logPath;

        private static readonly JsonSerializerOptions CaseInsensitive = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        public MainForm()
        {
            InitializeComponent();
                        try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Windows.Forms.Application.ExecutablePath); } catch { }
            _http = CreateHttpClient();
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCPForwarder", "pcp-log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        }

        private HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler();
            if (_tlsInsecure)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }
            return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var root = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(root))
            {
                var devRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot"));
                if (Directory.Exists(devRoot))
                    root = devRoot;
                else
                    Directory.CreateDirectory(root);
            }

            var indexPath = Path.Combine(root, "index.html");
            if (!File.Exists(indexPath))
            {
                webView.CoreWebView2.NavigateToString("<h2 style='font-family:sans-serif'>Missing wwwroot/index.html</h2>");
                return;
            }

            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("app", root, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate("https://app/index.html");

            webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
            webView.CoreWebView2.DOMContentLoaded += (_, __) => InjectBridge();

            this.KeyPreview = true;
            this.KeyDown += (_, ev) => { if (ev.KeyCode == Keys.F12) webView.CoreWebView2.OpenDevToolsWindow(); };
        }

        private async void InjectBridge()
        {
            var js = @"
                window.native = {
                  invoke: async function(kind, payload){
                    return new Promise((resolve, reject)=>{
                      const id = Math.random().toString(36).slice(2);
                      const handler = (e) => {
                        try {
                          const msg = JSON.parse(e.data);
                          if (msg && msg.replyTo===id){
                            window.chrome.webview.removeEventListener('message', handler);
                            if (msg.error) reject(new Error(msg.error)); else resolve(msg.data);
                          }
                        } catch {}
                      };
                      window.chrome?.webview?.addEventListener('message', handler);
                      window.chrome?.webview?.postMessage(JSON.stringify({ id, kind, payload }));
                    });
                  }
                };
            ";
            await webView.CoreWebView2.ExecuteScriptAsync(js);
        }

        private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var raw = e.TryGetWebMessageAsString();
            try
            {
                var msg = JsonSerializer.Deserialize<Msg>(raw ?? "{}", CaseInsensitive);
                if (msg == null) return;

                switch (msg.Kind)
                {
                    case "apiFetch":
                        var res = await HandleApiFetch(msg.Payload);
                        await Reply(msg.Id, res);
                        break;

                    case "confirmReboot":
                        var confirm = MessageBox.Show("Send reboot command?", "Confirm Reboot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                        await Reply(msg.Id, new { ok = (confirm == DialogResult.Yes) });
                        break;

                    case "setVerbose":
                        _verbose = msg.Payload.GetProperty("value").GetBoolean();
                        await Reply(msg.Id, new { ok = true, verbose = _verbose });
                        break;

                    case "setTlsInsecure":
                        _tlsInsecure = true; // forced on; ignore UI
                        _http.Dispose();
                        _http = CreateHttpClient();
                        await Reply(msg.Id, new { ok = true, tlsInsecure = _tlsInsecure });
                        break;

                    default:
                        await ReplyError(msg.Id, $"Unknown kind: {msg.Kind}");
                        break;
                }
            }
            catch (Exception ex)
            {
                await ReplyError("", ex.ToString());
            }
        }

        private async Task<object> HandleApiFetch(JsonElement payload)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            string baseUrl = payload.TryGetProperty("baseUrl", out var bu) && bu.ValueKind != JsonValueKind.Null ? bu.GetString() ?? "" : "";
            string path = payload.GetProperty("path").GetString() ?? "/";
            string method = payload.TryGetProperty("method", out var m) && m.ValueKind != JsonValueKind.Null ? m.GetString() ?? "GET" : "GET";
            string? body = payload.TryGetProperty("body", out var b) && b.ValueKind != JsonValueKind.Null ? b.GetString() : null;
            string? user = payload.TryGetProperty("user", out var u) && u.ValueKind != JsonValueKind.Null ? u.GetString() : null;
            string? pass = payload.TryGetProperty("pass", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null;

            // absolute URL support for Teams check
            bool absolute = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            if (!absolute)
            {
                if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    var err1 = new { ok = false, status = 0, reason = "HTTPS_REQUIRED", error = "Base URL must start with https:// (phone requires HTTPS).", request = new { url = baseUrl + path, method, body } };
                    Log(err1);
                    return err1;
                }
            }

            var req = new HttpRequestMessage(new HttpMethod(method), absolute ? new Uri(path) : new Uri(new Uri(baseUrl), path));

// Ensure Content-Type: application/json is sent for POST/PUT even when body is empty
if (req.Method == HttpMethod.Post || req.Method == HttpMethod.Put)
{
    var effectiveBody = string.IsNullOrEmpty(body) ? string.Empty : body;
    var __sc = new StringContent(effectiveBody, Encoding.UTF8);
    __sc.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    req.Content = __sc;
    // Accept header intentionally omitted for POST/PUT to match phone behavior
}
if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
            }

            try
            {
                using var resp = await _http.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                // Retry logic for VVX quirk on /api/v1/callctrl/dial returning HTTP 400 with HTML
                if ((int)resp.StatusCode == 400 && path.Contains("/api/v1/callctrl/dial"))
                {
                    try
                    {
                        // Variant A: retry with lowercase content-type and without Accept header
                        var req2 = new HttpRequestMessage(new HttpMethod(method), absolute ? new Uri(path) : new Uri(new Uri(baseUrl), path));
                        if (req.Method == HttpMethod.Post || req.Method == HttpMethod.Put)
                        {
                            var effectiveBody2 = string.IsNullOrEmpty(body) ? string.Empty : body;
                            var __sc2 = new StringContent(effectiveBody2, Encoding.UTF8);
                            __sc2.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                            req2.Content = __sc2;
                            // intentionally do not set Accept header
                        }
                        if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
                        {
                            var token2 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                            req2.Headers.Authorization = new AuthenticationHeaderValue("Basic", token2);
                        }
                        using var resp2 = await _http.SendAsync(req2);
                        var text2 = await resp2.Content.ReadAsStringAsync();

                        if ((int)resp2.StatusCode == 400 && baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            // Variant B: fallback to HTTP absolute URL
                            var httpBase = "http://" + baseUrl.Substring("https://".Length);
                            var req3 = new HttpRequestMessage(new HttpMethod(method), new Uri(new Uri(httpBase), path));
                            if (req.Method == HttpMethod.Post || req.Method == HttpMethod.Put)
                            {
                                var eff3 = string.IsNullOrEmpty(body) ? string.Empty : body;
                                var __sc3 = new StringContent(eff3, Encoding.UTF8);
                                __sc3.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                                req3.Content = __sc3;
                                // no Accept header
                            }
                            if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(pass))
                            {
                                var token3 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
                                req3.Headers.Authorization = new AuthenticationHeaderValue("Basic", token3);
                            }
                            using var resp3 = await _http.SendAsync(req3);
                            var text3 = await resp3.Content.ReadAsStringAsync();
                            sw.Stop();
                            var resObj3 = new
                            {
                                ok = resp3.IsSuccessStatusCode,
                                status = (int)resp3.StatusCode,
                                reason = resp3.ReasonPhrase,
                                elapsedMs = sw.ElapsedMilliseconds,
                                request = new { url = req3.RequestUri!.ToString(), method, headers = req3.Headers.ToString(), contentHeaders = req3.Content?.Headers.ToString(), body },
                                response = new { headers = resp3.Headers.ToString() + resp3.Content.Headers.ToString(), body = text3 }
                            };
                            Log(resObj3);
                            return resObj3;
                        }
                        else
                        {
                            sw.Stop();
                            var resObj2 = new
                            {
                                ok = resp2.IsSuccessStatusCode,
                                status = (int)resp2.StatusCode,
                                reason = resp2.ReasonPhrase,
                                elapsedMs = sw.ElapsedMilliseconds,
                                request = new { url = req2.RequestUri!.ToString(), method, headers = req2.Headers.ToString(), contentHeaders = req2.Content?.Headers.ToString(), body },
                                response = new { headers = resp2.Headers.ToString() + resp2.Content.Headers.ToString(), body = text2 }
                            };
                            Log(resObj2);
                            return resObj2;
                        }
                    }
                    catch { /* ignore and fall back to original response */ }
                }

                sw.Stop();

                var resObj = new
                {
                    ok = resp.IsSuccessStatusCode,
                    status = (int)resp.StatusCode,
                    reason = resp.ReasonPhrase,
                    elapsedMs = sw.ElapsedMilliseconds,
                    request = new { url = req.RequestUri!.ToString(), method, headers = req.Headers.ToString(), contentHeaders = req.Content?.Headers.ToString(), body },
                    response = new { headers = resp.Headers.ToString() + resp.Content.Headers.ToString(), body = text }
                };
                Log(resObj);
                return resObj;
            }
            catch (Exception ex)
            {
                sw.Stop();
                var errObj = new { ok = false, status = 0, reason = "EXCEPTION", elapsedMs = sw.ElapsedMilliseconds, error = ex.ToString(), request = new { url = absolute ? path : (baseUrl + path), method, body } };
                Log(errObj);
                return errObj;
            }
        }

        private void Log(object obj)
        {
            try
            {
                var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + JsonSerializer.Serialize(obj) + Environment.NewLine;
                File.AppendAllText(_logPath, line);
                if (_verbose)
                {
                    webView.CoreWebView2.PostWebMessageAsString(JsonSerializer.Serialize(new { replyTo = "__log__", data = obj }));
                }
            }
            catch { }
        }

        private Task Reply(string id, object data)
        {
            var payload = JsonSerializer.Serialize(new { replyTo = id, data });
            webView.CoreWebView2.PostWebMessageAsString(payload);
            return Task.CompletedTask;
        }
        private Task ReplyError(string id, string error)
        {
            var payload = JsonSerializer.Serialize(new { replyTo = id, error });
            webView.CoreWebView2.PostWebMessageAsString(payload);
            return Task.CompletedTask;
        }

        private class Msg
        {
            public string Id { get; set; } = string.Empty;
            public string Kind { get; set; } = string.Empty;
            public JsonElement Payload { get; set; }
        }
    }
}
