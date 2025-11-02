using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public class RedisDBContextExtendedOptions
    {
        public IConnectionMultiplexer? ConnectionMultiplexerWrite { get; set; }
        public IConnectionMultiplexer? ConnectionMultiplexerRead { get; set; }
        public bool KeepDataInMemory { get; set; }
        public Func<string, string>? NameGeneratorStrategy { get; set; } = (c) => c;
        public string? ChannelName { get; set; }
    }
    public class RedisDBContextOptions
    {
        public IConnectionMultiplexer? ConnectionMultiplexer { get; set; }
        public bool KeepDataInMemory { get; set; } = true;
        public Func<string, string>? NameGeneratorStrategy { get; set; } = (c) => c;
        public string? ChannelName { get; set; }
    }
}