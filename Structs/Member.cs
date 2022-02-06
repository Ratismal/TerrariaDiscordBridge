using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBridge.Structs
{
    public class Member
    {
        public string avatar;
        public bool deaf;
        public string hoisted_role;
        public string joined_at;
        public bool mute;
        public string nick;
        public bool pending;
        public string premium_since;
        public List<string> roles;
    }
}
