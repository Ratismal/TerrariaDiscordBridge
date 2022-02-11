using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DiscordBridge.Structs;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI.Chat;
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
        private bool _heartbeatStarted = false;

        private readonly string BASE_ENDPOINT = "https://discord.com/api/v9";

        public ExtendedUser BotUser;

        public Discord(DiscordBridge mod)
        {
            this.mod = mod;
        }

        public void StartDiscord()
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
                    if (_started) {
                        _started = false;
                        StartDiscord();
                    } 
                    else
                    {
                        _started = false;
                    }
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
            _started = false;
            _client.Close();
        }

        public async Task StopDiscord()
        {
            await CloseWebSocket();
            _sequence = 0;
            _sessionId = null;
        }

        public async Task HeartBeat()
        {
            if (_heartbeatStarted) {
                return;
            }
            _heartbeatStarted = true;
            while (true)
            {
                await Task.Delay(_interval);

                if (!_started)
                {
                    break;
                }

                await Ack();
            }
            _heartbeatStarted = false;
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
                    StartDiscord();
                    break;
                case 7: // Reconnect
                    mod.Logger.Info("Reconnecting");
                    await CloseWebSocket();
                    StartDiscord();
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
                if (!msg.author.bot && msg.content.StartsWith("!"))
                {
                    var words = msg.content.Substring(1).ToLower().Split(' ');
                    if (words[0] == "ping")
                    {
                        await SendMessage(msg.channel_id, "Pong!");
                    }
                }
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

        public Item ParseItem(string text, string options)
        {
            Item item = new Item();
            int result;
            if (int.TryParse(text, out result) && result < ItemLoader.ItemCount)
            {
                item.netDefaults(result);
            }
            if (item.type <= 0)
            {
                return null;
            }
            item.stack = 1;
            if (options != null)
            {
                string[] array = options.Split(new char[]
                {
                    ','
                });
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i].Length != 0)
                    {
                        char c = array[i][0];
                        if (c <= 'p')
                        {
                            if (c != 'd')
                            {
                                if (c == 'p')
                                {
                                    int result2;
                                    if (int.TryParse(array[i].Substring(1), out result2))
                                    {
                                        item.Prefix((int) ((byte) Utils.Clamp<int>(result2, 0,
                                            (int) ModPrefix.PrefixCount)));
                                    }
                                }
                            }
                            else
                            {
                                item = ItemIO.FromBase64(array[i].Substring(1));
                            }
                        }
                        else if (c == 's' || c == 'x')
                        {
                            int result3;
                            if (int.TryParse(array[i].Substring(1), out result3))
                            {
                                item.stack = Utils.Clamp<int>(result3, 1, item.maxStack);
                            }
                        }
                    }
                }
            }

            return item;
        }

        public string ParseMessage(string message, string boundary)
        {
            var payload = new JObject();

            List<string> attachments = new List<string>();
            var form = new StringBuilder();

            // [i/dH4sIAC5OBWIA/+NiYOBgYM7NT2HgCEktKkosykxkZmDKTGFg4OllAADuEVe2HQAAAA==:3213]
            var regex = ChatManager.Regexes.Format;
            var matches = regex.Matches(message);
            List<Item> items = new List<Item>();
            message = regex.Replace(message, match =>
            {
                if (match.Groups["tag"].Value == "i" || match.Groups["tag"].Value == "item")
                {
                    var item = ParseItem(match.Groups["text"].Value, match.Groups["options"].Value);
                    string output = item.Name;
                    if (item.maxStack == 1)
                    {
                        if (item.AffixName() != null)
                        {
                            output = item.AffixName();
                        }
                    }
                    else
                    {
                        output = item.Name + " (" + item.stack + ")";
                    }
                    items.Add(item);
                    return "[" + output + "]";
                }

                if (match.Groups["tag"].Value == "c" || match.Groups["tag"].Value == "color")
                {
                    return match.Groups["text"].Value;
                }

                return match.Value;
            });

            /*
            if (items.Count > 0)
            {
                List<JObject> embeds = new List<JObject>();

                foreach (var item in items)
                {
                    var embed = new JObject();
                    embed.Add("title", item.AffixName());
                    string desc = "";
                    for (int i = 0; i < item.ToolTip.Lines; i++)
                    {
                        desc += item.ToolTip.GetLine(i) + "\n";
                    }

                    try
                    {
                        var texture = Main.itemTexture[item.type];
                        MemoryStream stream = new MemoryStream();
                        texture.SaveAsPng(stream, item.getRect().Width, item.getRect().Width);
                        // item.
                        embed.Add("description", desc);


                        var filename = "item-" + item.type + ".png";

                        form.Append("--" + boundary + "\r\n");
                        form.Append("Content-Disposition: form-data; name=\"file[");
                        form.Append(attachments.Count);
                        form.Append("]\"; ");
                        form.Append("filename=\"");
                        form.Append(filename);
                        form.Append("\"\r\n");
                        form.Append("Content-Type: image/png\r\n\r\n");
                        form.Append(Convert.ToBase64String(stream.ToArray()) + "\r\n");

                        attachments.Add(filename);

                        var image = new JObject();
                        image.Add("url", "attachments://" + filename);
                        embed.Add("image", image);
                    } catch {}

                    embeds.Add(embed);
                }
                payload.Add("embeds", JToken.FromObject(embeds));
            }
            */

            payload.Add("content", message);
            var json = JsonConvert.SerializeObject(payload);
       
            form.Append("--" + boundary + "\r\n");
            form.Append("Content-Disposition: form-data; name=\"payload_json\"\r\n");
            form.Append("Content-Type: application/json\r\n\r\n");
            form.Append(json + "\r\n");

            form.Append("--" + boundary + "--");

            return form.ToString();
        }

        public async Task SendMessage(string channel, string message)
        {
            var boundary = "--" + DateTime.Now.Ticks;

            var url = BASE_ENDPOINT + "/channels/" + channel + "/messages";

            var payload = ParseMessage(message, boundary);

            mod.Logger.Debug(payload);

            var request = WebRequest.CreateHttp(url);
            request.Headers.Add("Authorization", "Bot " + mod.config.token);
            request.Method = "POST";

            var data = Encoding.UTF8.GetBytes(payload);
            request.ContentType = "multipart/form-data; boundary=" + boundary;
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
