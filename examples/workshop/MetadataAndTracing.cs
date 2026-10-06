using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Troolio.Core;

namespace Workshop;

public static class MessageContext
{
    // IDs supplied by an authenticated application boundary, not trusted from arbitrary payloads.
    public static Metadata AtIngress(Guid correlationId, Guid userId, Guid deviceId) =>
        new(correlationId, userId, deviceId);

    // For application-owned messages outside the actor dispatch protocol.
    public static Metadata Child(Metadata parent) => parent with
    {
        MessageId = Guid.NewGuid(),
        CausationId = parent.MessageId
    };

    // A later job starts new execution context and retains an explicit causal link.
    public static Metadata Scheduled(Metadata trigger, Guid currentUserId) =>
        new(Guid.NewGuid(), currentUserId, Guid.Empty)
        {
            CausationId = trigger.MessageId,
            TransactionId = null
        };
}

public static class WorkflowTelemetry
{
    public static readonly ActivitySource Source = new("Workshop.Workflows");

    public static async Task ExecuteAsync(ILogger logger, Metadata headers, Func<Task> work)
    {
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = headers.CorrelationId,
            ["MessageId"] = headers.MessageId,
            ["CausationId"] = headers.CausationId,
            ["TransactionId"] = headers.TransactionId
        });
        using Activity? activity = Source.StartActivity("registration.process", ActivityKind.Internal);
        activity?.SetTag("troolio.correlation_id", headers.CorrelationId.ToString("D"));
        try
        {
            await work();
            logger.LogInformation("Registration processing completed");
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.GetType().Name);
            logger.LogError(error, "Registration processing failed");
            throw;
        }
    }
}
