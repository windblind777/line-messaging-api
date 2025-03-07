using Microsoft.Extensions.Options;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace LINGBOxGAS.Model
{
    /// <summary>
    /// LINE Messaging API 管理
    /// </summary>
    public class LineMessagingManager
    {
        private readonly LineMessagingOptions _lineMessagingOptions;
        private readonly LineLoginOptions _lineLoginOptions;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>
        /// LINE Messaging API URL
        /// </summary>
        private const string ReplyMessageUrl = "https://api.line.me/v2/bot/message/reply";
        private const string PushMessageUrl = "https://api.line.me/v2/bot/message/push";
        private const string UserProfileUrl = "https://api.line.me/v2/bot/profile";
        private const string TokenUrl = "https://api.line.me/oauth2/v2.1/token";
        private const string VerifyUrl = "https://api.line.me/oauth2/v2.1/verify";

        public LineMessagingManager(IOptions<LineMessagingOptions> messagingOptions, IOptions<LineLoginOptions> loginOptions)
        {
            _lineMessagingOptions = messagingOptions.Value;
            _lineLoginOptions = loginOptions.Value;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _lineMessagingOptions.ChannelAccessToken);

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        public GetLineMessagingAPIBotBasicId()
        {
            return _lineMessagingOptions.BotBasicId;
        }

        /// <summary>
        /// 回覆訊息
        /// </summary>
        public async Task ReplyMessage(string replyToken, string message)
        {
            var payload = new
            {
                replyToken,
                messages = new[]
                {
                    new { type = "text", text = message }
                }
            };

            await SendMessage(ReplyMessageUrl, payload);
        }

        /// <summary>
        /// 推播訊息給特定用戶
        /// </summary>
        public async Task PushMessage(string userId, string message)
        {
            var payload = new
            {
                to = userId,
                messages = new[]
                {
                    new { type = "text", text = message }
                }
            };

            await SendMessage(PushMessageUrl, payload);
        }

        /// <summary>
        /// 發送訊息至 LINE API
        /// </summary>
        private async Task SendMessage(string url, object payload)
        {
            var jsonContent = JsonSerializer.Serialize(payload, _jsonOptions);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var result = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"🔴 LINE API Response: {result}");
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// 取得用戶資訊
        /// </summary>
        public async Task<LineUserProfile> GetUserProfile(string userId)
        {
            var url = $"{UserProfileUrl}/{userId}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<LineUserProfile>(json, _jsonOptions);
            }

            return null;
        }

        /// <summary>
        /// 取得 LINE 登入 ID Token
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public async Task<string> GetIdTokenFromLine(string code)
        {
            using var client = new HttpClient();

            var values = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", code },  // 從 LINE Login 取得的 code
                { "redirect_uri", _lineLoginOptions.CallbackUrl },  // 確保與 LINE Developer Console 設定相同
                { "client_id", _lineLoginOptions.ChannelId },
                { "client_secret", _lineLoginOptions.ChannelSecret }
            };

            Console.WriteLine($"🔴 _lineLoginOptions: {_lineLoginOptions.ToString()}");
            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync(TokenUrl, content);
            var result = await response.Content.ReadAsStringAsync();
            //Console.WriteLine($"🔴 Token Response: {result}");
            if (!response.IsSuccessStatusCode) return null;

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(responseBody);
            //Console.WriteLine($"🔴 ID Token Response: {json.GetProperty("id_token").GetString()}");
            return json.GetProperty("id_token").GetString();
        }

        /// <summary>
        /// 取得 LINE 使用者 ID
        /// </summary>
        /// <param name="id_token"></param>
        public async Task<string> GetUserIdFromLine(string id_token)
        {
            using var client = new HttpClient();

            var values = new Dictionary<string, string>
            {
                { "id_token", id_token },
                { "client_id", _lineLoginOptions.ChannelId }
            };

            var content = new FormUrlEncodedContent(values);
            var response = await client.PostAsync(VerifyUrl, content);
            var result = await response.Content.ReadAsStringAsync();
            //Console.WriteLine($"🔴 Token Response: {result}");
            if (!response.IsSuccessStatusCode) return null;

            var responseBody = await response.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(responseBody);
            //Console.WriteLine($"🔴 USER ID Response: {json.GetProperty("sub").GetString()}");
            return json.GetProperty("sub").GetString();
        }

        /// <summary>
        /// 處理 LINE Webhook 訊息
        /// </summary>
        public async Task HandleWebhook(LineWebhookEvent webhookEvent)
        {
            foreach (var ev in webhookEvent.Events)
            {
                switch (ev.Source.Type)
                {
                    case "user":
                        await HandleWebhookTypeEventAboutUser(ev);
                        break;
                    case "group":
                        await HandleWebhookTypeEventAboutGroup(ev);
                        break;
                    case "room":
                        await HandleWebhookTypeEventAboutRoom(ev);
                        break;
                    default:
                        break;
                }                
            }
        }

        private async Task HandleWebhookTypeEventAboutUser(LineEvent ev)
        {
            switch (ev.Type)
            {
                case "message":
                    await HandleMessageEvent(ev);
                    break;
                case "follow":
                    await HandleFollowEvent(ev);
                    break;
                case "unfollow":
                    await HandleUnfollowEvent(ev);
                    break;
                case "postback":
                    await HandlePostbackEvent(ev);
                    break;
                case "beacon":
                    await HandleBeaconEvent(ev);
                    break;
                case "accountLink":
                    await HandleAccountLinkEvent(ev);
                    break;
                default:
                    break;
            }
        }

        private async Task HandleWebhookTypeEventAboutGroup(LineEvent ev)
        {
            switch (ev.Type)
            {
                case "message":
                    await HandleMessageEvent(ev);
                    break;
                case "join":
                    await HandleJoinEvent(ev);
                    break;
                case "leave":
                    await HandleLeaveEvent(ev);
                    break;
                case "postback":
                    await HandlePostbackEvent(ev);
                    break;
                case "beacon":
                    await HandleBeaconEvent(ev);
                    break;
                case "memberJoined":
                    await HandleMemberJoinedEvent(ev);
                    break;
                case "memberLeft":
                    await HandleMemberLeftEvent(ev);
                    break;
                default:
                    break;
            }
        }

        private async Task HandleWebhookTypeEventAboutRoom(LineEvent ev)
        {
            switch (ev.Type)
            {
                case "message":
                    await HandleMessageEvent(ev);
                    break;
                case "join":
                    await HandleJoinEvent(ev);
                    break;
                case "leave":
                    await HandleLeaveEvent(ev);
                    break;
                case "postback":
                    await HandlePostbackEvent(ev);
                    break;
                case "beacon":
                    await HandleBeaconEvent(ev);
                    break;
                case "memberJoined":
                    await HandleMemberJoinedEvent(ev);
                    break;
                case "memberLeft":
                    await HandleMemberLeftEvent(ev);
                    break;
                default:
                    break;
            }
        }

        private async Task HandleMessageEvent(LineEvent ev)
        {
            var message = ev.Message.Text;
            var userId = ev.Source.UserId;
            var profile = await GetUserProfile(userId);
            var replyMessage = $"Hello, {profile.DisplayName}! You said: {message}";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleFollowEvent(LineEvent ev)
        {
            var userId = ev.Source.UserId;
            var profile = await GetUserProfile(userId);
            var replyMessage = $"Hello, {profile.DisplayName}! Thanks for following!";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleUnfollowEvent(LineEvent ev)
        {
            var userId = ev.Source.UserId;
            var profile = await GetUserProfile(userId);
            Console.WriteLine($"User {profile.DisplayName} unfollowed the bot.");
        }

        private async Task HandleJoinEvent(LineEvent ev)
        {
            var replyMessage = "Thanks for inviting me!";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleLeaveEvent(LineEvent ev)
        {
            Console.WriteLine("Bot was removed from group.");
        }

        private async Task HandlePostbackEvent(LineEvent ev)
        {
            var data = ev.Postback.Data;
            var replyMessage = $"Received postback data: {data}";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleBeaconEvent(LineEvent ev)
        {
            var replyMessage = $"Received beacon event: {ev.Beacon.Type}";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleAccountLinkEvent(LineEvent ev)
        {
            var replyMessage = $"Received account link event: {ev.Source.UserId}";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleMemberJoinedEvent(LineEvent ev)
        {
            var replyMessage = $"Received member joined event: {ev.Joined.Members.Length} members";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }

        private async Task HandleMemberLeftEvent(LineEvent ev)
        {
            var replyMessage = $"Received member left event: {ev.Left.Members.Length} members";
            await ReplyMessage(ev.ReplyToken, replyMessage);
        }
    }

    /// <summary>
    /// webhook 事件
    /// </summary>
    public class LineWebhookEvent
    {
        [JsonPropertyName("events")]
        public LineEvent[] Events { get; set; }
    }

    public class LineEvent
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("replyToken")]
        public string ReplyToken { get; set; }

        [JsonPropertyName("source")]
        public LineEventSource Source { get; set; }

        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("message")]
        public LineEventMessage Message { get; set; }

        [JsonPropertyName("postback")]
        public LineEventPostback Postback { get; set; }

        [JsonPropertyName("beacon")]
        public LineEventBeacon Beacon { get; set; }

        [JsonPropertyName("link")]
        public LineEventLink Link { get; set; }

        [JsonPropertyName("joined")]
        public LineEventJoined Joined { get; set; }

        [JsonPropertyName("left")]
        public LineEventLeft Left { get; set; }
    }

    public class LineEventSource
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("groupId")]
        public string GroupId { get; set; }

        [JsonPropertyName("roomId")]
        public string RoomId { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    public class LineEventMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("text")]
        public string Text { get; set; }
    }

    public class LineEventPostback
    {
        [JsonPropertyName("data")]
        public string Data { get; set; }

        [JsonPropertyName("params")]
        public LineEventPostbackParams Params { get; set; }
    }

    public class LineEventPostbackParams
    {
        [JsonPropertyName("date")]
        public string Date { get; set; }
    }

    public class LineEventBeacon
    {
        [JsonPropertyName("hwid")]
        public string Hwid { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    public class LineEventLink
    {
        [JsonPropertyName("result")]
        public string Result { get; set; }

        [JsonPropertyName("nonce")]
        public string Nonce { get; set; }
    }

    public class LineEventJoined
    {
        [JsonPropertyName("members")]
        public LineEventSource[] Members { get; set; }
    }

    public class LineEventLeft
    {
        [JsonPropertyName("members")]
        public LineEventSource[] Members { get; set; }
    }

    /// <summary>
    /// LINE 使用者資訊
    /// </summary>
    public class LineUserProfile
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; }

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        [JsonPropertyName("pictureUrl")]
        public string PictureUrl { get; set; }

        [JsonPropertyName("statusMessage")]
        public string StatusMessage { get; set; }
    }

    /// <summary>
    /// 設定選項
    /// </summary>
    public class LineMessagingOptions
    {
        public const string Name = "LineMessaging";
        public string ChannelAccessToken { get; set; }

        public string ChannelSecret { get; set; }

        public string ChannelId { get; set; }

        public string BotBasicId { get; set; }

        public string UserId { get; set; }
    }

    /// <summary>
    /// LINE 登入選項
    /// </summary>
    public class LineLoginOptions
    {
        public const string Name = "LineLogin";

        public string ChannelSecret { get; set; }

        public string ChannelId { get; set; }

        public string CallbackUrl { get; set; }

        public string UserId { get; set; }
    }
}
