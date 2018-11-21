#region License
// https://www.allow2.com/developer-license/
#endregion

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Allow2
{
    public sealed class Allow2
    {
        private HttpClient apiClient = new HttpClient();

        // singleton
        private static readonly Lazy<Allow2> lazy = new Lazy<Allow2>(() => new Allow2());
        public static Allow2 Instance { get { return lazy.Value; } }

        // todo: pass required deviceToken param on initialisation of the singleton and make it an
        // internal private variable, so it's fixed for life

        private Allow2() {
            apiClient.BaseAddress = new Uri("https://api.allow2.com/");
            apiClient.DefaultRequestHeaders.Accept.Clear();
            apiClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /*
         * need to set this before pairing
         */
        public string deviceToken = "MISSING";

        /*
         * Pairing
         * <param name="user">login username</param>  
         * <param name="password">login password</param> 
         * <param name="deviceName">initial name for this paired device</param> 
         * 
         * NOTE: you need to set Allow2.deviceToken first!
         */
        public async Task<Boolean> PairAsync(string user, string password, string deviceName)
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>{
                {"user",user},
                {"password", password},
                {"deviceToken", this.deviceToken},
                {"name", deviceName}
            });
            HttpResponseMessage response = await apiClient.PostAsync("api/pairDevice", content);
            response.EnsureSuccessStatusCode();
            return false;
        }
    }
}
 