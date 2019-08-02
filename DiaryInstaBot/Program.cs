using AveDiaryInstaBot;
using System;
using System.Text;

namespace DiaryInstaBot
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            InstaBot bot = new InstaBot();
            bot.StartPolling();

            Console.ReadLine();
            bot.StopPolling();
        }
    }
}
