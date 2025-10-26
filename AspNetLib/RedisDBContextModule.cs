using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Santel.Redis.TypedKeys
{
    public class RedisDBContextModuleConfigs
    {
        public ISubscriber? Sub { get; set; }
        public RedisChannel? Channel { get; set; }
        public ILogger? Logger { get; set; }
        public bool KeepDataInMemory { get; set; }
        public IConnectionMultiplexer Writer { get; set; }
        public IConnectionMultiplexer Reader { get; set; }

        public void PublishByKey(IRedisHashKey a, string key)
        {
            if (Channel.HasValue)
                Sub?.Publish(Channel.Value, $"{a.FullName}|{key}");
        }
        public void Publish(IRedisHashKey a)
        {
            if (Channel.HasValue)
                Sub?.Publish(Channel.Value, $"{a.FullName}|all");
        }
        public void Publish(IRedisKey a)
        {
            if (Channel.HasValue)
                Sub?.Publish(Channel.Value, $"{a.FullName}");
        }
    }
    public class RedisDBContextModule
    {
        private RedisDBContextModuleConfigs Config { set; get; } = new();
        public RedisDBContextModule() { }
        public RedisDBContextModule(RedisDBContextExtendedOptions options, ILogger<RedisDBContextModule>? logger = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ConnectionMultiplexerWrite == null && options.ConnectionMultiplexerRead == null)
                throw new ArgumentException("At least one connection multiplexer must be provided in options.", nameof(options));

            var write = options.ConnectionMultiplexerWrite ?? options.ConnectionMultiplexerRead!;
            var read = options.ConnectionMultiplexerRead ?? options.ConnectionMultiplexerWrite!;

            Init(write, read, options.KeepDataInMemory, logger, options.NameGeneratorStrategy, options.ChannelName);
        }
        public RedisDBContextModule(RedisDBContextOptions options, ILogger<RedisDBContextModule>? logger = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ConnectionMultiplexer == null)
                throw new ArgumentException("At least one connection multiplexer must be provided in options.", nameof(options));
            Init(options.ConnectionMultiplexer, options.ConnectionMultiplexer, options.KeepDataInMemory, logger, options.NameGeneratorStrategy, options.ChannelName);
        }

        void Init(
          IConnectionMultiplexer connectionMultiplexerWrite,
          IConnectionMultiplexer connectionMultiplexerRead,
          bool keepDataInMemory,
          ILogger? logger,
          Func<string, string>? nameGeneratorStrategy = null,
          string? channelName = null
          )
        {
            Config.KeepDataInMemory = keepDataInMemory;
            Config.Writer = connectionMultiplexerWrite;
            Config.Reader = connectionMultiplexerRead;
            Config.Logger = logger;
            Config.Sub = connectionMultiplexerWrite.GetSubscriber();
            Config.Channel = !string.IsNullOrWhiteSpace(channelName) ? new RedisChannel(channelName, RedisChannel.PatternMode.Literal) : default;
            // Single pass over properties to initialize keys
            foreach (var prop in GetType().GetProperties())
            {
                var pType = prop.PropertyType;
                if (!pType.IsGenericType)
                    continue;

                var genDef = pType.GetGenericTypeDefinition();
                var tArg = pType.GetGenericArguments()[0];
                var fullKeyName = nameGeneratorStrategy != null ? nameGeneratorStrategy(prop.Name) : prop.Name;

                if (genDef == typeof(RedisHashKey<>))
                {
                    var item = prop.GetValue(this) as IRedisHashKey
                               ?? Activator.CreateInstance(typeof(RedisHashKey<>).MakeGenericType(tArg), 1, null, null) as IRedisHashKey;
                    prop.SetValue(this, item);
                    item!.Init(Config, fullKeyName);
                }
                if (genDef == typeof(RedisPrefixedKeys<>))
                {
                    var item = prop.GetValue(this) as IRedisHashKey
                               ?? Activator.CreateInstance(typeof(RedisPrefixedKeys<>).MakeGenericType(tArg), 1, null, null) as IRedisHashKey;
                    prop.SetValue(this, item);
                    item!.Init(Config, fullKeyName);
                }
                else if (genDef == typeof(RedisKey<>))
                {
                    var item = prop.GetValue(this) as IRedisKey
                               ?? Activator.CreateInstance(typeof(RedisKey<>).MakeGenericType(tArg), 1, null, null) as IRedisKey;
                    prop.SetValue(this, item);
                    item!.Init(Config, fullKeyName);
                }
            }
        }
    }
}

