using System.IO;
using System.Threading.Tasks;
using Terraria.ModLoader;
using System.Net.WebSockets;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.Chat;
using Terraria.GameContent.NetModules;
using Terraria.ID;
using Terraria.Localization;
using NetMessage = On.Terraria.NetMessage;
using NetTextModule = On.Terraria.GameContent.NetModules.NetTextModule;

// using Discord;
// using Discord.WebSocket;

namespace DiscordBridge
{
	public class DiscordBridge : Mod
    {
        private Discord discord;
        public DiscordBridgeConfig config;

        public override void Load()
        {
            if (Main.netMode != NetmodeID.Server)
            {
                return;
            }

            config = GetConfig("DiscordBridgeConfig") as DiscordBridgeConfig;

            discord = new Discord(this);
            Task.Factory.StartNew(discord.StartDiscord);

            On.Terraria.NetMessage.BroadcastChatMessage += NetMessageOnBroadcastChatMessage;
            On.Terraria.GameContent.NetModules.NetTextModule.DeserializeAsServer += NetTextModuleOnDeserializeAsServer;
        }

        private bool NetTextModuleOnDeserializeAsServer(NetTextModule.orig_DeserializeAsServer orig, Terraria.GameContent.NetModules.NetTextModule self, BinaryReader reader, int senderplayerid)
        {
            long savedPosition = reader.BaseStream.Position;
            ChatMessage message = ChatMessage.Deserialize(reader);

            discord.SendMessage(config.channelId, "<" + Main.player[senderplayerid].name + "> " + message.Text);

            reader.BaseStream.Position = savedPosition;
            return orig(self, reader, senderplayerid);
        }

        private void NetMessageOnBroadcastChatMessage(NetMessage.orig_BroadcastChatMessage orig, NetworkText text, Color color, int excludedplayer)
        {
            Logger.Debug("BroadcastChatMessage " + text);

            var str = text.ToString();
            var reg = new Regex(@"^.+>> ", RegexOptions.Compiled);
            if (!reg.IsMatch(str))
            {
                discord.SendMessage(config.channelId, text.ToString());
            }

            orig(text, color, excludedplayer);
        }

        public override void Unload()
        {
            Task.Factory.StartNew(discord.StopDiscord);
            On.Terraria.NetMessage.BroadcastChatMessage -= NetMessageOnBroadcastChatMessage;
            On.Terraria.GameContent.NetModules.NetTextModule.DeserializeAsServer -= NetTextModuleOnDeserializeAsServer;
        }
    }
}