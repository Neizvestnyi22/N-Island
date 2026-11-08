using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using NIsland.Core;

namespace NIsland.Services
{
    /// <summary>Самопроверка MCP-сервера: NIsland.exe --selftest mcp &lt;выход&gt; — настоящие HTTP-запросы на тестовый порт.</summary>
    static class McpTest
    {
        public static int Run(string output)
        {
            var sb = new StringBuilder();
            var task = Task.Run(() => Body(sb));
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            if (task.Exception != null) sb.AppendLine("FATAL " + task.Exception);
            File.WriteAllText(output, sb.ToString());
            return 0;
        }

        static async Task Body(StringBuilder sb)
        {
            var s = Store.Settings;
            s.McpOn = true; s.McpPort = 7399; s.McpLevel = 1;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(McpServer.Apply);
            sb.AppendLine("running=" + McpServer.Running + " err=" + McpServer.Error);
            using var http = new HttpClient();
            async Task<string> Post(string json, string token = null, string origin = null)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:7399/mcp") { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + (token ?? McpServer.Token));
                if (origin != null) req.Headers.TryAddWithoutValidation("Origin", origin);
                using var r = await http.SendAsync(req);
                var body = await r.Content.ReadAsStringAsync();
                return $"{(int)r.StatusCode} {(body.Length > 700 ? body[..700] + "…" : body)}";
            }
            int id = 0;
            string Call(string tool, string args = "{}") => $"{{\"jsonrpc\":\"2.0\",\"id\":{++id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\",\"arguments\":{args}}}}}";
            sb.AppendLine("initialize: " + await Post("{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}"));
            sb.AppendLine("initialized: " + await Post("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"));
            var list = await Post("{\"jsonrpc\":\"2.0\",\"id\":99,\"method\":\"tools/list\"}");
            sb.AppendLine("tools/list: " + list.Length + " chars, has island_notify=" + list.Contains("island_notify") + ", has settings_set=" + list.Contains("settings_set"));
            sb.AppendLine("status: " + await Post(Call("island_status")));
            sb.AppendLine("now playing: " + await Post(Call("media_now_playing")));
            sb.AppendLine("settings_list: " + await Post(Call("settings_list", "{\"filter\":\"Mascot\"}")));
            sb.AppendLine("settings_set (level 1): " + await Post(Call("settings_set", "{\"name\":\"VizStyle\",\"value\":\"galaxy\"}")));
            s.McpLevel = 2;
            sb.AppendLine("settings_set (level 2): " + await Post(Call("settings_set", "{\"name\":\"VizStyle\",\"value\":\"galaxy\"}")));
            sb.AppendLine("settings_set forbidden: " + await Post(Call("settings_set", "{\"name\":\"AiKeys\",\"value\":\"x\"}")));
            sb.AppendLine("unknown tool: " + await Post(Call("nope")));
            sb.AppendLine("batch: " + await Post("[{\"jsonrpc\":\"2.0\",\"id\":201,\"method\":\"ping\"},{\"jsonrpc\":\"2.0\",\"method\":\"notifications/x\"}]"));
            sb.AppendLine("bad token: " + await Post(Call("island_status"), token: "wrong"));
            sb.AppendLine("evil origin: " + await Post(Call("island_status"), origin: "https://evil.example"));
            using (var g = await http.GetAsync("http://127.0.0.1:7399/mcp")) sb.AppendLine("GET (no auth): " + (int)g.StatusCode);
            using (var g = await http.GetAsync("http://127.0.0.1:7399/health")) sb.AppendLine("health: " + (int)g.StatusCode);
            sb.AppendLine("screenshot: " + (await Post(Call("screenshot", "{\"window\":true}"))).Substring(0, 120));
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(McpServer.Stop);
        }
    }
}
