using L.EventBus.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace L.EventBus.RabbitMQ.Core;

public static class RabbitMQDependencyInjectionExtensions
{
    public static IEventBusBuilder AddRabbitMqEventBus(this IHostApplicationBuilder builder, string connectionName)
    {
        builder.Services.AddSingleton<IEventBus, RabbitMQEventBus>();
        builder.Services.AddHostedService(p => (RabbitMQEventBus)p.GetRequiredService<IEventBus>());

        return new EventBusBuilder(builder.Services);
    }

    private class EventBusBuilder(IServiceCollection services) : IEventBusBuilder
    {
        public IServiceCollection Services => services;
    }
}
