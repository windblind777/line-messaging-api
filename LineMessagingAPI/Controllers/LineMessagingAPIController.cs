using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace LineMessagingAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LineMessagingAPIController : ControllerBase
    {
        private readonly ILogger<LineMessagingAPIController> _logger;
        private readonly LineMessagingManager _lineMessagingManager;
        private readonly HttpClient _httpClient = new();

        public LineMessagingAPIController(ILogger<LineMessagingAPIController> logger, LineMessagingManager lineMessagingManager)
        {
            _logger = logger;
            _lineMessagingManager = lineMessagingManager;
        }

        [HttpGet(Name = "GetLineMessaging")]
        public string Get()
        {
            return "test";
        }

        [HttpPost(Name = "PostLineMessaging")]
        public async Task<IActionResult> Post([FromBody] JsonObject requestBody)
        {
            Console.WriteLine($"Received Webhook: {requestBody}");

            var events = requestBody["events"]?.AsArray();
            if (events == null) return Ok();

            foreach (var ev in events)
            {
                var type = ev["type"]?.ToString();
                if (type == "message")
                {
                    var replyToken = ev["replyToken"]?.ToString();
                    var userMessage = ev["message"]?["text"]?.ToString();

                    if (!string.IsNullOrEmpty(replyToken) && !string.IsNullOrEmpty(userMessage))
                    {
                        await _lineMessagingManager.ReplyMessage(replyToken, $"你說了: {userMessage}");
                    }
                }
            }

            return Ok();
        }

        [HttpPost("line/webhook")]
        public async Task<IActionResult> Post([FromBody] JsonElement payload)
        {
            // 解析 JSON 取得 webhook訊息
            if (payload.TryGetProperty("events", out var events) && events.GetArrayLength() > 0)
            {
                var groupId = events[0].GetProperty("source").GetProperty("groupId").GetString();
                if (groupId != null && !string.IsNullOrEmpty(groupId))
                {
                    Console.WriteLine($"📌 取得 groupId: {groupId}");
                }
                    
                var userId = events[0].GetProperty("source").GetProperty("userId").GetString();
                if (userId != null && !string.IsNullOrEmpty(userId))
                {
                    Console.WriteLine($"📌 取得 userId: {userId}");
                }                    
            }

            return Ok();
        }

        [HttpGet("callback")]
        public async Task<RedirectResult> LineLoginCallback([FromQuery] string code, [FromQuery] string state)
        {
            if (string.IsNullOrEmpty(code))
                Redirect($"https://www.google.com/");

            Console.WriteLine($"✅ 進入callback: code={code}, state={state}");
            // 解析 state 取得 taxId & deviceId
            var stateParts = state.Split("-");
            string taxId = stateParts[0];
            string deviceId = stateParts[1];

            // 呼叫 LINE API 取得 userId
            var id_token = await _lineMessagingManager.GetIdTokenFromLine(code);
            if (string.IsNullOrEmpty(id_token))
                return Redirect($"https://www.google.com/");

            // 存入資料庫
            Console.WriteLine($"✅ 綁定成功: TaxID={taxId}, DeviceID={deviceId}, id_token={id_token}");
            
            var userId = await _lineMessagingManager.GetUserIdFromLine(id_token);
            if (string.IsNullOrEmpty(userId))
                return Redirect($"https://www.google.com/");
            await _lineMessagingManager.PushMessage(userId, "綁定成功");
            return Redirect($"line://ti/p/{_lineMessagingManager.GetLineMessagingAPIBotBasicId()}");
        }        
    }
}
