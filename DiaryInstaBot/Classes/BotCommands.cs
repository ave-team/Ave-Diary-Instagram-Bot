using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace DiaryInstaBot.Classes
{
    public class BotCommands
    {
        public List<string> Login { get; set; }
        public List<string> Help { get; set; }
        public List<string> TomorrowHomework { get; set; }
        public List<string> AllHomework { get; set; }

        public object this[string propertyName]
        {
            get
            {
                var myType = typeof(BotCommands);
                var myPropInfo = myType.GetProperty(propertyName);
                return myPropInfo.GetValue(this, null);
            }
            set
            {
                var myType = typeof(BotCommands);
                var myPropInfo = myType.GetProperty(propertyName);
                myPropInfo.SetValue(this, value, null);

            }

        }
    }
}
