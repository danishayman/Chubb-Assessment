using Claims.Api.Messaging.Handlers;

namespace Claims.Api.Messaging;

public static class MessagingSetup
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("Messaging").Get<MessagingOptions>() ?? new MessagingOptions();
        services.AddSingleton(options);
        services.AddSingleton<EventDispatcher>();
        services.AddHostedService<OutboxDispatcher>();

        if (options.Provider.Equals("Kafka", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEventPublisher, KafkaPublisher>();
            services.AddHostedService<KafkaConsumer>();
        }
        else
        {
            services.AddSingleton<InMemoryBus>();
            services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<InMemoryBus>());
            services.AddHostedService<InMemoryConsumer>();
        }

        services.AddScoped<IEventHandler, NotificationProjector>();
        services.AddScoped<IEventHandler, PaymentHandoff>();
        return services;
    }
}
