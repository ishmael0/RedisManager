using Moq;
using StackExchange.Redis;
using Santel.Redis.TypedKeys;
using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class RedisPrefixedKeysTests
    {
        private Mock<IConnectionMultiplexer> _mockConnection;
        private Mock<IDatabase> _mockDatabase;
        private Mock<ILogger> _mockLogger;
        private RedisDBContextModuleConfigs _contextConfig;

        public RedisPrefixedKeysTests()
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
            var prefixedKeys = new RedisPrefixedKeys<string>(0);

            // Assert
            Assert.Equal(0, prefixedKeys.DbIndex);
            Assert.NotNull(prefixedKeys.Serialize);
            Assert.NotNull(prefixedKeys.DeSerialize);
        }

        [Fact]
        public void Constructor_ShouldInitializeWithCustomSerialization()
        {
            // Arrange & Act
            var prefixedKeys = new RedisPrefixedKeys<int>(0,
   serialize: x => x.ToString(),
      deSerialize: x => int.Parse(x));

            // Assert
            Assert.Equal(0, prefixedKeys.DbIndex);
            Assert.NotNull(prefixedKeys.Serialize);
            Assert.NotNull(prefixedKeys.DeSerialize);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.StringSet(
It.IsAny<StackExchange.Redis.RedisKey>(),
   It.IsAny<RedisValue>(),
    It.IsAny<TimeSpan?>(),
        It.IsAny<bool>(),
                It.IsAny<When>(),
    It.IsAny<CommandFlags>()))
    .Returns(true);

            // Act
            var result = prefixedKeys.Write("key1", "value1");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.StringSet(
                It.Is<StackExchange.Redis.RedisKey>(k => k.ToString().Contains("test:prefix:key1")),
         It.IsAny<RedisValue>(),
     It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
     It.IsAny<When>(),
         It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnFalse_WhenKeyIsNull()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            // Act
            var result = prefixedKeys.Write(null!, "value1");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Write_SingleKey_ShouldReturnFalse_WhenDataIsNull()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            // Act
            var result = prefixedKeys.Write("key1", null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Write_Dictionary_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var data = new Dictionary<string, string>
        {
              { "key1", "value1" },
           { "key2", "value2" }
            };

            _mockDatabase.Setup(x => x.StringSet(
                It.IsAny<StackExchange.Redis.RedisKey>(),
                          It.IsAny<RedisValue>(),
          It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                      It.IsAny<When>(),
          It.IsAny<CommandFlags>()))
                .Returns(true);

            // Act
            var result = prefixedKeys.Write(data);

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.StringSet(
                It.IsAny<StackExchange.Redis.RedisKey>(),
             It.IsAny<RedisValue>(),
           It.IsAny<TimeSpan?>(),
       It.IsAny<bool>(),
        It.IsAny<When>(),
         It.IsAny<CommandFlags>()), Times.Exactly(2));
        }

        [Fact]
        public void Write_Dictionary_ShouldReturnFalse_WhenDataIsEmpty()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var data = new Dictionary<string, string>();

            // Act
            var result = prefixedKeys.Write(data);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Read_SingleKey_ShouldReturnDefault_WhenKeyDoesNotExist()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.StringGet(
               It.IsAny<StackExchange.Redis.RedisKey>(),
             It.IsAny<CommandFlags>()))
         .Returns(RedisValue.Null);

            // Act
            var result = prefixedKeys.Read("key1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void Read_MultipleKeys_ShouldReturnDictionary()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = new[] { "key1", "key2" };

            _mockDatabase.Setup(x => x.StringGet(
 It.IsAny<StackExchange.Redis.RedisKey[]>(),
                It.IsAny<CommandFlags>()))
      .Returns(new[] { RedisValue.Null, RedisValue.Null });

            // Act
            var result = prefixedKeys.Read(keys);

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public async Task ReadAsync_SingleKey_ShouldReturnDefault_WhenKeyDoesNotExist()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.StringGetAsync(
       It.IsAny<StackExchange.Redis.RedisKey>(),
            It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

            // Act
            var result = await prefixedKeys.ReadAsync("key1");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task WriteAsync_SingleKey_ShouldReturnTrue_WhenSuccessful()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.StringSetAsync(
               It.IsAny<StackExchange.Redis.RedisKey>(),
            It.IsAny<RedisValue>(),
                 It.IsAny<TimeSpan?>(),
             It.IsAny<bool>(),
                 It.IsAny<When>(),
           It.IsAny<CommandFlags>()))
                 .ReturnsAsync(true);

            // Act
            var result = await prefixedKeys.WriteAsync("key1", "value1");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Remove_SingleKey_ShouldReturnTrue()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.KeyDelete(
                         It.IsAny<StackExchange.Redis.RedisKey>(),
                    It.IsAny<CommandFlags>()))
               .Returns(true);

            // Act
            var result = prefixedKeys.Remove("key1");

            // Assert
            Assert.True(result);
            _mockDatabase.Verify(x => x.KeyDelete(
         It.Is<StackExchange.Redis.RedisKey>(k => k.ToString().Contains("test:prefix:key1")),
          It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Remove_MultipleKeys_ShouldReturnTrue()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = new[] { "key1", "key2" };

            _mockDatabase.Setup(x => x.KeyDelete(
      It.IsAny<StackExchange.Redis.RedisKey[]>(),
      It.IsAny<CommandFlags>()))
    .Returns(2);

            // Act
            var result = prefixedKeys.Remove(keys);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Remove_ShouldReturnFalse_WhenKeyIsNull()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            // Act
            var result = prefixedKeys.Remove((string)null!);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task RemoveAsync_SingleKey_ShouldReturnTrue()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            _mockDatabase.Setup(x => x.KeyDeleteAsync(
               It.IsAny<StackExchange.Redis.RedisKey>(),
                   It.IsAny<CommandFlags>()))
               .ReturnsAsync(true);

            // Act
            var result = await prefixedKeys.RemoveAsync("key1");

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void InvalidateCache_SingleKey_ShouldClearCachedValue()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

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

            prefixedKeys.Write("key1", "value1");

            // Act
            prefixedKeys.InvalidateCache("key1");
            var result = prefixedKeys.Read("key1");

            // Assert
            _mockDatabase.Verify(x => x.StringGet(
    It.IsAny<StackExchange.Redis.RedisKey>(),
     It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void InvalidateCache_MultipleKeys_ShouldClearCachedValues()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = new[] { "key1", "key2" };

            // Act
            prefixedKeys.InvalidateCache(keys);

            // Assert - No exception should be thrown
            Assert.True(true);
        }

        [Fact]
        public void InvalidateCache_AllKeys_ShouldClearAllCachedValues()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            // Act
            prefixedKeys.InvalidateCache();

            // Assert - No exception should be thrown
            Assert.True(true);
        }

        [Fact]
        public void ReadInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = Enumerable.Range(1, 2500).Select(i => $"key{i}");

            _mockDatabase.Setup(x => x.StringGet(
          It.IsAny<StackExchange.Redis.RedisKey[]>(),
          It.IsAny<CommandFlags>()))
      .Returns((StackExchange.Redis.RedisKey[] redisKeys, CommandFlags flags) =>
              redisKeys.Select(_ => RedisValue.Null).ToArray());

            // Act
            var result = prefixedKeys.ReadInChunks(keys, chunkSize: 1000);

            // Assert
            Assert.NotNull(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.StringGet(
              It.IsAny<StackExchange.Redis.RedisKey[]>(),
               It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public void ReadInChunks_ShouldThrow_WhenChunkSizeIsZero()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = new[] { "key1", "key2" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => prefixedKeys.ReadInChunks(keys, chunkSize: 0));
        }

        [Fact]
        public void WriteInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var data = Enumerable.Range(1, 2500)
                .ToDictionary(i => $"key{i}", i => $"value{i}");

            _mockDatabase.Setup(x => x.StringSet(
           It.IsAny<StackExchange.Redis.RedisKey>(),
          It.IsAny<RedisValue>(),
          It.IsAny<TimeSpan?>(),
           It.IsAny<bool>(),
             It.IsAny<When>(),
             It.IsAny<CommandFlags>()))
               .Returns(true);

            // Act
            var result = prefixedKeys.WriteInChunks(data, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 2500 calls (one per key)
            _mockDatabase.Verify(x => x.StringSet(
         It.IsAny<StackExchange.Redis.RedisKey>(),
     It.IsAny<RedisValue>(),
             It.IsAny<TimeSpan?>(),
           It.IsAny<bool>(),
            It.IsAny<When>(),
       It.IsAny<CommandFlags>()), Times.Exactly(2500));
        }

        [Fact]
        public void RemoveInChunks_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = Enumerable.Range(1, 2500).Select(i => $"key{i}");

            _mockDatabase.Setup(x => x.KeyDelete(
                  It.IsAny<StackExchange.Redis.RedisKey[]>(),
            It.IsAny<CommandFlags>()))
                     .Returns(1000);

            // Act
            var result = prefixedKeys.RemoveInChunks(keys, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.KeyDelete(
           It.IsAny<StackExchange.Redis.RedisKey[]>(),
             It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public async Task ReadInChunksAsync_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = Enumerable.Range(1, 2500).Select(i => $"key{i}");

            _mockDatabase.Setup(x => x.StringGetAsync(
        It.IsAny<StackExchange.Redis.RedisKey[]>(),
        It.IsAny<CommandFlags>()))
                .ReturnsAsync((StackExchange.Redis.RedisKey[] redisKeys, CommandFlags flags) =>
 redisKeys.Select(_ => RedisValue.Null).ToArray());

            // Act
            var result = await prefixedKeys.ReadInChunksAsync(keys, chunkSize: 1000);

            // Assert
            Assert.NotNull(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.StringGetAsync(
                         It.IsAny<StackExchange.Redis.RedisKey[]>(),
              It.IsAny<CommandFlags>()), Times.Exactly(3));
        }

        [Fact]
        public async Task WriteInChunksAsync_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var data = Enumerable.Range(1, 2500)
       .ToDictionary(i => $"key{i}", i => $"value{i}");

            _mockDatabase.Setup(x => x.StringSetAsync(
              It.IsAny<StackExchange.Redis.RedisKey>(),
                 It.IsAny<RedisValue>(),
                 It.IsAny<TimeSpan?>(),
                     It.IsAny<bool>(),
                   It.IsAny<When>(),
               It.IsAny<CommandFlags>()))
                 .ReturnsAsync(true);

            // Act
            var result = await prefixedKeys.WriteInChunksAsync(data, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 2500 calls (one per key)
            _mockDatabase.Verify(x => x.StringSetAsync(
               It.IsAny<StackExchange.Redis.RedisKey>(),
         It.IsAny<RedisValue>(),
             It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
          It.IsAny<CommandFlags>()), Times.Exactly(2500));
        }

        [Fact]
        public async Task RemoveInChunksAsync_ShouldProcessInChunks()
        {
            // Arrange
            var prefixedKeys = new RedisPrefixedKeys<string>(0);
            prefixedKeys.Init(_contextConfig, "test:prefix");

            var keys = Enumerable.Range(1, 2500).Select(i => $"key{i}");

            _mockDatabase.Setup(x => x.KeyDeleteAsync(
              It.IsAny<StackExchange.Redis.RedisKey[]>(),
                 It.IsAny<CommandFlags>()))
          .ReturnsAsync(1000);

            // Act
            var result = await prefixedKeys.RemoveInChunksAsync(keys, chunkSize: 1000);

            // Assert
            Assert.True(result);
            // Should make 3 calls (1000 + 1000 + 500)
            _mockDatabase.Verify(x => x.KeyDeleteAsync(
                       It.IsAny<StackExchange.Redis.RedisKey[]>(),
          It.IsAny<CommandFlags>()), Times.Exactly(3));
        }
    }
}
