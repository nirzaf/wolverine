using System;
using System.Threading.Tasks;
using Baseline.Dates;
using IntegrationTests;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Oakton.Resources;
using PersistenceTests.Marten;
using Shouldly;
using TestingSupport;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Marten;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime.Handlers;
using Xunit;

namespace PersistenceTests;

public class durability_with_local : PostgresqlContext
{
    [Fact]
    public async Task should_recover_persisted_messages()
    {
        using (var host1 = WolverineHost.For(opts => opts.ConfigureDurableSender(true, true)))
        {
            await host1.SendAsync(new ReceivedMessage());

            var counts = await host1.Get<IEnvelopePersistence>().Admin.FetchCountsAsync();

            await host1.StopAsync();

            counts.Incoming.ShouldBe(1);
        }

        // Don't use WolverineHost here because you need the existing persisted state!!!!
        using (var host1 = Host.CreateDefaultBuilder().UseWolverine(opts => opts.ConfigureDurableSender(true, false))
                   .Start())
        {
            var counts = await host1.Get<IEnvelopePersistence>().Admin.FetchCountsAsync();

            var i = 0;
            while (counts.Incoming != 1 && i < 10)
            {
                await Task.Delay(100.Milliseconds());
                counts = await host1.Get<IEnvelopePersistence>().Admin.FetchCountsAsync();
                i++;
            }

            counts.Incoming.ShouldBe(1);


            await host1.StopAsync();
        }
    }
}

public static class DurableOptionsConfiguration
{
    public static void ConfigureDurableSender(this WolverineOptions opts, bool latched, bool initial)
    {
        if (initial)
        {
            opts.Services.AddResourceSetupOnStartup(StartupAction.ResetState);
        }

        opts.PublishAllMessages()
            .ToLocalQueue("one")
            .UseDurableInbox();

        opts.Services.AddMarten(Servers.PostgresConnectionString)
            .IntegrateWithWolverine();

        opts.Services.AddSingleton(new ReceivingSettings { Latched = latched });
    }
}

public class ReceivingSettings
{
    public bool Latched { get; set; } = true;
}

public class ReceivedMessageHandler
{
    public void Handle(ReceivedMessage message, Envelope envelope, ReceivingSettings settings)
    {
        if (settings.Latched)
        {
            throw new DivideByZeroException();
        }
    }

    public static void Configure(HandlerChain chain)
    {
        chain.OnException(e => true).Requeue(1000);
    }
}

public class ReceivedMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
}