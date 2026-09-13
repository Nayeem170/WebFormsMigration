namespace CoreWebForms
{
    public static class SessionState
    {
        public static StackExchange.Redis.IConnectionMultiplexer? Multiplexer { get; set; }

        public static bool StoreUnavailable
            => Multiplexer != null && !Multiplexer.IsConnected;
    }
}
