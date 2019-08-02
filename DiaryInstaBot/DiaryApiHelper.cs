using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DiaryInstaBot
{
    public class DiaryApiHelper
    {
        private class IsUserExistsResponse
        {
            public bool Result { get; set; }
        }

        private HttpClient http;

        public DiaryApiHelper()
        {
            this.http = new HttpClient
            {
                BaseAddress = new Uri("https://avediary.online/api.php")
            };
        }

        public async Task<bool> IsClassLoginExists(string classLogin)
        {
            var response = await this.http.GetAsync($"?type=test&login={classLogin}");

            if(!response.IsSuccessStatusCode)
                throw new Exception("Ave Diary Api returned error status code");

            var jsonResult = await response.Content.ReadAsStringAsync();
            var isUserExists = JsonConvert.DeserializeObject<IsUserExistsResponse>(jsonResult);
            return isUserExists.Result;
        }
    }
}
