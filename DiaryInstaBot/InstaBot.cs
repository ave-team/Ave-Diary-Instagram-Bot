using InstagramApiSharp.API;
using InstagramApiSharp.API.Builder;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Logger;
using System;
using System.Collections.Generic;
using System.Text;

namespace AveDiaryInstaBot
{
    public class InstaBot
    {
        private const string AveDiaryApiBaseLink = "https://avediary.online/api.php";
        private IInstaApi instaApi;
        private IRequestDelay instaApiDelay;
        private UserSessionData loginData;

        public InstaBot(string username, string password)
        {
            this.loginData = new UserSessionData
            {
                UserName = username,
                Password = password
            };

            InitializeInstaApi();
        }
        private void InitializeInstaApi()
        {
            this.instaApiDelay = RequestDelay.FromSeconds(2, 2);
            this.instaApi = InstaApiBuilder.CreateBuilder()
                 .SetUser(this.loginData)
                 .UseLogger(new DebugLogger(LogLevel.Exceptions))
                 .SetRequestDelay(this.instaApiDelay)
                 .Build();
        }
    }
}
