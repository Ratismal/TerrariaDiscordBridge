using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader.Config;

namespace DiscordBridge
{
    public class DiscordBridgeConfig : ModConfig
    {
        public override ConfigScope Mode => ConfigScope.ServerSide;

        [DefaultValue("Put your token here!")] public string token;
        [DefaultValue("Put your channel id here!")] public string channelId;
    }
}
