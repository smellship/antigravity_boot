using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace AntigravityBootOverlay
{
    public class ControlServer
    {
        readonly TrayApp app;
        readonly Dispatcher disp;
        readonly bool managed;
        readonly DateTime startedAt = DateTime.UtcNow;

        HttpListener listener;
        Thread worker;
        volatile bool stopping;
        public int Port;
        public string Token;

        public ControlServer(TrayApp app, Dispatcher disp, bool managed)
        {
            this.app = app;
            this.disp = disp;
            this.managed = managed;
            this.Token = GenerateToken();
        }

        static string GenerateToken()
        {
            byte[] buf = new byte[16];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(buf);
            var sb = new StringBuilder(32);
            for (int i = 0; i < buf.Length; i++) sb.Append(buf[i].ToString("x2"));
            return sb.ToString();
        }

        static int FindFreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public void Start()
        {
            Exception lastErr = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int p = FindFreePort();
                var hl = new HttpListener();
                hl.Prefixes.Add("http://127.0.0.1:" + p + "/");
                try
                {
                    hl.Start();
                    listener = hl;
                    Port = p;
                    WriteRuntimeFile();
                    worker = new Thread(AcceptLoop) { IsBackground = true };
                    worker.Start();
                    Log.W("本地控制服务已启动：http://127.0.0.1:" + Port + "/");
                    return;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    try { hl.Close(); } catch { }
                }
            }
            throw new InvalidOperationException("无法启动本地环回控制端口：" + (lastErr != null ? lastErr.Message : "unknown"));
        }

        void WriteRuntimeFile()
        {
            var d = new Dictionary<string, object>();
            d["pid"] = Process.GetCurrentProcess().Id;
            d["port"] = Port;
            d["token"] = Token;
            d["managed"] = managed;
            d["startedAt"] = startedAt.ToString("o", CultureInfo.InvariantCulture);
            var js = new JavaScriptSerializer();
            ConfigStore.AtomicWriteText(AppPaths.RuntimePath, ConfigStore.PrettyJson(js.Serialize(d)));
        }

        public void Stop()
        {
            stopping = true;
            try { if (listener != null) listener.Stop(); } catch { }
            try { if (listener != null) listener.Close(); } catch { }
            try
            {
                if (File.Exists(AppPaths.RuntimePath))
                {
                    var txt = File.ReadAllText(AppPaths.RuntimePath, Encoding.UTF8);
                    var d = new JavaScriptSerializer().DeserializeObject(txt) as Dictionary<string, object>;
                    if (d != null && d.ContainsKey("pid") && Convert.ToInt32(d["pid"]) == Process.GetCurrentProcess().Id)
                        File.Delete(AppPaths.RuntimePath);
                }
            }
            catch { }
        }

        void AcceptLoop()
        {
            while (!stopping)
            {
                HttpListenerContext ctx = null;
                try { ctx = listener.GetContext(); }
                catch { if (stopping) break; continue; }
                ThreadPool.QueueUserWorkItem(delegate(object state)
                {
                    HandleContext((HttpListenerContext)state);
                }, ctx);
            }
        }

        void HandleContext(HttpListenerContext ctx)
        {
            int status = 200;
            object payload = null;
            try
            {
                string path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                if (string.IsNullOrEmpty(path)) path = "/";
                string method = ctx.Request.HttpMethod.ToUpperInvariant();

                if (path == "/health" && method == "GET")
                {
                    var h = new Dictionary<string, object>();
                    h["ok"] = true;
                    h["pid"] = Process.GetCurrentProcess().Id;
                    h["version"] = "1.0.0";
                    h["uptimeSec"] = Math.Round((DateTime.UtcNow - startedAt).TotalSeconds, 1);
                    payload = h;
                }
                else
                {
                    if (!Authorized(ctx.Request))
                    {
                        status = 401;
                        var err = new Dictionary<string, object>();
                        err["ok"] = false;
                        err["error"] = "unauthorized";
                        payload = err;
                    }
                    else
                    {
                        Dictionary<string, object> body = ReadBody(ctx.Request);
                        payload = DispatchRoute(method, path, body, out status);
                    }
                }
            }
            catch (Exception ex)
            {
                status = 500;
                var err = new Dictionary<string, object>();
                err["ok"] = false;
                err["error"] = ex.Message;
                payload = err;
            }

            try
            {
                var js = new JavaScriptSerializer();
                byte[] bytes = Encoding.UTF8.GetBytes(js.Serialize(payload));
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.OutputStream.Close();
            }
            catch { }
        }

        bool Authorized(HttpListenerRequest req)
        {
            string auth = req.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(auth.Substring(7).Trim(), Token, StringComparison.Ordinal)) return true;
            }
            string alt = req.Headers["X-Boot-Token"];
            if (!string.IsNullOrEmpty(alt) && string.Equals(alt.Trim(), Token, StringComparison.Ordinal)) return true;
            return false;
        }

        static Dictionary<string, object> ReadBody(HttpListenerRequest req)
        {
            if (!req.HasEntityBody) return new Dictionary<string, object>();
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
            {
                string raw = reader.ReadToEnd();
                if (string.IsNullOrEmpty(raw)) return new Dictionary<string, object>();
                var d = new JavaScriptSerializer().DeserializeObject(raw) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
        }

        T OnUi<T>(Func<T> fn)
        {
            if (disp.CheckAccess()) return fn();
            return disp.Invoke(fn);
        }

        object DispatchRoute(string method, string path, Dictionary<string, object> body, out int status)
        {
            status = 200;
            if (path == "/status" && method == "GET")
            {
                return OnUi(delegate { return app.GetStatusMap(); });
            }
            if (path == "/windows" && method == "GET")
            {
                return OnUi(delegate
                {
                    var m = new Dictionary<string, object>();
                    m["ok"] = true;
                    m["windows"] = app.ListWindowsMaps();
                    return m;
                });
            }
            if (path == "/play" && method == "POST")
            {
                string mode = body.ContainsKey("mode") ? Convert.ToString(body["mode"]) : "intro";
                string themeId = body.ContainsKey("themeId") ? Convert.ToString(body["themeId"]) : null;
                long hwnd = 0;
                if (body.ContainsKey("hwnd") && body["hwnd"] != null)
                {
                    try { hwnd = Convert.ToInt64(body["hwnd"], CultureInfo.InvariantCulture); } catch { }
                }
                return OnUi(delegate { return app.HandlePlay(mode != "loop", themeId, hwnd); });
            }
            if (path == "/skip" && method == "POST")
            {
                return OnUi(delegate
                {
                    app.SkipCurrent();
                    var m = new Dictionary<string, object>();
                    m["ok"] = true;
                    return m;
                });
            }
            if (path == "/stop" && method == "POST")
            {
                bool fast = body.ContainsKey("fast") && body["fast"] is bool ? (bool)body["fast"] : true;
                return OnUi(delegate
                {
                    app.StopCurrent(fast);
                    var m = new Dictionary<string, object>();
                    m["ok"] = true;
                    return m;
                });
            }
            if (path == "/apply-live" && method == "POST")
            {
                return OnUi(delegate { return app.ApplyLivePatch(body); });
            }
            if (path == "/reload" && method == "POST")
            {
                return OnUi(delegate
                {
                    app.ReloadAllFromDisk();
                    var m = new Dictionary<string, object>();
                    m["ok"] = true;
                    return m;
                });
            }
            if (path == "/shutdown" && method == "POST")
            {
                disp.BeginInvoke(new Action(delegate { app.ShutdownApp(); }));
                var m = new Dictionary<string, object>();
                m["ok"] = true;
                return m;
            }
            status = 404;
            var nf = new Dictionary<string, object>();
            nf["ok"] = false;
            nf["error"] = "not found";
            return nf;
        }
    }
}
