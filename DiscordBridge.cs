using System.Threading.Tasks;
using Terraria.ModLoader;
using Discord;
using Discord.WebSocket;

namespace DiscordBridge
{
	public class DiscordBridge : Mod
    {
        private DiscordSocketClient _client;

        public override void Load()
        {
            MainDiscord();
        }

        public async Task MainDiscord()
        {
            _client = new DiscordSocketClient();
            _client.Log += Log;

            await _client.LoginAsync(TokenType.Bot, "");
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private async Task Log(LogMessage msg)
        {
            Logger.Debug(msg);
        }
    }
}