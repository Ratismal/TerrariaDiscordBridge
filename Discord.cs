using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
using WebSocketSharp;

namespace DiscordBridge
{
    public class Discord
    {
        private DiscordBridge mod;

        private readonly string DISCORD_URI = "wss://gateway.discord.gg/?v=9&encoding=json";
        private WebSocket _client;
        private CancellationTokenSource _cts;

        private string _sessionId;
        private int _sequence = 0;
        private int _interval;

        private bool _started = false;

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
            _client = new WebSocket(DISCORD_URI);
            _cts = new CancellationTokenSource();

            try
            {
                _client.ConnectAsync();
                _client.EmitOnPing = true;
                _client.OnMessage += ClientOnOnMessage;
                _client.OnError += (sender, args) =>
                {
                    mod.Logger.Error(args.Message);
                };
                _client.OnClose += (sender, args) =>
                {
                    _started = false;
                    mod.Logger.Info("WebSocket closed: " + args.Code + " " + args.Reason);
                };

                mod.Logger.Info("Connection established");
            }
            catch (Exception e)
            {
                mod.Logger.Debug(e.Message);
                mod.Logger.Debug(e.StackTrace);
            }
        }

        private void ClientOnOnMessage(object sender, MessageEventArgs e)
        {
            var msg = JsonConvert.DeserializeObject<JObject>(e.Data);
            if (msg != null)
            {
                HandleMessage(msg);
            }
        }

        private async Task CloseWebSocket()
        {
            _client.CloseAsync();
            // await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, _cts.Token);
        }

        public async Task StopDiscord()
        {
            await CloseWebSocket();
            _sequence = 0;
            _sessionId = null;
        }

        public async Task HeartBeat()
        {
            while (true)
            {
                await Task.Delay(_interval);

                if (!_started)
                {
                    return;
                }

                await Ack();
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

                    _interval = data.GetValue("heartbeat_interval").Value<int>();

                    Task.Factory.StartNew(HeartBeat, TaskCreationOptions.LongRunning);
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
            _client.Send(jsonMessage);
        }

        private async Task Send(JObject obj)
        {
            await Send(JsonConvert.SerializeObject(obj));
        }

        private async Task Ack()
        {
            var payload = new JObject();
            payload.Add("op", 1);
            payload.Add("d", _sequence);

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

            var request = WebRequest.CreateHttp(url);
            request.Headers.Add("Authorization", "Bot " + mod.config.token);
            request.Method = "POST";

            var data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
            request.ContentType = "application/json";
            request.ContentLength = data.Length;

            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }

                var response = (HttpWebResponse) request.GetResponse();

                var responseString = new StreamReader(response.GetResponseStream()).ReadToEnd();
            }
            catch (Exception e)
            {
                mod.Logger.Debug(e.Message);
                mod.Logger.Debug(e.StackTrace);
            }
        }
    }
}
