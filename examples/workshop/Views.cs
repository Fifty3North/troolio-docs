using System.Security.Cryptography;
using System.Text.Json;
using Troolio.Core;
using Troolio.Core.ReadModels;
using Troolio.Stores;

namespace Workshop;

public sealed record RegistrationCard(Guid RegistrationId, Guid WorkshopId,
    Guid AttendeeId, string DisplayName, string Status);

public sealed record ProjectionSource(string Consumer, string Stream, ulong Version, string DomainHash);
public enum ApplyOutcome { Applied, Duplicate, OlderSnapshot }

public interface IRegistrationCardWriter
{
    Task<ApplyOutcome> ApplyAsync(RegistrationCard card, ProjectionSource source,
        CancellationToken cancellationToken);
}

public static class RegistrationCards
{
    public static RegistrationCard From(RegistrationRequested e) =>
        new(e.RegistrationId, e.WorkshopId, e.AttendeeId, e.DisplayName, "requested");

    public static RegistrationCard From(RegistrationCancelled e) =>
        new(e.RegistrationId, e.WorkshopId, e.AttendeeId, e.DisplayName, "cancelled");

    public static ProjectionSource Source(IEventEnvelope envelope, RegistrationCard card)
    {
        if (!Guid.TryParse(envelope.Id, out Guid actorId) || actorId != card.RegistrationId)
            throw new InvalidDataException("Unexpected registration source actor.");
        // Domain-only comparison: diagnostic header rewrites must not change deduplication.
        string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(card)));
        return new("registration-cards-v1", envelope.Stream, envelope.Version, hash);
    }
}

[ProjectionStreamSubscription(typeof(RegistrationActor))]
public sealed class RegistrationCardsProjection(IRegistrationCardWriter writer) : ProjectionActor
{
    public Task On(EventEnvelope<RegistrationRequested> envelope)
    {
        RegistrationCard card = RegistrationCards.From(envelope.Event);
        return writer.ApplyAsync(card, RegistrationCards.Source(envelope, card), CancellationToken.None);
    }

    public Task On(EventEnvelope<RegistrationCancelled> envelope)
    {
        RegistrationCard card = RegistrationCards.From(envelope.Event);
        return writer.ApplyAsync(card, RegistrationCards.Source(envelope, card), CancellationToken.None);
    }
}

public sealed record RegistrationDetails(Guid Id) : TroolioReadModel
{
    public Guid OwnerId { get; init; }
    public Guid WorkshopId { get; init; }
    public string DisplayName { get; init; } = "";
    public string Status { get; init; } = "absent";

    public RegistrationDetails On(EventEnvelope<RegistrationRequested> envelope) =>
        this with { OwnerId = envelope.Event.AttendeeId, WorkshopId = envelope.Event.WorkshopId,
            DisplayName = envelope.Event.DisplayName, Status = "requested" };

    public RegistrationDetails On(EventEnvelope<RegistrationCancelled> _) =>
        this with { Status = "cancelled" };

    public override Func<Metadata, bool> Authorized =>
        headers => OwnerId != Guid.Empty && headers.UserId == OwnerId;
}

// Optional linked-event view. Its host must provide ordered, deduplicated source delivery.
[ProjectionStreamSubscription(typeof(RegistrationActor))]
public sealed class RegistrationDetailsLinks(IStore store) : ReadModelProjectionActor<RegistrationDetails>(store)
{
    public string Handle(EventEnvelope<RegistrationRequested> envelope) =>
        envelope.Event.RegistrationId.ToString("D");

    public string Handle(EventEnvelope<RegistrationCancelled> envelope) =>
        envelope.Event.RegistrationId.ToString("D");
}
