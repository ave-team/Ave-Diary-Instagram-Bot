using DiaryInstaBot.ApiResponses;
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
            var isUserExists = JsonConvert.DeserializeObject<IsClassLoginExistsResponse>(jsonResult);
            return isUserExists.Result;
        }

        public async Task<string> GetTomorrowHomework(string classLogin)
        {
            var response = await this.http.GetAsync($"?login={classLogin}&type=json&date=tomorrow");

            if (!response.IsSuccessStatusCode)
                throw new Exception("Ave Diary Api returned error status code");

            var jsonResult = await response.Content.ReadAsStringAsync();
            var tomorrowHomeworkResponse = JsonConvert.DeserializeObject<TomorrowHomeworkResponse>(jsonResult);
            return tomorrowHomeworkResponse.Homework;
        }
    }
}
