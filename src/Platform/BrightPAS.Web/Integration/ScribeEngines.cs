using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using BrightPAS.Modules.Scribe;

namespace BrightPAS.Web.Integration;

/// <summary>
/// Speech and drafting engines for BrightPAS Scribe, over plain HTTP so TDS can choose:
/// speech = any OpenAI-compatible transcription server (on site: Whisper/faster-whisper/whisper.cpp server; or a cloud service);
/// drafting = an on-site language model with an OpenAI-compatible chat API (e.g. vLLM, Ollama) or the Claude API (de-identified transcript only).
/// </summary>
public sealed class ScribeEngines(IHttpClientFactory http)
{
    public const string DefaultClaudeModel = "claude-sonnet-4-5";

    private static string Endpoint(string url, string path) => url.TrimEnd('/').EndsWith(path, StringComparison.OrdinalIgnoreCase) ? url : url.TrimEnd('/') + path;

    public async Task<(List<Segment> Segments, string Engine)> TranscribeAsync(Engine e, byte[] audio, string type, IEnumerable<string> terms, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue(type.Split(';')[0]);
        form.Add(file, "file", type.Contains("mp4") ? "audio.mp4" : type.Contains("ogg") ? "audio.ogg" : type.Contains("wav") ? "audio.wav" : "audio.webm");
        form.Add(new StringContent(e.Model ?? "whisper-1"), "model");
        form.Add(new StringContent("verbose_json"), "response_format");
        form.Add(new StringContent("en"), "language");
        var prompt = string.Join(", ", terms.Take(80));
        if (prompt.Length > 0) form.Add(new StringContent(prompt.Length > 800 ? prompt[..800] : prompt), "prompt");
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint(e.Url, "/v1/audio/transcriptions")) { Content = form };
        if (e.Key is { Length: > 0 } k) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", k);
        using var res = await http.CreateClient("ai").SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The speech engine answered HTTP {(int)res.StatusCode}.");
        var root = JsonNode.Parse(text);
        var list = new List<Segment>();
        var n = 0;
        foreach (var s in root?["segments"] as JsonArray ?? [])
        {
            var t = s?["text"]?.GetValue<string>()?.Trim();
            if (string.IsNullOrEmpty(t)) continue;
            decimal Num(string name) => s?[name] is JsonValue v && v.TryGetValue<double>(out var d) ? (decimal)Math.Round(d, 2) : 0m;
            decimal? conf = s?["avg_logprob"] is JsonValue lp && lp.TryGetValue<double>(out var l) ? (decimal)Math.Round(Math.Clamp(Math.Exp(l), 0, 1), 3) : null;
            list.Add(new Segment(++n, s?["speaker"]?.GetValue<string>() ?? "Unknown", Num("start"), Num("end"), t, conf));
        }
        if (list.Count == 0 && root?["text"]?.GetValue<string>() is { Length: > 0 } whole) list.Add(new Segment(1, "Unknown", 0, 0, whole.Trim(), null));
        return (list, $"{new Uri(e.Url).Host} {e.Model ?? "whisper-1"}");
    }

    public async Task<(string Answer, string Engine)> DraftAsync(DraftMode mode, Engine e, string prompt, CancellationToken ct = default)
    {
        HttpRequestMessage req;
        if (mode == DraftMode.CloudClaude)
        {
            var body = new JsonObject { ["model"] = e.Model ?? DefaultClaudeModel, ["max_tokens"] = 4000, ["temperature"] = 0, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }) };
            req = new HttpRequestMessage(HttpMethod.Post, Endpoint(e.Url, "/v1/messages")) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            req.Headers.TryAddWithoutValidation("x-api-key", e.Key);
            req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }
        else
        {
            var body = new JsonObject { ["model"] = e.Model ?? "local", ["temperature"] = 0, ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = prompt }) };
            req = new HttpRequestMessage(HttpMethod.Post, Endpoint(e.Url, "/v1/chat/completions")) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
            if (e.Key is { Length: > 0 } k) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", k);
        }
        using (req)
        {
            using var res = await http.CreateClient("ai").SendAsync(req, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"The drafting engine answered HTTP {(int)res.StatusCode}.");
            var root = JsonNode.Parse(text);
            var answer = mode == DraftMode.CloudClaude
                ? string.Concat((root?["content"] as JsonArray ?? []).Select(c => c?["text"]?.GetValue<string>() ?? ""))
                : root?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
            return (answer, $"{(mode == DraftMode.CloudClaude ? "Claude" : new Uri(e.Url).Host)} {e.Model ?? (mode == DraftMode.CloudClaude ? DefaultClaudeModel : "local")}");
        }
    }
}
