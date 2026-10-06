using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Orleans;
using Troolio.Core;
using Troolio.Stores;

namespace Workshop;

public interface IWorkshopRosterActor : IActor { }

[GenerateSerializer]
public record AddAttendee(Metadata Headers, Guid WorkshopId, Guid RegistrationId,
    Guid AttendeeId) : InternalCommand<IWorkshopRosterActor>(Headers);

[GenerateSerializer]
public record RemoveAttendee(Metadata Headers, Guid WorkshopId, Guid RegistrationId)
    : InternalCommand<IWorkshopRosterActor>(Headers);

[GenerateSerializer]
public record AttendeeAdded(Metadata Headers, Guid RegistrationId, Guid AttendeeId) : Event(Headers);

[GenerateSerializer]
public record AttendeeRemoved(Metadata Headers, Guid RegistrationId) : Event(Headers);

[GenerateSerializer]
public record GetAttendeeCount : Query<IWorkshopRosterActor, int>;

public sealed class WorkshopRosterActor(IStore store, IConfiguration configuration)
    : EventSourcedActor(store, configuration), IWorkshopRosterActor
{
    private ImmutableDictionary<Guid, Guid> attendees = ImmutableDictionary<Guid, Guid>.Empty;
    private ImmutableHashSet<Guid> removed = ImmutableHashSet<Guid>.Empty;

    public IEnumerable<Event> Handle(AddAttendee command)
    {
        if (!Guid.TryParse(Id, out Guid workshopId) || workshopId != command.WorkshopId)
            throw new ArgumentException("Workshop key mismatch.");
        if (removed.Contains(command.RegistrationId)) return [];
        if (attendees.TryGetValue(command.RegistrationId, out Guid prior))
        {
            if (prior != command.AttendeeId) throw new InvalidOperationException("Registration identity conflict.");
            return [];
        }
        return [new AttendeeAdded(command.Headers, command.RegistrationId, command.AttendeeId)];
    }

    public IEnumerable<Event> Handle(RemoveAttendee command)
    {
        if (!Guid.TryParse(Id, out Guid workshopId) || workshopId != command.WorkshopId)
            throw new ArgumentException("Workshop key mismatch.");
        return removed.Contains(command.RegistrationId)
            ? [] : [new AttendeeRemoved(command.Headers, command.RegistrationId)];
    }

    public void On(AttendeeAdded e) => attendees = attendees.Add(e.RegistrationId, e.AttendeeId);
    public void On(AttendeeRemoved e)
    {
        attendees = attendees.Remove(e.RegistrationId);
        removed = removed.Add(e.RegistrationId);
    }
    public int Handle(GetAttendeeCount _) => attendees.Count;
}

[OrchestrationStreamSubscription(typeof(RegistrationActor))]
public sealed class WorkshopEnrolmentOrchestration : OrchestrationActor
{
    public Task On(EventEnvelope<RegistrationRequested> envelope) =>
        System.ActorOf<IWorkshopRosterActor>(envelope.Event.WorkshopId.ToString("D"))
            .Tell(new AddAttendee(envelope.Event.Headers, envelope.Event.WorkshopId,
                envelope.Event.RegistrationId, envelope.Event.AttendeeId));

    public Task On(EventEnvelope<RegistrationCancelled> envelope) =>
        System.ActorOf<IWorkshopRosterActor>(envelope.Event.WorkshopId.ToString("D"))
            .Tell(new RemoveAttendee(envelope.Event.Headers, envelope.Event.WorkshopId,
                envelope.Event.RegistrationId));
}
