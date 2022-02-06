using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscordBridge.Structs
{
    public class User
    {
        public string avatar;
        public bool bot;
        public string discriminator;
        public string id;
        public string username;
        public int public_flags;
    }

    public class ExtendedUser : User
    {
        public string email;
        public int flags;
        public bool mfa_enabled;
        public bool verified;
    }
}
