﻿using System;
using System.Threading.Tasks;
using Wolverine.Runtime;

namespace Wolverine.ErrorHandling;

internal class ScheduledRetryContinuation : IContinuation, IContinuationSource
{
    public ScheduledRetryContinuation(TimeSpan delay)
    {
        _delay = delay;
    }

    private readonly TimeSpan _delay;

    public TimeSpan Delay => _delay;

    public async ValueTask ExecuteAsync(IEnvelopeLifecycle lifecycle, IWolverineRuntime runtime, DateTimeOffset now)
    {
        var scheduledTime = now.Add(_delay);

        await lifecycle.ReScheduleAsync(scheduledTime);
    }

    public override string ToString()
    {
        return $"Schedule Retry in {_delay.TotalSeconds} seconds";
    }

    protected bool Equals(ScheduledRetryContinuation other)
    {
        return _delay.Equals(other._delay);
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((ScheduledRetryContinuation)obj);
    }

    public override int GetHashCode()
    {
        return _delay.GetHashCode();
    }

    public string Description => ToString();
    public IContinuation Build(Exception ex, Envelope envelope)
    {
        return this;
    }
}
