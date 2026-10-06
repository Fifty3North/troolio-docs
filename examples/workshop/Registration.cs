using Microsoft.Extensions.Configuration;
using Orleans;
using Troolio.Core;
using Troolio.Core.State;
using Troolio.Stores;

namespace Workshop;

public interface IRegistrationActor : IActor { }

[GenerateSerializer]
public record RequestRegistration(Metadata Headers, Guid RegistrationId,
    Guid WorkshopId, string DisplayName) : Command<IRegistrationActor>(Headers);

[GenerateSerializer]
public record CancelRegistration(Metadata Headers) : Command<IRegistrationActor>(Headers);

[GenerateSerializer]
public record GetRegistration : Query<IRegistrationActor, RegistrationState>;

[GenerateSerializer]
public record RegistrationRequested(Metadata Headers, Guid RegistrationId,
    Guid WorkshopId, Guid AttendeeId, string DisplayName) : Event(Headers);

[GenerateSerializer]
public record RegistrationCancelled(Metadata Headers, Guid RegistrationId,
    Guid WorkshopId, Guid AttendeeId, string DisplayName) : Event(Headers);

[GenerateSerializer]
public record RegistrationState(Guid RegistrationId, Guid WorkshopId,
    Guid AttendeeId, string DisplayName, string Status) : IActorState
{
    public static RegistrationState Empty => new(Guid.Empty, Guid.Empty,
        Guid.Empty, "", "absent");

    public RegistrationState Apply(RegistrationRequested e) =>
        new(e.RegistrationId, e.WorkshopId, e.AttendeeId, e.DisplayName, "requested");

    public RegistrationState Apply(RegistrationCancelled e) =>
        new(e.RegistrationId, e.WorkshopId, e.AttendeeId, e.DisplayName, "cancelled");
}

public static class RegistrationRules
{
    public static IEnumerable<Event> Request(RegistrationState state, RequestRegistration command)
    {
        if (command.RegistrationId == Guid.Empty || command.WorkshopId == Guid.Empty
            || command.Headers.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.DisplayName)
            || command.DisplayName.Length > 120)
            throw new ArgumentException("Registration details are incomplete.");
        if (state.Status != "absent")
        {
            if (state.Status == "requested" && state.RegistrationId == command.RegistrationId
                && state.WorkshopId == command.WorkshopId && state.AttendeeId == command.Headers.UserId
                && state.DisplayName == command.DisplayName)
                return [];
            throw new InvalidOperationException("This registration already has a different outcome.");
        }
        return [new RegistrationRequested(command.Headers, command.RegistrationId,
            command.WorkshopId, command.Headers.UserId, command.DisplayName)];
    }

    public static IEnumerable<Event> Cancel(RegistrationState state, CancelRegistration command)
    {
        if (state.Status == "absent") throw new InvalidOperationException("Registration is absent.");
        if (command.Headers.UserId != state.AttendeeId) throw new UnauthorizedAccessException();
        if (state.Status == "cancelled") return [];
        return [new RegistrationCancelled(command.Headers, state.RegistrationId,
            state.WorkshopId, state.AttendeeId, state.DisplayName)];
    }
}

public sealed class RegistrationActor : EventSourcedActor<RegistrationState>, IRegistrationActor
{
    public RegistrationActor(IStore store, IConfiguration configuration) : base(store, configuration)
        => State = RegistrationState.Empty;

    public IEnumerable<Event> Handle(RequestRegistration command)
    {
        if (!Guid.TryParse(Id, out Guid actorId) || actorId != command.RegistrationId)
            throw new ArgumentException("The actor key must match the registration ID.");
        return RegistrationRules.Request(State, command);
    }

    public IEnumerable<Event> Handle(CancelRegistration command) => RegistrationRules.Cancel(State, command);
    public void On(RegistrationRequested e) => State = State.Apply(e);
    public void On(RegistrationCancelled e) => State = State.Apply(e);
    public RegistrationState Handle(GetRegistration _) => State;
}
