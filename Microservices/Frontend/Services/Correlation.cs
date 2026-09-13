using System;
using System.Web;

namespace CoreWebForms.Services
{
    internal static class Correlation
    {
        private const string ItemsKey = "CorrelationId";

        internal static string? TryCurrent()
        {
            return HttpContext.Current?.Items[ItemsKey] as string;
        }

        internal static string Current()
        {
            var ctx = HttpContext.Current;
            if (ctx != null)
            {
                if (ctx.Items[ItemsKey] is string existing && existing.Length > 0)
                    return existing;
                var minted = Guid.NewGuid().ToString("N");
                ctx.Items[ItemsKey] = minted;
                return minted;
            }
            return Guid.NewGuid().ToString("N");
        }
    }
}
