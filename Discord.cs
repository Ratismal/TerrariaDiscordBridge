using System;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscordBridge.Structs;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DiscordBridge
{
    public class Discord
    {
        private DiscordBridge mod;

        private readonly Uri DISCORD_URI = new Uri("wss://gateway.discord.gg/?v=9&encoding=json");
        private ClientWebSocket _client;
        private CancellationTokenSource _cts;

        private string _sessionId;
        private int _sequence = 0;

        private bool _started = false;

        private static HttpClient _restClient = new HttpClient();

        private readonly string BASE_ENDPOINT = "https://discord.com/api/v9";

        public ExtendedUser BotUser;

        public Discord(DiscordBridge mod)
        {
            this.mod = mod;
        }

        public async Task StartDiscord()
        {
            mod.Logger.Info("Starting Discord Connection");
            if (_started)
            {
                return;
            }

            _started = true;
            _client = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            _restClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", mod.config.token);

            await _client.ConnectAsync(DISCORD_URI, _cts.Token);

            await Task.Factory.StartNew(ReceiveMessage, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private async Task CloseWebSocket()
        {
            await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cts.Token);
        }

        public async Task StopDiscord()
        {
            await CloseWebSocket();
            _sequence = 0;
            _sessionId = null;
        }

        private async Task ReceiveMessage()
        {
            mod.Logger.Info("Starting message receiver...");
            var rcvBytes = new byte[128];
            var rcvBuffer = new ArraySegment<byte>(rcvBytes);

            string receivedMessage = "";
            while (true)
            {
                WebSocketReceiveResult rcvResult = await _client.ReceiveAsync(rcvBuffer, _cts.Token);
                // mod.Logger.Debug("Received a message!");

                byte[] msgBytes = rcvBuffer.Skip(rcvBuffer.Offset).Take(rcvResult.Count).ToArray();
                string rcvMsg = Encoding.UTF8.GetString(msgBytes);
                receivedMessage += rcvMsg;

                if (rcvResult.EndOfMessage)
                {
                    var msg = JsonConvert.DeserializeObject<JObject>(receivedMessage);
                    if (msg != null)
                    {
                        // mod.Logger.Debug(receivedMessage);
                        receivedMessage = "";

                        HandleMessage(msg);
                    }
                }
            }
        }

        private async Task HandleMessage(JObject msg)
        {
            int op = msg.GetValue("op").Value<int>();
            if (msg.GetValue("s").HasValues)
            {
                int sequence = msg.GetValue("s").Value<int>();
                _sequence = sequence;
            }

            JObject data;
            try
            {
                data = msg.GetValue("d").Value<JObject>();
            }
            catch
            {
                data = new JObject();
            }

            switch (op)
            {
                case 10: // Hello
                    if (_sessionId != null)
                    {
                        await Resume();
                    }
                    else
                    {
                        await Identify();
                    }
                    break;
                case 1: // Heartbeat
                    await Ack();
                    break;
                case 9: // Invalid Session
                    mod.Logger.Info("Invalid session, reconnecting");
                    await CloseWebSocket();
                    await StartDiscord();
                    break;
                case 7: // Reconnect
                    mod.Logger.Info("Reconnecting");
                    await CloseWebSocket();
                    await StartDiscord();
                    break;
                case 0: // Dispatch
                    string t = msg.GetValue("t").Value<string>();
                    switch (t)
                    {
                        case "READY":
                            await OnReady(data);
                            break;
                        case "MESSAGE_CREATE":
                            await OnMessageCreate(data);
                            break;
                    }
                    break;
            }
        }

        private async Task Send(string jsonMessage)
        {
            var sendBytes = Encoding.UTF8.GetBytes(jsonMessage);
            var sendBuffer = new ArraySegment<byte>(sendBytes);
            await _client.SendAsync(sendBuffer, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: _cts.Token);
        }

        private async Task Send(JObject obj)
        {
            await Send(JsonConvert.SerializeObject(obj));
        }

        private async Task Ack()
        {
            var payload = new JObject();
            payload.Add("op", 11);

            await Send(payload);
        }

        private async Task Identify()
        {
            var payload = new JObject();
            payload.Add("op", 2);

            var data = new JObject();
            payload.Add("d", data);
            data.Add("token", mod.config.token);
            data.Add("intents", 515);

            var properties = new JObject();
            data.Add("properties", properties);
            properties.Add("$os", "linux probably");
            properties.Add("$browser", "stupid cat's amazing custom .NET library");
            properties.Add("device", "stupid cat's amazing custom .NET library");

            await Send(payload);
        }

        private async Task Resume()
        {
            var payload = new JObject();
            payload.Add("op", 6);

            var data = new JObject();
            payload.Add("d", data);
            data.Add("token", mod.config.token);
            data.Add("session_id", _sessionId);
            data.Add("seq", _sequence);

            await Send(payload);
        }

        private async Task OnReady(JObject data)
        {
            var sessionId = data.GetValue("session_id").Value<string>();
            this._sessionId = sessionId;

            var user = data.GetValue("user").ToObject<ExtendedUser>();
            BotUser = user;

            mod.Logger.Info("Logged in as " + user.username + "#" + user.discriminator);

            await SendMessage(mod.config.channelId, "Ready!");
        }

        private async Task OnMessageCreate(JObject data)
        {
            var msg = data.ToObject<Message>();
            if (msg.channel_id == mod.config.channelId && msg.content != null && msg.author.id != BotUser.id)
            {
                var text = msg.author.username + ">> " + msg.content;
                if (Main.netMode != NetmodeID.Server)
                {
                    Main.NewText(text, 120, 170, 220);
                }
                else
                {
                    NetworkText nText = NetworkText.FromLiteral(text);
                    NetMessage.BroadcastChatMessage(nText, new Color(120, 170, 220));
                }
            }
        }

        public async Task SendMessage(string channel, string message)
        {
            var url = BASE_ENDPOINT + "/channels/" + channel + "/messages";

            var payload = new JObject();
            payload.Add("content", message);

            StringContent str = new StringContent(JsonConvert.SerializeObject(payload));
            str.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            var response = await _restClient.PostAsync(url, str);

            // response.EnsureSuccessStatusCode();
            string body = await response.Content.ReadAsStringAsync();

            // mod.Logger.Debug(response.IsSuccessStatusCode + " " + body);
        }
    }
}
