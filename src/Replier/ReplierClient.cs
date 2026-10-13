using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Replier
{
    public class ReplierOptions
    {
        public string? InstanceId { get; set; }
        public string? Token { get; set; }
        /// <summary>Needed only for ListInstances / NewInstance / RenewInstance.</summary>
        public string? Email { get; set; }
        /// <summary>Needed only for ListInstances / NewInstance / RenewInstance.</summary>
        public string? ApiKey { get; set; }
        public string BaseUrl { get; set; } = ReplierClient.DefaultBaseUrl;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(70);
    }

    /// <summary>
    /// Replier.net WhatsApp API client.
    /// <code>
    /// var replier = new ReplierClient(new ReplierOptions { InstanceId = "12345", Token = "your-token" });
    /// await replier.SendMessageAsync("15551234567", "Hello!");
    /// </code>
    /// </summary>
    public class ReplierClient : IDisposable
    {
        public const string Version = "1.0.0";
        public const string DefaultBaseUrl = "https://api.replier.net";

        private readonly ReplierOptions _options;
        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly string _baseUrl;

        /// <param name="httpClient">Optional; pass one from IHttpClientFactory in ASP.NET Core apps.</param>
        public ReplierClient(ReplierOptions options, HttpClient? httpClient = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _baseUrl = options.BaseUrl.TrimEnd('/');
            if (httpClient == null)
            {
                _http = new HttpClient { Timeout = options.Timeout };
                _ownsHttp = true;
            }
            else
            {
                _http = httpClient;
            }
        }

        public void Dispose()
        {
            if (_ownsHttp) _http.Dispose();
        }

        // ------------------------------------------------------------------ core

        /// <summary>Call any instance endpoint; instanceid and token are added automatically.</summary>
        public Task<JsonElement> CallAsync(string method, IDictionary<string, object?>? parameters = null,
            CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?>();
            if (_options.InstanceId != null) body["instanceid"] = _options.InstanceId;
            if (_options.Token != null) body["token"] = _options.Token;
            return RequestAsync(method, Merge(body, parameters), ct);
        }

        /// <summary>Call an account endpoint; email and apikey are added automatically.</summary>
        public Task<JsonElement> CallAccountAsync(string method, IDictionary<string, object?>? parameters = null,
            CancellationToken ct = default)
        {
            var body = new Dictionary<string, object?>();
            if (_options.Email != null) body["email"] = _options.Email;
            if (_options.ApiKey != null) body["apikey"] = _options.ApiKey;
            return RequestAsync(method, Merge(body, parameters), ct);
        }

        private static Dictionary<string, object?> Merge(Dictionary<string, object?> auth, IDictionary<string, object?>? p)
        {
            if (p != null) foreach (var kv in p) auth[kv.Key] = kv.Value;
            return auth;
        }

        private async Task<JsonElement> RequestAsync(string method, Dictionary<string, object?> parameters, CancellationToken ct)
        {
            var body = parameters.Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => kv.Value);
            using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/" + method)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Headers.TryAddWithoutValidation("User-Agent", "replier-dotnet/" + Version);

            HttpResponseMessage res;
            try
            {
                res = await _http.SendAsync(req, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw new ReplierException("Request failed: " + e.Message, inner: e);
            }
            catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
            {
                throw new ReplierException("Request timed out", inner: e);
            }

            using (res)
            {
                var status = (int)res.StatusCode;
                var raw = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                JsonElement data;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    data = doc.RootElement.Clone();
                }
                catch (JsonException e)
                {
                    throw new ReplierException($"Invalid JSON response (HTTP {status})", httpStatus: status, inner: e);
                }
                if (data.ValueKind != JsonValueKind.Object)
                    throw new ReplierException($"Invalid JSON response (HTTP {status})", httpStatus: status);

                var failed = data.TryGetProperty("success", out var s) &&
                             (s.ValueKind == JsonValueKind.False ||
                              (s.ValueKind == JsonValueKind.String && s.GetString() == "false"));
                if (status >= 400 || failed)
                {
                    string? code = data.TryGetProperty("errorCode", out var c) ? c.ToString() : null;
                    throw new ReplierException(Reason(data, status >= 400 ? $"HTTP {status}" : "Request failed"),
                        code, data, status);
                }
                return data;
            }
        }

        private static string Reason(JsonElement data, string fallback)
        {
            if (data.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String && r.GetString() != "")
                return r.GetString()!;
            if (data.TryGetProperty("msg", out var m))
            {
                if (m.ValueKind == JsonValueKind.Array)
                    return string.Join(", ", m.EnumerateArray().Select(x => x.ToString()));
                if (m.ValueKind == JsonValueKind.String) return m.GetString()!;
            }
            return fallback;
        }

        private static Dictionary<string, object?> P(params (string Key, object? Value)[] kv) =>
            kv.ToDictionary(x => x.Key, x => x.Value);

        private static string Phones(IEnumerable<string> phones) => string.Join(",", phones);

        // ------------------------------------------------------------------ instance

        public Task<JsonElement> StatusAsync(CancellationToken ct = default) => CallAsync("status", null, ct);
        public Task<JsonElement> QrCodeAsync(CancellationToken ct = default) => CallAsync("qr_code", null, ct);
        public Task<JsonElement> PairCodeAsync(string phone, CancellationToken ct = default) => CallAsync("pair_code", P(("phone", phone)), ct);
        public Task<JsonElement> LogoutAsync(CancellationToken ct = default) => CallAsync("logout", null, ct);
        public Task<JsonElement> TakeoverAsync(CancellationToken ct = default) => CallAsync("takeover", null, ct);
        public Task<JsonElement> ExpiryAsync(CancellationToken ct = default) => CallAsync("expiry", null, ct);
        public Task<JsonElement> RetryAsync(CancellationToken ct = default) => CallAsync("retry", null, ct);
        public Task<JsonElement> RebootAsync(CancellationToken ct = default) => CallAsync("reboot", null, ct);
        public Task<JsonElement> GetSettingsAsync(CancellationToken ct = default) => CallAsync("getSettings", null, ct);
        public Task<JsonElement> SetSettingsAsync(IDictionary<string, object?> settings, CancellationToken ct = default) => CallAsync("settings", settings, ct);
        public Task<JsonElement> MeAsync(CancellationToken ct = default) => CallAsync("me", null, ct);
        public Task<JsonElement> VersionAsync(CancellationToken ct = default) => CallAsync("version", null, ct);
        public Task<JsonElement> ScreenshotAsync(CancellationToken ct = default) => CallAsync("screenshot", null, ct);

        // ------------------------------------------------------------------ messages

        public Task<JsonElement> SendMessageAsync(string phone, string body, object? options = null, CancellationToken ct = default) =>
            CallAsync("sendMessage", P(("phone", phone), ("body", body), ("options", options)), ct);

        /// <param name="body">File URL or base64 data URI.</param>
        public Task<JsonElement> SendFileAsync(string phone, string body, string filename, string? caption = null, CancellationToken ct = default) =>
            CallAsync("sendFile", P(("phone", phone), ("body", body), ("filename", filename), ("caption", caption)), ct);

        public Task<JsonElement> SendPTTAsync(string phone, string audio, CancellationToken ct = default) =>
            CallAsync("sendPTT", P(("phone", phone), ("audio", audio)), ct);

        public Task<JsonElement> SendLinkAsync(string phone, string body, CancellationToken ct = default) =>
            CallAsync("sendLink", P(("phone", phone), ("body", body)), ct);

        public Task<JsonElement> SendContactAsync(string phone, string contactId, CancellationToken ct = default) =>
            CallAsync("sendContact", P(("phone", phone), ("contactId", contactId)), ct);

        public Task<JsonElement> SendLocationAsync(string phone, double lat, double lng, string? address = null, CancellationToken ct = default) =>
            CallAsync("sendLocation", P(("phone", phone), ("lat", lat), ("lng", lng), ("address", address)), ct);

        public Task<JsonElement> SendVCardAsync(string phone, string vcard, CancellationToken ct = default) =>
            CallAsync("sendVCard", P(("phone", phone), ("vcard", vcard)), ct);

        public Task<JsonElement> SendButtonAsync(string phone, string body, object buttons, string? header = null, string? footer = null, CancellationToken ct = default) =>
            CallAsync("sendButton", P(("phone", phone), ("body", body), ("buttons", buttons), ("header", header), ("footer", footer)), ct);

        public Task<JsonElement> SendListAsync(string phone, string body, string title, object list, string? header = null, string? footer = null, CancellationToken ct = default) =>
            CallAsync("sendList", P(("phone", phone), ("body", body), ("title", title), ("list", list), ("header", header), ("footer", footer)), ct);

        public Task<JsonElement> ForwardMessageAsync(string phone, string messageId, CancellationToken ct = default) =>
            CallAsync("forwardMessage", P(("phone", phone), ("messageId", messageId)), ct);

        public Task<JsonElement> DeleteMessageAsync(string phone, string messageId, CancellationToken ct = default) =>
            CallAsync("deleteMessage", P(("phone", phone), ("messageId", messageId)), ct);

        public Task<JsonElement> SendSeenAsync(string phone, CancellationToken ct = default) =>
            CallAsync("sendSeen", P(("phone", phone)), ct);

        public Task<JsonElement> MessagesAsync(string phone, int? limit = null, long? lastMessageNumber = null, CancellationToken ct = default) =>
            CallAsync("messages", P(("phone", phone), ("limit", limit), ("lastMessageNumber", lastMessageNumber)), ct);

        public Task<JsonElement> ShowMessagesQueueAsync(CancellationToken ct = default) => CallAsync("showMessagesQueue", null, ct);
        public Task<JsonElement> ClearMessagesQueueAsync(CancellationToken ct = default) => CallAsync("clearMessagesQueue", null, ct);
        public Task<JsonElement> ShowActionsQueueAsync(CancellationToken ct = default) => CallAsync("showActionsQueue", null, ct);
        public Task<JsonElement> ClearActionsQueueAsync(CancellationToken ct = default) => CallAsync("clearActionsQueue", null, ct);

        // ------------------------------------------------------------------ chats

        public Task<JsonElement> DialogsAsync(CancellationToken ct = default) => CallAsync("dialogs", null, ct);
        public Task<JsonElement> ChatsAsync(CancellationToken ct = default) => CallAsync("chats", null, ct);
        public Task<JsonElement> ContactsAsync(CancellationToken ct = default) => CallAsync("contacts", null, ct);

        // ------------------------------------------------------------------ groups

        public Task<JsonElement> CreateGroupAsync(string groupName, IEnumerable<string> phones, CancellationToken ct = default) =>
            CallAsync("group", P(("groupName", groupName), ("phones", Phones(phones))), ct);

        public Task<JsonElement> LeaveGroupAsync(string groupId, CancellationToken ct = default) =>
            CallAsync("leaveGroup", P(("groupId", groupId)), ct);

        public Task<JsonElement> AddGroupParticipantAsync(string groupId, IEnumerable<string> phones, CancellationToken ct = default) =>
            CallAsync("addGroupParticipant", P(("groupId", groupId), ("phones", Phones(phones))), ct);

        public Task<JsonElement> RemoveGroupParticipantAsync(string groupId, IEnumerable<string> phones, CancellationToken ct = default) =>
            CallAsync("removeGroupParticipant", P(("groupId", groupId), ("phones", Phones(phones))), ct);

        public Task<JsonElement> PromoteGroupParticipantAsync(string groupId, IEnumerable<string> phones, CancellationToken ct = default) =>
            CallAsync("promoteGroupParticipant", P(("groupId", groupId), ("phones", Phones(phones))), ct);

        public Task<JsonElement> DemoteGroupParticipantAsync(string groupId, IEnumerable<string> phones, CancellationToken ct = default) =>
            CallAsync("demoteGroupParticipant", P(("groupId", groupId), ("phones", Phones(phones))), ct);

        // ------------------------------------------------------------------ channels

        public Task<JsonElement> CreateChannelAsync(string name, string? description = null, string? picture = null, CancellationToken ct = default) =>
            CallAsync("channel", P(("name", name), ("description", description), ("picture", picture)), ct);

        public Task<JsonElement> FollowChannelAsync(string channelId, CancellationToken ct = default) =>
            CallAsync("channelFollow", P(("channelId", channelId)), ct);

        public Task<JsonElement> UnfollowChannelAsync(string channelId, CancellationToken ct = default) =>
            CallAsync("channelUnfollow", P(("channelId", channelId)), ct);

        public Task<JsonElement> ChannelInfoAsync(string channelId, CancellationToken ct = default) =>
            CallAsync("channelInfo", P(("channelId", channelId)), ct);

        // ------------------------------------------------------------------ webhook / bot / ban

        public Task<JsonElement> SetWebhookAsync(string webhookUrl, CancellationToken ct = default) =>
            CallAsync("webhook", P(("webhookUrl", webhookUrl)), ct);

        public Task<JsonElement> GetWebhookStatusAsync(CancellationToken ct = default) => CallAsync("getWebhookStatus", null, ct);
        public Task<JsonElement> GetBanSettingsAsync(CancellationToken ct = default) => CallAsync("getBanSettings", null, ct);
        public Task<JsonElement> BotSettingsAsync(CancellationToken ct = default) => CallAsync("botSettings", null, ct);
        public Task<JsonElement> GetBotSettingsAsync(CancellationToken ct = default) => CallAsync("getbotSettings", null, ct);

        // ------------------------------------------------------------------ account

        public Task<JsonElement> ListInstancesAsync(CancellationToken ct = default) => CallAccountAsync("listInstances", null, ct);

        public Task<JsonElement> NewInstanceAsync(int packageId, string? voucher = null, CancellationToken ct = default) =>
            CallAccountAsync("newInstance", P(("packageid", packageId), ("voucher", voucher)), ct);

        public Task<JsonElement> RenewInstanceAsync(string instanceId, int packageId, string? voucher = null, CancellationToken ct = default) =>
            CallAccountAsync("renewInstance", P(("instanceid", instanceId), ("packageid", packageId), ("voucher", voucher)), ct);
    }
}
