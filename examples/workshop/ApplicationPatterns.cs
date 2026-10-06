using System.Security.Cryptography;
using System.Text.Json;
using Troolio.Core;
using Troolio.Core.Client;
using Troolio.Core.Transactions;
using Troolio.Stores;

namespace Workshop;

public static class RegistrationCalls
{
    public static Task RequestAsync(ITroolioClient client, Guid registrationId, Guid workshopId,
        string displayName, Guid authenticatedUserId, Guid verifiedDeviceId, Guid correlationId)
    {
        Metadata headers = MessageContext.AtIngress(correlationId, authenticatedUserId, verifiedDeviceId);
        return client.Tell<IRegistrationActor>(registrationId.ToString("D"),
            new RequestRegistration(headers, registrationId, workshopId, displayName));
    }

    public static async Task<RegistrationDetails> ReadDetailsAsync(ITroolioClient client,
        Guid registrationId, Metadata verifiedCaller)
    {
        RegistrationDetails details = await client.Get<RegistrationDetails>(registrationId.ToString("D"));
        if (!details.Authorized(verifiedCaller)) throw new UnauthorizedAccessException();
        return details;
    }
}

public sealed record RegistrationChangedHint(Guid RegistrationId);

public static class RegistrationRefresh
{
    public static Task<RegistrationCard?> AfterHintAsync(RegistrationChangedHint hint,
        Guid authenticatedUserId, SqlRegistrationCards reader, CancellationToken cancellationToken) =>
        reader.GetForUserAsync(hint.RegistrationId, authenticatedUserId, cancellationToken);
}

public sealed record RepairItem(RegistrationCard Card, ProjectionSource Source);
public sealed record RepairPage(IReadOnlyList<RepairItem> Items, string NextCursor);

// Application contracts: implementations own verified source reads and durable progress.
public interface IRepairFeed
{
    Task<RepairPage> ReadAsync(string partition, string? after, int maximumItems,
        CancellationToken cancellationToken);
}

public interface IRepairProgress
{
    Task<string?> LoadAsync(string partition, CancellationToken cancellationToken);
    Task<bool> AdvanceAsync(string partition, string? expectedCursor, string nextCursor,
        CancellationToken cancellationToken);
}

public static class ProjectionRepair
{
    public static async Task ProcessPageAsync(string partition, IRepairFeed feed,
        IRegistrationCardWriter writer, IRepairProgress progress, CancellationToken cancellationToken)
    {
        string? cursor = await progress.LoadAsync(partition, cancellationToken);
        RepairPage page = await feed.ReadAsync(partition, cursor, 100, cancellationToken);
        if (page.Items.Count > 100) throw new InvalidDataException("Repair page exceeded its bound.");
        foreach (RepairItem item in page.Items)
            await writer.ApplyAsync(item.Card, item.Source, cancellationToken);
        // A crash before this point repeats the page; the writer deduplicates source records.
        if (!await progress.AdvanceAsync(partition, cursor, page.NextCursor, cancellationToken))
            throw new InvalidOperationException("Another worker advanced this repair partition.");
    }
}

public static class NotificationIdentity
{
    public static string ForRecipient(ProjectionSource source, Guid recipientId)
    {
        string[] fields = ["registration-notification-v1", source.Stream,
            source.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), recipientId.ToString("D")];
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fields)));
    }
}

public static class ConfirmationEffects
{
    public static Task<ExternalEffectIntent> RegisterAsync(IExternalEffectIntentStore store,
        IIdempotentExternalEffectProvider provider, AtomicEventTransactionReceipt receipt, byte[] payload) =>
        ExternalEffectDispatcher.RegisterAsync(store, provider, receipt,
            "registration-confirmation", "confirmation-worker", payload);
}
