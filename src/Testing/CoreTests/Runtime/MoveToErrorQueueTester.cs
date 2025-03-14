﻿using System;
using System.Threading.Tasks;
using CoreTests.Messaging;
using NSubstitute;
using Wolverine;
using Wolverine.ErrorHandling;
using Xunit;

namespace CoreTests.Runtime;

public class MoveToErrorQueueTester
{
    private readonly IEnvelopeLifecycle theLifecycle = Substitute.For<IEnvelopeLifecycle>();
    private readonly MoveToErrorQueue theContinuation;
    private readonly Envelope theEnvelope = ObjectMother.Envelope();

    private readonly Exception theException = new DivideByZeroException();
    private readonly MockWolverineRuntime theRuntime = new();

    public MoveToErrorQueueTester()
    {
        theContinuation = new MoveToErrorQueue(theException);
        theLifecycle.Envelope.Returns(theEnvelope);
    }

    [Fact]
    public async Task should_send_a_failure_ack()
    {
        await theContinuation.ExecuteAsync(theLifecycle, theRuntime, DateTimeOffset.Now);

        await theLifecycle
                .Received()
                .SendFailureAcknowledgementAsync($"Moved message {theEnvelope.Id} to the Error Queue.\n{theException}")
            ;
    }

    [Fact]
    public async Task logging_calls()
    {
        await theContinuation.ExecuteAsync(theLifecycle, theRuntime, DateTimeOffset.Now);

        theRuntime.MessageLogger.Received().MessageFailed(theEnvelope, theException);
        theRuntime.MessageLogger.Received().MovedToErrorQueue(theEnvelope, theException);
    }
}
