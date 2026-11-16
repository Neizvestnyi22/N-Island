using System;
namespace NIsland.Core
{
    public static class BotRetryPolicy
    {
        public static bool MayRetry(string method)=>method is "getMe" or "getUpdates" or "editMessageWidgetData";
        public static TimeSpan Delay(int failures,int code=0,int retryAfter=0)
        {
            if(code==429&&retryAfter>0)return TimeSpan.FromSeconds(Math.Clamp(retryAfter,1,3600));
            if(code is 401 or 409)return TimeSpan.FromSeconds(60);
            return TimeSpan.FromSeconds(Math.Min(30,Math.Pow(2,Math.Clamp(failures,0,5))));
        }
    }
}
