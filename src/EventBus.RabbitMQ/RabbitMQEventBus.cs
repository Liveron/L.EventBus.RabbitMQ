using L.EventBus.Core.Abstractions;
using L.EventBus.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace L.EventBus.RabbitMQ.Core;

public class RabbitMQEventBus(
    ILogger<RabbitMQEventBus> logger,
    IServiceProvider serviceProvider,
    IOptions<EventBusOptions> options,
    IOptions<EventBusSubscriptionInfo> subscriptionInfo)
    : BackgroundService, IEventBus
{
    private const string EXCHANGE_NAME = "heritage_event_bus";

    private readonly ILogger<RabbitMQEventBus> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly string _queueName = options.Value.SubscriptionClientName;
    private readonly EventBusSubscriptionInfo _subscriptionInfo = subscriptionInfo.Value;

    private IConnection? _rabbitMQConnection;
    private IChannel _consumerChannel = null!;

    private readonly bool _traceEnabled = logger.IsEnabled(LogLevel.Trace);

    public async Task PublishAsync(IntegrationEvent @event)
    {
        string routingKey = @event.GetType().Name;

        if (_traceEnabled)
            _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        if (_rabbitMQConnection is null)
            throw new InvalidOperationException("RabbitMQ connection is not open");

        using IChannel publisherChannel = await _rabbitMQConnection.CreateChannelAsync();

        if (_traceEnabled)
            _logger.LogTrace("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        await publisherChannel.ExchangeDeclareAsync(exchange: EXCHANGE_NAME, type: "direct");

        byte[] body = SerializeMessage(@event);

        var properties = new BasicProperties { Persistent = true };

        if (_traceEnabled)
            _logger.LogTrace("Publishing event to RabbitMQ: {EventId}", @event.Id);

        try
        {
            await publisherChannel.BasicPublishAsync(
                exchange: EXCHANGE_NAME,
                routingKey: routingKey,
                mandatory: true,
                basicProperties: properties,
                body: body);
        }
        catch (Exception) { throw; }
    }

    protected override async Task ExecuteAsync(CancellationToken _)
    {
        try
        {
            _logger.LogInformation("Starting RabbitMQ connection on a background thread");

            _rabbitMQConnection = _serviceProvider.GetRequiredService<IConnection>();
            if (!_rabbitMQConnection.IsOpen)
                return;

            if (_traceEnabled)
                _logger.LogTrace("Creating RabbitMQ consumer channel");

            _consumerChannel = await _rabbitMQConnection.CreateChannelAsync();

            _consumerChannel.CallbackExceptionAsync += (_, e) =>
            {
                _logger.LogWarning(e.Exception, "Error with RabbitMQ consumer channel");
                return Task.CompletedTask;
            };

            await _consumerChannel.ExchangeDeclareAsync(
                exchange: EXCHANGE_NAME, type: "direct");

            await _consumerChannel.QueueDeclareAsync(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false);

            if (_traceEnabled)
                _logger.LogTrace("Starting RabbitMQ basic consume");

            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.ReceivedAsync += OnMessageReceived;

            await _consumerChannel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: false,
                consumer: consumer);

            foreach (var (eventName, _) in _subscriptionInfo.EventTypes)
            {
                await _consumerChannel.QueueBindAsync(
                    queue: _queueName,
                    exchange: EXCHANGE_NAME,
                    routingKey: eventName); 
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting RabbitMQ connecion");
        }
    }

    private async Task OnMessageReceived(object sender, BasicDeliverEventArgs e)
    {
        string eventName = e.RoutingKey;
        string message = Encoding.UTF8.GetString(e.Body.Span);

        try
        {
            await ProcessEvent(eventName, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error Processing message \"{Message}\"", message);
        }

        await _consumerChannel.BasicAckAsync(e.DeliveryTag, multiple: false);
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        if (_traceEnabled)
            _logger.LogTrace("Processing RabbitMQ event: {EventName}", eventName);

        await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();

        if (!_subscriptionInfo.EventTypes.TryGetValue(eventName, out Type? eventType))
        {
            _logger.LogWarning("Unable to resolve event type for event name {EventName}", eventName);
            return;
        }

        IntegrationEvent? integrationEvent = DeserializeMessage(message, eventType);

        if (integrationEvent is null)
        {
            _logger.LogWarning("Unable to deserialize event message: {EventType} \"{Message}\"", message, eventType);
            return;
        }

        foreach (var handler in scope.ServiceProvider.GetKeyedServices<IIntegrationEventHandler>(eventType))
            await handler.Handle(integrationEvent);
    }

    private static byte[] SerializeMessage(IntegrationEvent @event) =>
        JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

    private static IntegrationEvent? DeserializeMessage(string message, Type eventType) =>
        JsonSerializer.Deserialize(message, eventType) as IntegrationEvent;
}
