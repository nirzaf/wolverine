using System;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Wolverine.ErrorHandling;

namespace Wolverine.Runtime.Handlers;

internal class NoHandlerExecutor : IExecutor
{
    private readonly Type _messageType;
    private readonly IContinuation _continuation;

    public NoHandlerExecutor(Type messageType, WolverineRuntime runtime)
    {
        _messageType = messageType;
        var handlers = runtime.MissingHandlers();
        _continuation = new NoHandlerContinuation(handlers, runtime);
    }

    public Task<IContinuation> ExecuteAsync(MessageContext context, CancellationToken cancellation)
    {
        return Task.FromResult(_continuation);
    }

    public Task<InvokeResult> InvokeAsync(MessageContext context, CancellationToken cancellation)
    {
        throw new NotSupportedException($"No known handler for message type {_messageType.FullNameInCode()}");
    }
}
