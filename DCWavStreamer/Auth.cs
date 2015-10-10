using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DCWavStreamer
{
    class Auth
    {
        public static string getCookie()
        {
            return "cbid=" + getRandomToken() + ";path=/;expires=" + getExpiry();
        }

        private static string getExpiry()
        {
            var now = DateTime.Now;
            var expires = now.AddDays(100);
            return expires.ToString();
        }

        private static string getRandomToken()
        {
            var chars = "abcdefghijklmnopqrstuvwxyz";
            var random = new Random();
            var result = new string(
                Enumerable.Repeat(chars, 72)
                          .Select(s => s[random.Next(s.Length)])
                          .ToArray());
            return result;
        }
    }

}
