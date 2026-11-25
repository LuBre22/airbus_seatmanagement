using System.Collections.ObjectModel;
namespace SxSeatOps.Core;

/// <summary>Actor identity and granted operational roles.</summary>
public sealed record OperatorContext(string Actor,OperatorRole Roles);
/// <summary>Result returned for an accepted or rejected domain operation.</summary>
public sealed record OperationResult(bool Succeeded,string? Reason,string OperationId)
{
    /// <summary>Creates a successful result.</summary>
    public static OperationResult Success(Guid id)=>new(true,null,id.ToString("D"));
    /// <summary>Creates a rejected result.</summary>
    public static OperationResult Reject(Guid id,string reason)=>new(false,reason,id.ToString("D"));
}
/// <summary>Append-only audit integration point.</summary>
public interface IAuditSink
{
    /// <summary>Appends exactly one audit event.</summary>
    void Record(string actor,string payload);
}
/// <summary>Mutable application aggregate for a single flight.</summary>
public sealed class CabinState
{
    private readonly Dictionary<string,Passenger> _passengers=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SeatId,string> _assignments=new();
    private readonly Dictionary<string,SeatId> _passengerSeats=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SeatId,BlockReason> _blocks=new();
    /// <summary>Gets the seat map.</summary>
    public SeatMap SeatMap { get; }
    /// <summary>Gets the schedule.</summary>
    public FlightSchedule Schedule { get; }
    /// <summary>Gets the injected clock used for freeze enforcement.</summary>
    public TimeProvider Clock { get; }
    /// <summary>Gets the pending upgrade requests.</summary>
    public UpgradeQueue UpgradeQueue { get; }=new();
    /// <summary>Gets the undo/redo operation history.</summary>
    public OperationHistory History { get; }=new();
    /// <summary>Returns manifest passengers read-only.</summary>
    public IReadOnlyDictionary<string,Passenger> Passengers=>new ReadOnlyDictionary<string,Passenger>(_passengers);
    /// <summary>Returns seat-to-PNR assignments read-only.</summary>
    public IReadOnlyDictionary<SeatId,string> SeatAssignments=>new ReadOnlyDictionary<SeatId,string>(_assignments);
    /// <summary>Returns PNR-to-seat assignments read-only.</summary>
    public IReadOnlyDictionary<string,SeatId> PassengerSeats=>new ReadOnlyDictionary<string,SeatId>(_passengerSeats);
    /// <summary>Returns seat blocks read-only.</summary>
    public IReadOnlyDictionary<SeatId,BlockReason> SeatBlocks=>new ReadOnlyDictionary<SeatId,BlockReason>(_blocks);
    /// <summary>Creates a flight state with the standard map and schedule.</summary>
    public CabinState(SeatMap? seatMap=null,FlightSchedule? schedule=null,TimeProvider? clock=null){SeatMap=seatMap??new SeatMap();Schedule=schedule??FlightSchedule.CreateDefault();Clock=clock??TimeProvider.System;}
    /// <summary>Adds a unique passenger to the manifest.</summary>
    public bool TryAddPassenger(Passenger passenger) { ArgumentNullException.ThrowIfNull(passenger); return _passengers.TryAdd(passenger.Pnr,passenger); }
    /// <summary>Gets a passenger by case-insensitive PNR.</summary>
    public bool TryGetPassenger(string pnr,out Passenger? passenger)=>_passengers.TryGetValue(pnr,out passenger);
    /// <summary>Checks aggregate invariants across seats, assignments, blocks, and cabin counts.</summary>
    public bool CheckConsistency(out string? violation)
    {
        violation=null;
        if(_assignments.Count!=_passengerSeats.Count){violation="Assignment indexes have different counts.";return false;}
        foreach(var pair in _assignments){if(!_passengerSeats.TryGetValue(pair.Value,out SeatId inverse)||inverse!=pair.Key){violation="Assignment indexes are not mirrored.";return false;} if(!_passengers.ContainsKey(pair.Value)){violation="Assigned passenger is absent from manifest.";return false;} if(!SeatMap.TryGetSeat(pair.Key,out Seat? seat)||seat!.Status!=SeatStatus.Occupied){violation="Occupied seat status is inconsistent.";return false;}}
        foreach(var pair in _blocks)if(_assignments.ContainsKey(pair.Key)||!SeatMap.TryGetSeat(pair.Key,out Seat? seat)||seat!.Status!=SeatStatus.Blocked){violation="Seat block is inconsistent.";return false;}
        foreach(Seat seat in SeatMap.Seats){SeatStatus expected=_assignments.ContainsKey(seat.Id)?SeatStatus.Occupied:_blocks.ContainsKey(seat.Id)?SeatStatus.Blocked:SeatStatus.Available;if(seat.Status!=expected){violation=$"Status mismatch at {seat.Id}.";return false;}}
        return true;
    }
    internal void AddBlock(SeatId id,BlockReason reason){_blocks.Add(id,reason);SeatMap.TryGetSeat(id,out Seat? s);s!.Status=SeatStatus.Blocked;}
    internal bool RemoveBlock(SeatId id){if(!_blocks.Remove(id))return false;SeatMap.TryGetSeat(id,out Seat? s);s!.Status=SeatStatus.Available;return true;}
    internal bool Assign(string pnr,SeatId id){if(!_passengers.ContainsKey(pnr)||!SeatMap.TryGetSeat(id,out Seat? seat)||_assignments.ContainsKey(id)||_passengerSeats.ContainsKey(pnr)||seat!.Status!=SeatStatus.Available)return false;_assignments.Add(id,pnr);_passengerSeats.Add(pnr,id);seat.Status=SeatStatus.Occupied;return true;}
    internal bool Unassign(string pnr){if(!_passengerSeats.Remove(pnr,out SeatId id))return false;_assignments.Remove(id);SeatMap.TryGetSeat(id,out Seat? seat);seat!.Status=SeatStatus.Available;return true;}
    internal bool TrySwap(SeatId first,SeatId second)
    { if(!_assignments.TryGetValue(first,out string? p1)||!_assignments.TryGetValue(second,out string? p2))return false;_assignments[first]=p2;_assignments[second]=p1;_passengerSeats[p1]=second;_passengerSeats[p2]=first;return true; }
    /// <summary>Captures all mutable state for undo and transaction rollback.</summary>
    public CabinStateSnapshot Snapshot()
    { var seats=new Dictionary<SeatId,SeatStatus>();foreach(Seat s in SeatMap.Seats)seats.Add(s.Id,s.Status);var entitlements=new Dictionary<string,CabinClass>(StringComparer.OrdinalIgnoreCase);foreach(var p in _passengers)entitlements.Add(p.Key,p.Value.EntitledCabin);return new CabinStateSnapshot(new Dictionary<SeatId,string>(_assignments),new Dictionary<string,SeatId>(_passengerSeats,StringComparer.OrdinalIgnoreCase),new Dictionary<SeatId,BlockReason>(_blocks),seats,entitlements); }
    /// <summary>Restores a prior snapshot exactly.</summary>
    public void Restore(CabinStateSnapshot snapshot)
    { ArgumentNullException.ThrowIfNull(snapshot);_assignments.Clear();foreach(var p in snapshot.Assignments)_assignments.Add(p.Key,p.Value);_passengerSeats.Clear();foreach(var p in snapshot.PassengerSeats)_passengerSeats.Add(p.Key,p.Value);_blocks.Clear();foreach(var p in snapshot.Blocks)_blocks.Add(p.Key,p.Value);foreach(var p in snapshot.SeatStatuses)if(SeatMap.TryGetSeat(p.Key,out Seat? s))s!.Status=p.Value;foreach(var p in snapshot.Entitlements)if(_passengers.TryGetValue(p.Key,out Passenger? x))x.EntitledCabin=p.Value; }
}
/// <summary>Value-comparable snapshot used by undo and transactional tests.</summary>
public sealed class CabinStateSnapshot : IEquatable<CabinStateSnapshot>
{
    internal Dictionary<SeatId,string> Assignments {get;} internal Dictionary<string,SeatId> PassengerSeats {get;} internal Dictionary<SeatId,BlockReason> Blocks {get;} internal Dictionary<SeatId,SeatStatus> SeatStatuses {get;} internal Dictionary<string,CabinClass> Entitlements {get;}
    internal CabinStateSnapshot(Dictionary<SeatId,string>a,Dictionary<string,SeatId>p,Dictionary<SeatId,BlockReason>b,Dictionary<SeatId,SeatStatus>s,Dictionary<string,CabinClass>e){Assignments=a;PassengerSeats=p;Blocks=b;SeatStatuses=s;Entitlements=e;}
    /// <inheritdoc/>
    public bool Equals(CabinStateSnapshot? other)=>other is not null&&DictEqual(Assignments,other.Assignments)&&DictEqual(PassengerSeats,other.PassengerSeats)&&DictEqual(Blocks,other.Blocks)&&DictEqual(SeatStatuses,other.SeatStatuses)&&DictEqual(Entitlements,other.Entitlements);
    /// <inheritdoc/>
    public override bool Equals(object? obj)=>obj is CabinStateSnapshot other&&Equals(other);
    /// <inheritdoc/>
    public override int GetHashCode()=>HashCode.Combine(Assignments.Count,PassengerSeats.Count,Blocks.Count,SeatStatuses.Count,Entitlements.Count);
    private static bool DictEqual<TKey,TValue>(Dictionary<TKey,TValue>a,Dictionary<TKey,TValue>b) where TKey:notnull {if(a.Count!=b.Count)return false;foreach(var p in a)if(!b.TryGetValue(p.Key,out TValue? v)||!EqualityComparer<TValue>.Default.Equals(p.Value,v))return false;return true;}
}

/// <summary>Template-method base class for audited seat changes.</summary>
public abstract class SeatOperation
{
    private CabinStateSnapshot? _before;
    /// <summary>Unique operation identifier.</summary>
    public Guid Id {get;}=Guid.NewGuid();
    /// <summary>Roles accepted by the operation (any matching role is sufficient).</summary>
    public abstract OperatorRole RequiredRole {get;}
    /// <summary>Runs role, freeze, validation, execution, and exactly-one audit event in order.</summary>
    public OperationResult Run(CabinState state,OperatorContext context,IAuditSink audit)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(audit);
        OperationResult result=Replay(state,context);
        audit.Record(context.Actor,$"{AuditPayload()} {Id:D} {(result.Succeeded?"ACCEPTED":"REJECTED: "+result.Reason)}");
        return result;
    }
    internal OperationResult Replay(CabinState state,OperatorContext context)
    {
        string? error=null;
        if((context.Roles&RequiredRole)==OperatorRole.None)return OperationResult.Reject(Id,"Missing required role.");
        if(state.Schedule.IsSeatMapFrozen(state.Clock.GetUtcNow())&&!context.Roles.HasFlag(OperatorRole.Supervisor))return OperationResult.Reject(Id,"Seat map is frozen; Supervisor is additionally required.");
        if(!Validate(state,out error))return OperationResult.Reject(Id,error??"Operation rejected.");
        _before=state.Snapshot();
        if(ExecuteCore(state,out error))return OperationResult.Success(Id);
        state.Restore(_before);_before=null;return OperationResult.Reject(Id,error??"Operation rejected.");
    }
    /// <summary>Builds privacy-safe operation details for the audit event.</summary>
    protected virtual string AuditPayload()=>GetType().Name;
    /// <summary>Checks operation-specific preconditions.</summary>
    protected internal virtual bool Validate(CabinState state,out string? reason){reason=null;return true;}
    /// <summary>Applies the operation after validation.</summary>
    protected internal abstract bool ExecuteCore(CabinState state,out string? reason);
    /// <summary>Restores the state before a successful execution.</summary>
    public virtual bool Undo(CabinState state){if(_before is null)return false;state.Restore(_before);_before=null;return true;}
}
/// <summary>Blocks one unoccupied seat.</summary>
public sealed class BlockSeatOperation(SeatId seat,BlockReason reason):SeatOperation
{
    protected override string AuditPayload()=>$"BLK {seat} {reason.ToCode()}";
    /// <inheritdoc/>
    public override OperatorRole RequiredRole=>OperatorRole.Occ;
    /// <inheritdoc/>
    protected internal override bool Validate(CabinState s,out string? e){e=null;if(!s.SeatMap.TryGetSeat(seat,out Seat? x)){e="Unknown seat.";return false;}if(x!.Status!=SeatStatus.Available){e="Seat is not available.";return false;}return true;}
    /// <inheritdoc/>
    protected internal override bool ExecuteCore(CabinState s,out string? e){s.AddBlock(seat,reason);e=null;return true;}
}
/// <summary>Releases a blocked seat.</summary>
public sealed class ReleaseSeatOperation(SeatId seat):SeatOperation
{
    protected override string AuditPayload()=>$"REL {seat}";
    /// <inheritdoc/>
    public override OperatorRole RequiredRole=>OperatorRole.Occ;
    /// <inheritdoc/>
    protected internal override bool Validate(CabinState s,out string? e){e=s.SeatBlocks.ContainsKey(seat)?null:"Seat is not blocked.";return e is null;}
    /// <inheritdoc/>
    protected internal override bool ExecuteCore(CabinState s,out string? e){s.RemoveBlock(seat);e=null;return true;}
}
/// <summary>Assigns a passenger to an available seat.</summary>
public sealed class AssignSeatOperation(string pnr,SeatId seat):SeatOperation
{
    protected override string AuditPayload()=>$"ASG {pnr} {seat}";
    /// <inheritdoc/>
    public override OperatorRole RequiredRole=>OperatorRole.Station;
    /// <inheritdoc/>
    protected internal override bool Validate(CabinState s,out string? e)
    {e=null;if(!s.TryGetPassenger(pnr,out Passenger? p)){e="Unknown PNR.";return false;}if(!s.SeatMap.TryGetSeat(seat,out Seat? x)){e="Unknown seat.";return false;}if(x!.Status!=SeatStatus.Available){e="Seat is not available.";return false;}if(!x.IsEligibleFor(p!,s.Schedule.LocalDepartureDate,out e))return false;return true;}
    /// <inheritdoc/>
    protected internal override bool ExecuteCore(CabinState s,out string? e){bool ok=s.Assign(pnr,seat);e=ok?null:"Assignment conflicts with current state.";return ok;}
}
/// <summary>Swaps the occupants of two assigned seats.</summary>
public sealed class SwapSeatsOperation(SeatId first,SeatId second):SeatOperation
{
    protected override string AuditPayload()=>$"SWP {first} {second}";
    /// <inheritdoc/>
    public override OperatorRole RequiredRole=>OperatorRole.Station;
    /// <inheritdoc/>
    protected internal override bool Validate(CabinState s,out string? e)
    {
        e=null;if(!s.SeatAssignments.TryGetValue(first,out string? p1)||!s.SeatAssignments.TryGetValue(second,out string? p2)){e="Both seats must be occupied.";return false;}
        if(!s.TryGetPassenger(p1,out Passenger? passenger1)||!s.TryGetPassenger(p2,out Passenger? passenger2)||!s.SeatMap.TryGetSeat(first,out Seat? seat1)||!s.SeatMap.TryGetSeat(second,out Seat? seat2)){e="Assignment state is inconsistent.";return false;}
        if(!seat2!.IsEligibleFor(passenger1!,s.Schedule.LocalDepartureDate,out e))return false;
        if(!seat1!.IsEligibleFor(passenger2!,s.Schedule.LocalDepartureDate,out e))return false;
        return true;
    }
    /// <inheritdoc/>
    protected internal override bool ExecuteCore(CabinState s,out string? e){bool ok=s.TrySwap(first,second);e=ok?null:"Swap failed.";return ok;}
}
/// <summary>Changes passenger entitlement upward and assigns a seat in the new cabin.</summary>
public sealed class UpgradeOperation(string pnr,CabinClass target,SeatId targetSeat):SeatOperation
{
    private CabinClass _oldEntitlement;
    protected override string AuditPayload()=>$"UPG {pnr} {target.ToIataCode()} {targetSeat}";
    /// <inheritdoc/>
    public override OperatorRole RequiredRole=>OperatorRole.Revenue;
    /// <inheritdoc/>
    protected internal override bool Validate(CabinState s,out string? e)
    {e=null;if(!s.TryGetPassenger(pnr,out Passenger? p)){e="Unknown PNR.";return false;}if((int)target >= (int)p!.EntitledCabin){e="Upgrade must move to a higher cabin.";return false;}if(!s.SeatMap.TryGetSeat(targetSeat,out Seat? seat)||seat!.Cabin!=target||seat.Status!=SeatStatus.Available){e="Target seat is unavailable or in the wrong cabin.";return false;} _oldEntitlement=p.EntitledCabin;p.EntitledCabin=target;bool eligible=seat.IsEligibleFor(p,s.Schedule.LocalDepartureDate,out e);p.EntitledCabin=_oldEntitlement;return eligible;}
    /// <inheritdoc/>
    protected internal override bool ExecuteCore(CabinState s,out string? e){s.TryGetPassenger(pnr,out Passenger? p);_oldEntitlement=p!.EntitledCabin;p.EntitledCabin=target;if(s.PassengerSeats.ContainsKey(pnr))s.Unassign(pnr);bool ok=s.Assign(pnr,targetSeat);if(!ok)p.EntitledCabin=_oldEntitlement;e=ok?null:"Upgrade assignment failed.";return ok;}
}
