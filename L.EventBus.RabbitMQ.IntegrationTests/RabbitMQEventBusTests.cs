using L.EventBus.Core.Abstractions;
using L.EventBus.RabbitMQ.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using L.EventBus.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Moq;

namespace L.EventBus.RabbitMQ.IntegrationTests;

public class RabbitMQEventBusTests : IDisposable, IAsyncDisposable
{
    private readonly Mock<IOptions<EventBusOptions>> _optionsMock;
    private readonly Mock<IOptions<EventBusSubscriptionInfo>> _subscriptionInfoMock;
    private readonly FakeLogger<RabbitMQEventBus> _fakeLogger;
    private readonly ServiceProvider _serviceProvider;

    public RabbitMQEventBusTests()
    {
        var factory = new ConnectionFactory { HostName = "localhost" };
        IConnection connection = factory.CreateConnectionAsync().GetAwaiter().GetResult();
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(connection);
        _serviceProvider = serviceCollection.BuildServiceProvider();
        
        _optionsMock = new Mock<IOptions<EventBusOptions>>();
        _subscriptionInfoMock = new Mock<IOptions<EventBusSubscriptionInfo>>();
        _fakeLogger = new FakeLogger<RabbitMQEventBus>();
    }


    [Fact]
    public async Task PublishAsync_ShouldPublishEventToRabbitMQ()
    {
        // Arrange
        var eventBusOptions = new EventBusOptions { SubscriptionClientName = "test_queue" };
        _optionsMock.Setup(x => x.Value).Returns(eventBusOptions);

        var eventBusSubscriptionInfo = new EventBusSubscriptionInfo();
        eventBusSubscriptionInfo.EventTypes.Add(
            nameof(TestIntegrationEvent), typeof(TestIntegrationEvent));
        _subscriptionInfoMock.Setup(x => x.Value).Returns(eventBusSubscriptionInfo);

        var rabbitMQEventBus = new RabbitMQEventBus(
            _fakeLogger,
            _serviceProvider,
            _optionsMock.Object,
            _subscriptionInfoMock.Object);

        await rabbitMQEventBus.StartAsync(CancellationToken.None);

        // Act
        await rabbitMQEventBus.PublishAsync(new TestIntegrationEvent());

        await Task.Delay(1000);

        await rabbitMQEventBus.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(LogLevel.Trace, _fakeLogger.Collector.LatestRecord.Level);
        Assert.Equal($"Processing RabbitMQ event: {nameof(TestIntegrationEvent)}",
            _fakeLogger.Collector.LatestRecord.Message);
    }

    private record TestIntegrationEvent : IntegrationEvent { }

    public void Dispose() => _serviceProvider.Dispose();
    public async ValueTask DisposeAsync() => await _serviceProvider.DisposeAsync();
}
