#region License
// https://www.allow2.com/developer-license/
#endregion

using System;
using EasyHttp.Http;

namespace Allow2
{
    public sealed class Allow2
    {
        private static readonly Lazy<Allow2> lazy =
            new Lazy<Allow2>(() => new Allow2());

        public static Allow2 Instance { get { return lazy.Value; } }

        private Allow2()
        {
        }

        public string deviceToken = "MISSING";

        public void pair(string user, string password, string deviceName)
        {
            var http = new HttpClient();
            http.Request.Accept = HttpContentTypes.ApplicationJson;
            http.Post("https://api.allow2.com/api/pairDevice", new {
                user = user,
                password = password,
                deviceToken = deviceToken,
                name = deviceName
            }, HttpContentTypes.ApplicationJson);
        }
    }
}
