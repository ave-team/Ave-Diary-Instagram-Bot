using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.Text;

namespace DiaryInstaBot.ApiResponses
{
    public class TomorrowHomeworkResponse
    {
        [JsonProperty("dz")]
        public string Homework { get; set; }

        [JsonProperty("server_date")]
        [JsonConverter(typeof(IsoDateTimeConverter))]
        public DateTime ServerDate { get; set; }

        [JsonProperty("server_time")]
        public TimeSpan ServerTime { get; set; }
    }
}
