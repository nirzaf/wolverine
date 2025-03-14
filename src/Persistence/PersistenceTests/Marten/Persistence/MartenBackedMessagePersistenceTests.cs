﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Baseline.Dates;
using IntegrationTests;
using Lamar;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TestingSupport;
using Wolverine;
using Wolverine.Marten;
using Wolverine.Persistence.Durability;
using Wolverine.Runtime;
using Wolverine.Transports;
using Xunit;

namespace PersistenceTests.Marten.Persistence;

public class MartenBackedMessagePersistenceTests : PostgresqlContext, IDisposable, IAsyncLifetime
{
    private readonly Envelope theEnvelope;

    private readonly IHost theHost;
    private Envelope persisted;

    public MartenBackedMessagePersistenceTests()
    {
        theHost = WolverineHost.For(opts =>
        {
            opts.Services.AddMarten(x => { x.Connection(Servers.PostgresConnectionString); })
                .IntegrateWithWolverine();
        });


        theEnvelope = ObjectMother.Envelope();
        theEnvelope.Message = new Message1();
        theEnvelope.ScheduledTime = DateTime.Today.ToUniversalTime().AddDays(1);
        theEnvelope.Status = EnvelopeStatus.Scheduled;
        theEnvelope.MessageType = "message1";
        theEnvelope.ContentType = EnvelopeConstants.JsonContentType;
        theEnvelope.ConversationId = Guid.NewGuid();
        theEnvelope.CorrelationId = Guid.NewGuid().ToString();
        theEnvelope.ParentId = Guid.NewGuid().ToString();
    }

    public async Task InitializeAsync()
    {
        var persistence = theHost.Get<IEnvelopePersistence>();

        await persistence.Admin.RebuildAsync();


        persistence.ScheduleJobAsync(theEnvelope).Wait(3.Seconds());

        persisted = (await persistence.Admin
                .AllIncomingAsync())
            .FirstOrDefault(x => x.Id == theEnvelope.Id);
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        theHost.Dispose();
    }

    [Fact]
    public void marten_outbox_is_registered()
    {
        var container = (IContainer)theHost.Services;
        
        container.Model.For<IMartenOutbox>().Default.Lifetime.ShouldBe(ServiceLifetime.Scoped);

        using var nested = container.GetNestedContainer();

        var outbox = nested.GetInstance<IMartenOutbox>();
        var session = nested.GetInstance<IDocumentSession>();
        
        outbox.Session.ShouldBeSameAs(session);
        
        outbox.As<MessageContext>().Transaction.ShouldBeOfType<MartenEnvelopeTransaction>()
            .Session.ShouldBeSameAs(session);
    }

    [Fact]
    public void should_bring_across_correlation_information()
    {
        persisted.CorrelationId.ShouldBe(theEnvelope.CorrelationId);
        persisted.ParentId.ShouldBe(theEnvelope.ParentId);
        persisted.ConversationId.ShouldBe(theEnvelope.ConversationId);
    }

    [Fact]
    public void should_be_in_scheduled_status()
    {
        persisted.Status.ShouldBe(EnvelopeStatus.Scheduled);
    }

    [Fact]
    public void should_be_owned_by_any_node()
    {
        persisted.OwnerId.ShouldBe(TransportConstants.AnyNode);
    }

    [Fact]
    public void should_persist_the_scheduled_envelope()
    {
        persisted.ShouldNotBeNull();
    }
}