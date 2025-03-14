using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Lamar;
using Lamar.Microsoft.DependencyInjection;
using LamarCodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ObjectPool;
using Oakton;
using Oakton.Descriptions;
using Oakton.Resources;
using Wolverine.Configuration;
using Wolverine.Logging;
using Wolverine.Persistence.Durability;
using Wolverine.Persistence.Sagas;
using Wolverine.Runtime;
using Wolverine.Runtime.Handlers;

namespace Wolverine;

public static class HostBuilderExtensions
{
    /// <summary>
    ///     Add Wolverine to an ASP.Net Core application with optional configuration to Wolverine
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="overrides">Programmatically configure Wolverine options</param>
    /// <returns></returns>
    public static IHostBuilder UseWolverine(this IHostBuilder builder,
        Action<HostBuilderContext, WolverineOptions>? overrides = null)
    {
        return builder.UseWolverine(new WolverineOptions(), overrides);
    }

    /// <summary>
    ///     Add Wolverine to an ASP.Net Core application with optional configuration to Wolverine
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="overrides">Programmatically configure Wolverine options</param>
    /// <returns></returns>
    public static IHostBuilder UseWolverine(this IHostBuilder builder, Action<WolverineOptions> overrides)
    {
        return builder.UseWolverine((_, r) => overrides(r));
    }

    /// <summary>
    ///     Add Wolverine to an ASP.Net Core application with a pre-built WolverineOptionsBuilder
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="optionsy"></param>
    /// <returns></returns>
    internal static IHostBuilder UseWolverine(this IHostBuilder builder, WolverineOptions options,
        Action<HostBuilderContext, WolverineOptions>? customization = null)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        builder.UseLamar(r => r.Policies.Add(new HandlerScopingPolicy(options.HandlerGraph)));

        builder.ConfigureServices((context, services) =>
        {
            if (services.Any(x => x.ServiceType == typeof(IWolverineRuntime)))
            {
                throw new InvalidOperationException(
                    "IHostBuilder.UseWolverine() can only be called once per service collection");
            }

            services.AddSingleton<IStatefulResource, EnvelopeStorageResource>();

            services.AddSingleton(s => s.GetRequiredService<IContainer>().CreateServiceVariableSource());

            services.AddSingleton(s =>
            {
                var extensions = s.GetServices<IWolverineExtension>();
                foreach (var extension in extensions)
                {
                    extension.Configure(options);
                    options.AppliedExtensions.Add(extension);
                }

                var environment = s.GetService<IHostEnvironment>();
                var directory = environment?.ContentRootPath ?? AppContext.BaseDirectory;

#if DEBUG
                if (directory.EndsWith("Debug", StringComparison.OrdinalIgnoreCase))
                {
                    directory = directory.ParentDirectory()!.ParentDirectory();
                }
                else if (directory.ParentDirectory()!.EndsWith("Debug", StringComparison.OrdinalIgnoreCase))
                {
                    directory = directory.ParentDirectory()!.ParentDirectory()!.ParentDirectory();
                }
#endif

                options.Advanced.CodeGeneration.GeneratedCodeOutputPath = directory!.AppendPath("Internal", "Generated");

                return options;
            });

            services.AddSingleton<IWolverineRuntime, WolverineRuntime>();

            services.AddSingleton(s => (IStatefulResourceSource)s.GetRequiredService<IWolverineRuntime>());

            services.AddSingleton(options.HandlerGraph);
            services.AddSingleton(options.Advanced);

            // The runtime is also a hosted service
            services.AddSingleton(s => (IHostedService)s.GetRequiredService<IWolverineRuntime>());

            services.MessagingRootService(x => x.MessageLogger);

            services.AddSingleton<IDescribedSystemPart>(s => s.GetRequiredService<WolverineOptions>().HandlerGraph);
            services.AddSingleton<IDescribedSystemPart>(s => s.GetRequiredService<WolverineOptions>());

            services.TryAddSingleton<IEnvelopePersistence, NullEnvelopePersistence>();
            services.AddSingleton<InMemorySagaPersistor>();

            services.MessagingRootService(x => x.Pipeline);

            services.AddOptions();
            services.AddLogging();

            services.AddScoped<ICommandBus, CommandBus>();
            services.AddScoped<IMessagePublisher, MessagePublisher>();
            services.AddScoped<IMessageContext, MessageContext>();

            services.AddSingleton<ObjectPoolProvider>(new DefaultObjectPoolProvider());

            // I'm not proud of this code, but you need a non-null
            // Container property to use the codegen
            services.AddSingleton<ICodeFileCollection>(c =>
            {
                var handlers = c.GetRequiredService<HandlerGraph>();
                var container = (IContainer)c;
                handlers.Container = container;

                // Ugly workaround. Leave this be.
                if (handlers.Rules == null)
                {
                    handlers.CompileAsync(container.GetInstance<WolverineOptions>(), container)
#pragma warning disable VSTHRD002
                        .GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
                }

                handlers.Rules ??= c.GetRequiredService<WolverineOptions>().Advanced.CodeGeneration;

                return handlers;
            });

            options.Services.InsertRange(0, services);

            ExtensionLoader.ApplyExtensions(options);

            customization?.Invoke(context, options);

            options.CombineServices(services);
        });

        return builder;
    }

    internal static void MessagingRootService<T>(this IServiceCollection services, Func<IWolverineRuntime, T> expression)
        where T : class
    {
        services.AddSingleton(s => expression(s.GetRequiredService<IWolverineRuntime>()));
    }


    /// <summary>
    ///     Syntactical sugar to execute the Wolverine command line for a configured WebHostBuilder
    /// </summary>
    /// <param name="hostBuilder"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    public static Task<int> RunWolverineAsync(this IHostBuilder hostBuilder, string[] args)
    {
        return hostBuilder.RunOaktonCommands(args);
    }

    public static T Get<T>(this IHost host)
    {
        return host.Services.As<IContainer>().GetInstance<T>();
    }

    public static object Get(this IHost host, Type serviceType)
    {
        return host.Services.GetRequiredService(serviceType);
    }

    /// <summary>
    ///     Syntactical sugar for host.Services.GetRequiredService<IMessagePublisher>().Send(message)
    /// </summary>
    /// <param name="host"></param>
    /// <param name="message"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static ValueTask SendAsync<T>(this IHost host, T message, DeliveryOptions? options = null)
    {
        return host.Get<IMessagePublisher>().SendAsync(message, options);
    }

    /// <summary>
    ///     Syntactical sugar for host.Services.GetRequiredService<IMessagePublisher>().Send(message)
    /// </summary>
    /// <param name="host"></param>
    /// <param name="endpointName"></param>
    /// <param name="message"></param>
    /// <param name="options"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static ValueTask SendToEndpointAsync<T>(this IHost host, string endpointName, T message,
        DeliveryOptions? options = null)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        return host.Get<IMessagePublisher>().SendToEndpointAsync(endpointName, message, options);
    }

    /// <summary>
    /// Syntactical sugar to invoke a single message with the registered
    /// Wolverine command bus for this host
    /// </summary>
    /// <param name="host"></param>
    /// <param name="command"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static Task InvokeAsync<T>(this IHost host, T command)
    {
        return host.Get<ICommandBus>().InvokeAsync(command!);
    }

}
