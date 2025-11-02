using Moq;
using StackExchange.Redis;
using Santel.Redis.TypedKeys;
using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class RedisKeyTests
    {
        private Mock<IConnectionMultiplexer> _mockConnection;
        private Mock<IDatabase> _mockDatabase;
        private Mock<ILogger> _mockLogger;
        private RedisDBContextModuleConfigs _contextConfig;

        public RedisKeyTests()
        {
            _mockConnection = new Mock<IConnectionMultiplexer>();
            _mockDatabase = new Mock<IDatabase>();
            _mockLogger = new Mock<ILogger>();

            _mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
         .Returns(_mockDatabase.Object);

            _contextConfig = new RedisDBContextModuleConfigs
            {
                Reader = _mockConnection.Object,
                Writer = _mockConnection.Object,
                Logger = _mockLogger.Object,
                KeepDataInMemory = true
            };
        }

        [Fact]
        public void Constructor_ShouldInitializeWithDefaultSerialization()
        {
            // Arrange & Act
            var redisKey = new RedisKey<string>(0);

            // Assert
            Assert.Equal(0, redisKey.DbIndex);
            Assert.NotNull(redisKey.Serialize);
            Assert.NotNull(redisKey.DeSerialize);
        }

        [Fact]
        public void Constructor_ShouldInitializeWithCustomSerialization()
        {
            // Arrange & Act
            var redisKey = new RedisKey<int>(0,
   serialize: x => x.ToString(),
        deSerialize: x => int.Parse(x));

            // Assert
            Assert.Equal(0, redisKey.DbIndex);
            Assert.NotNull(redisKey.Serialize);
            Assert.NotNull(redisKey.DeSerialize);
        }

        [Fact]
        public void Init_ShouldSetProperties()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            var fullName = new StackExchange.Redis.RedisKey("test:key");

            // Act
            redisKey.Init(_contextConfig, fullName);

            // Assert
            Assert.Equal(fullName, redisKey.FullName);
            Assert.Equal(_contextConfig, redisKey.ContextConfig);
            Assert.NotNull(redisKey.Reader);
            Assert.NotNull(redisKey.Writer);
        }

        [Fact]
        public void Write_ShouldReturnFalse_WhenDataIsNull()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            // Act
            var result = redisKey.Write(null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Write_ShouldCallStringSet_AndReturnTrue()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringSet(
              It.IsAny<StackExchange.Redis.RedisKey>(),
            It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
        It.IsAny<bool>(),
                        It.IsAny<When>(),
                 It.IsAny<CommandFlags>()))
            .Returns(true);

            // Act
            var result = redisKey.Write("test value");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.StringSet(
                It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<RedisValue>(),
      It.IsAny<TimeSpan?>(),
          It.IsAny<bool>(),
   It.IsAny<When>(),
     It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public async Task WriteAsync_ShouldCallStringSetAsync_AndReturnTrue()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringSetAsync(
         It.IsAny<StackExchange.Redis.RedisKey>(),
   It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan?>(),
      It.IsAny<bool>(),
     It.IsAny<When>(),
It.IsAny<CommandFlags>()))
     .ReturnsAsync(true);

            // Act
            var result = await redisKey.WriteAsync("test value");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.StringSetAsync(
               It.IsAny<StackExchange.Redis.RedisKey>(),
                      It.IsAny<RedisValue>(),
          It.IsAny<TimeSpan?>(),
           It.IsAny<bool>(),
            It.IsAny<When>(),
           It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Read_ShouldReturnDefault_WhenKeyDoesNotExist()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringGet(
                    It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<CommandFlags>()))
                       .Returns(RedisValue.Null);

            // Act
            var result = redisKey.Read();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ReadAsync_ShouldReturnDefault_WhenKeyDoesNotExist()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringGetAsync(
               It.IsAny<StackExchange.Redis.RedisKey>(),
                    It.IsAny<CommandFlags>()))
           .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await redisKey.ReadAsync();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void InvalidateCache_ShouldClearInMemoryCache()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringSet(
             It.IsAny<StackExchange.Redis.RedisKey>(),
           It.IsAny<RedisValue>(),
    It.IsAny<TimeSpan?>(),
          It.IsAny<bool>(),
         It.IsAny<When>(),
          It.IsAny<CommandFlags>()))
          .Returns(true);

            _mockDatabase.Setup(x => x.StringGet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
                It.IsAny<CommandFlags>()))
               .Returns(RedisValue.Null);

            redisKey.Write("test");

            // Act
            redisKey.InvalidateCache();
            var result = redisKey.Read();

            // Assert - Should call Redis again after cache invalidation
            _mockDatabase.Verify(x => x.StringGet(
            It.IsAny<StackExchange.Redis.RedisKey>(),
 It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Read_ShouldUseCachedValue_WhenKeepDataInMemoryIsTrue()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringSet(
              It.IsAny<StackExchange.Redis.RedisKey>(),
                It.IsAny<RedisValue>(),
                  It.IsAny<TimeSpan?>(),
                   It.IsAny<bool>(),
             It.IsAny<When>(),
            It.IsAny<CommandFlags>()))
               .Returns(true);

            redisKey.Write("test value");

            // Act
            var result1 = redisKey.Read();
            var result2 = redisKey.Read();

            // Assert
            Assert.Equal("test value", result1);
            Assert.Equal("test value", result2);
            // Verify Redis was not called for the second read (using cache)
            _mockDatabase.Verify(x => x.StringGet(
        It.IsAny<StackExchange.Redis.RedisKey>(),
       It.IsAny<CommandFlags>()), Times.Never);
        }

        [Fact]
        public void Read_WithForce_ShouldBypassCache()
        {
            // Arrange
            var redisKey = new RedisKey<string>(0);
            redisKey.Init(_contextConfig, "test:key");

            _mockDatabase.Setup(x => x.StringSet(
           It.IsAny<StackExchange.Redis.RedisKey>(),
        It.IsAny<RedisValue>(),
   It.IsAny<TimeSpan?>(),
          It.IsAny<bool>(),
         It.IsAny<When>(),
          It.IsAny<CommandFlags>()))
        .Returns(true);

            _mockDatabase.Setup(x => x.StringGet(
             It.IsAny<StackExchange.Redis.RedisKey>(),
                     It.IsAny<CommandFlags>()))
             .Returns(RedisValue.Null);

            redisKey.Write("test value");

            // Act
            var result = redisKey.Read(force: true);

            // Assert
            _mockDatabase.Verify(x => x.StringGet(
         It.IsAny<StackExchange.Redis.RedisKey>(),
         It.IsAny<CommandFlags>()), Times.Once);
        }
    }
}
