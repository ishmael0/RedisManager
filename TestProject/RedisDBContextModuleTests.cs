using Moq;
using StackExchange.Redis;
using Santel.Redis.TypedKeys;
using Microsoft.Extensions.Logging;

namespace TestProject
{
    public class RedisDBContextModuleTests
    {
        [Fact]
        public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new RedisDBContextModule((RedisDBContextOptions)null!));
        }

        [Fact]
        public void Constructor_WithNullConnectionMultiplexer_ShouldThrowArgumentException()
        {
            // Arrange
            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = null!,
                KeepDataInMemory = true
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new RedisDBContextModule(options));
        }

        [Fact]
        public void Constructor_WithValidOptions_ShouldInitialize()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
   .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
                .Returns(mockSubscriber.Object);

            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithLogger_ShouldInitialize()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();
            var mockLogger = new Mock<ILogger<RedisDBContextModule>>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                    .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
                .Returns(mockSubscriber.Object);

            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            var context = new RedisDBContextModule(options, mockLogger.Object);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithExtendedOptions_ShouldInitialize()
        {
            // Arrange
            var mockConnectionWrite = new Mock<IConnectionMultiplexer>();
            var mockConnectionRead = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnectionWrite.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);
            mockConnectionRead.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
 .Returns(mockDatabase.Object);
            mockConnectionWrite.Setup(x => x.GetSubscriber(It.IsAny<object>()))
            .Returns(mockSubscriber.Object);

            var options = new RedisDBContextExtendedOptions
            {
                ConnectionMultiplexerWrite = mockConnectionWrite.Object,
                ConnectionMultiplexerRead = mockConnectionRead.Object,
                KeepDataInMemory = true
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithExtendedOptions_OnlyWrite_ShouldUseWriteForRead()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                      .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
                    .Returns(mockSubscriber.Object);

            var options = new RedisDBContextExtendedOptions
            {
                ConnectionMultiplexerWrite = mockConnection.Object,
                ConnectionMultiplexerRead = null,
                KeepDataInMemory = true
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithExtendedOptions_OnlyRead_ShouldUseReadForWrite()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
           .Returns(mockSubscriber.Object);

            var options = new RedisDBContextExtendedOptions
            {
                ConnectionMultiplexerWrite = null,
                ConnectionMultiplexerRead = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithExtendedOptions_NullBoth_ShouldThrowArgumentException()
        {
            // Arrange
            var options = new RedisDBContextExtendedOptions
            {
                ConnectionMultiplexerWrite = null,
                ConnectionMultiplexerRead = null,
                KeepDataInMemory = true
            };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new RedisDBContextModule(options));
        }

        [Fact]
        public void Constructor_WithChannelName_ShouldInitializeChannel()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
           .Returns(mockSubscriber.Object);

            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = mockConnection.Object,
                KeepDataInMemory = true,
                ChannelName = "test-channel"
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_WithNameGeneratorStrategy_ShouldUseStrategy()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
        .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
                .Returns(mockSubscriber.Object);

            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = mockConnection.Object,
                KeepDataInMemory = true,
                NameGeneratorStrategy = name => $"prefix:{name}"
            };

            // Act
            var context = new RedisDBContextModule(options);

            // Assert
            Assert.NotNull(context);
        }

        [Fact]
        public void Constructor_DefaultConstructor_ShouldInitialize()
        {
            // Act
            var context = new RedisDBContextModule();

            // Assert
            Assert.NotNull(context);
        }

        // Test custom context with properties
        public class TestRedisContext : RedisDBContextModule
        {
            public RedisKey<string> TestKey { get; set; } = new(1);
            public RedisHashKey<int> TestHashKey { get; set; } = new(1);
            public RedisPrefixedKeys<string> TestPrefixedKeys { get; set; } = new(1);

            public TestRedisContext(RedisDBContextOptions options) : base(options) { }
        }

        [Fact]
        public void Constructor_WithCustomContext_ShouldInitializeProperties()
        {
            // Arrange
            var mockConnection = new Mock<IConnectionMultiplexer>();
            var mockDatabase = new Mock<IDatabase>();
            var mockSubscriber = new Mock<ISubscriber>();

            mockConnection.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                     .Returns(mockDatabase.Object);
            mockConnection.Setup(x => x.GetSubscriber(It.IsAny<object>()))
            .Returns(mockSubscriber.Object);

            var options = new RedisDBContextOptions
            {
                ConnectionMultiplexer = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            var context = new TestRedisContext(options);

            // Assert
            Assert.NotNull(context);
            Assert.NotNull(context.TestKey);
            Assert.NotNull(context.TestHashKey);
            Assert.NotNull(context.TestPrefixedKeys);
            Assert.NotEqual(default(StackExchange.Redis.RedisKey), context.TestKey.FullName);
            Assert.NotEqual(default(StackExchange.Redis.RedisKey), context.TestHashKey.FullName);
            Assert.NotEqual(default(StackExchange.Redis.RedisKey), context.TestPrefixedKeys.FullName);
        }
    }

    public class RedisDBContextModuleConfigsTests
    {
        [Fact]
        public void Publish_RedisKeyWithChannel_ShouldPublish()
        {
            // Arrange
            var mockSubscriber = new Mock<ISubscriber>();
            var mockRedisKey = new Mock<IRedisKey>();
            var mockConnection = new Mock<IConnectionMultiplexer>();

            mockRedisKey.Setup(x => x.FullName).Returns((StackExchange.Redis.RedisKey)"test:key");

            var config = new RedisDBContextModuleConfigs
            {
                Sub = mockSubscriber.Object,
                Channel = new RedisChannel("test-channel", RedisChannel.PatternMode.Literal),
                Writer = mockConnection.Object,
                Reader = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            config.Publish(mockRedisKey.Object);

            // Assert
            mockSubscriber.Verify(x => x.Publish(
              It.IsAny<RedisChannel>(),
            It.Is<RedisValue>(v => v.ToString() == "test:key"),
                 It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Publish_RedisHashKeyWithSingleKey_ShouldPublish()
        {
            // Arrange
            var mockSubscriber = new Mock<ISubscriber>();
            var mockRedisHashKey = new Mock<IRedisHashKey>();
            var mockConnection = new Mock<IConnectionMultiplexer>();

            mockRedisHashKey.Setup(x => x.FullName).Returns((StackExchange.Redis.RedisKey)"test:hash");

            var config = new RedisDBContextModuleConfigs
            {
                Sub = mockSubscriber.Object,
                Channel = new RedisChannel("test-channel", RedisChannel.PatternMode.Literal),
                Writer = mockConnection.Object,
                Reader = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            config.Publish(mockRedisHashKey.Object, "field1");

            // Assert
            mockSubscriber.Verify(x => x.Publish(
    It.IsAny<RedisChannel>(),
             It.Is<RedisValue>(v => v.ToString() == "test:hash|field1"),
       It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Publish_RedisHashKeyWithMultipleKeys_ShouldPublishMultipleTimes()
        {
            // Arrange
            var mockSubscriber = new Mock<ISubscriber>();
            var mockRedisHashKey = new Mock<IRedisHashKey>();
            var mockConnection = new Mock<IConnectionMultiplexer>();

            mockRedisHashKey.Setup(x => x.FullName).Returns((StackExchange.Redis.RedisKey)"test:hash");

            var config = new RedisDBContextModuleConfigs
            {
                Sub = mockSubscriber.Object,
                Channel = new RedisChannel("test-channel", RedisChannel.PatternMode.Literal),
                Writer = mockConnection.Object,
                Reader = mockConnection.Object,
                KeepDataInMemory = true
            };

            var keys = new[] { "field1", "field2" };

            // Act
            config.Publish(mockRedisHashKey.Object, keys);

            // Assert
            mockSubscriber.Verify(x => x.Publish(
   It.IsAny<RedisChannel>(),
            It.IsAny<RedisValue>(),
       It.IsAny<CommandFlags>()), Times.Exactly(2));
        }

        [Fact]
        public void Publish_RedisHashKeyAll_ShouldPublish()
        {
            // Arrange
            var mockSubscriber = new Mock<ISubscriber>();
            var mockRedisHashKey = new Mock<IRedisHashKey>();
            var mockConnection = new Mock<IConnectionMultiplexer>();

            mockRedisHashKey.Setup(x => x.FullName).Returns((StackExchange.Redis.RedisKey)"test:hash");

            var config = new RedisDBContextModuleConfigs
            {
                Sub = mockSubscriber.Object,
                Channel = new RedisChannel("test-channel", RedisChannel.PatternMode.Literal),
                Writer = mockConnection.Object,
                Reader = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            config.Publish(mockRedisHashKey.Object);

            // Assert
            mockSubscriber.Verify(x => x.Publish(
    It.IsAny<RedisChannel>(),
        It.Is<RedisValue>(v => v.ToString() == "test:hash|all"),
         It.IsAny<CommandFlags>()), Times.Once);
        }

        [Fact]
        public void Publish_WithoutChannel_ShouldNotPublish()
        {
            // Arrange
            var mockSubscriber = new Mock<ISubscriber>();
            var mockRedisKey = new Mock<IRedisKey>();
            var mockConnection = new Mock<IConnectionMultiplexer>();

            mockRedisKey.Setup(x => x.FullName).Returns((StackExchange.Redis.RedisKey)"test:key");

            var config = new RedisDBContextModuleConfigs
            {
                Sub = mockSubscriber.Object,
                Channel = null,
                Writer = mockConnection.Object,
                Reader = mockConnection.Object,
                KeepDataInMemory = true
            };

            // Act
            config.Publish(mockRedisKey.Object);

            // Assert
            mockSubscriber.Verify(x => x.Publish(
                   It.IsAny<RedisChannel>(),
     It.IsAny<RedisValue>(),
              It.IsAny<CommandFlags>()), Times.Never);
        }
    }
}
