using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBridge.Structs
{
    public class Message
    {
        public User author;
        public Member member;
        public string channel_id;
        public string content;
        public string edited_timestamp;
        public int flags;
        public string guild_id;
        public string id;
        public string nonce;
        public bool pinned;
        public string timestamp;
        public int type;
    }
}
