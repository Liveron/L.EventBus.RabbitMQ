using L.EventBus.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace L.EventBus.RabbitMQ.Core.Tests.RabbitMQDependencyInjectionExtensions;

public class AddRabbitMqEventBus
{
    [Fact]
    public void ShouldRegisterIEventBusAsRabbitMQEventBus()
    {
        // Arrange
        var hostApplicationBuilder = new HostApplicationBuilder();

        // Act
        hostApplicationBuilder.AddRabbitMqEventBus("testConnection");
        
        // Assert
        var serviceProvider = hostApplicationBuilder.Services.BuildServiceProvider();
        var eventBus = serviceProvider.GetService<IEventBus>();

        Assert.IsType<RabbitMQEventBus>(eventBus);
    }

    [Fact]
    public void ShouldRegisterRabbitMQEventBusAsHostedService()
    {
        // Arrange
        var hostApplicationBuilder = new HostApplicationBuilder();

        // Act
        hostApplicationBuilder.AddRabbitMqEventBus("testConnection");
        
        // Assert
        var serviceProvider = hostApplicationBuilder.Services.BuildServiceProvider();
        var hostedServices = serviceProvider.GetServices<IHostedService>();

        Assert.Contains(hostedServices, service => service is RabbitMQEventBus);
    }

    [Fact]
    public void ShouldReturnEventBusBuilder()
    {
        // Arrange
        var hostApplicationBuilder = new HostApplicationBuilder();

        // Act
        var eventBusBuilder = hostApplicationBuilder.AddRabbitMqEventBus("testConnection");

        //Assert
        Assert.IsType<IEventBusBuilder>(eventBusBuilder, exactMatch: false);
    }
}
