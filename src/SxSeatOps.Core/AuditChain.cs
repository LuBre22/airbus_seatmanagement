using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SxSeatOps.Core;

/// <summary>Immutable entry in the SHA-384 audit chain.</summary>
public sealed record AuditBlock(long Index,DateTimeOffset TimestampUtc,string Actor,string Payload,string PreviousHash,long Nonce,string Hash);

/// <summary>Append-only hash chain implementing the audit sink.</summary>
public sealed class AuditChain : IAuditSink
{
    private const string ZeroHash="000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";
    private readonly List<AuditBlock> _blocks=new(); private readonly TimeProvider _clock;
    /// <summary>Creates a chain with a deterministic genesis block.</summary>
    public AuditChain(TimeProvider? clock=null,int difficulty=0)
    {
        if(difficulty<0||difficulty>8)throw new ArgumentOutOfRangeException(nameof(difficulty));_clock=clock??TimeProvider.System;Difficulty=difficulty;
        string payload="GENESIS SX0350 2026-12-18";var genesis=Build(0,new DateTimeOffset(2026,12,18,0,0,0,TimeSpan.Zero),"SYSTEM",payload,ZeroHash,0);_blocks.Add(genesis);
    }
    /// <summary>Gets the proof-of-work difficulty in leading hexadecimal zeroes.</summary>
    public int Difficulty { get; }
    /// <summary>Gets a read-only view of the chain.</summary>
    public IReadOnlyList<AuditBlock> Blocks=>_blocks.AsReadOnly();
    /// <summary>Gets the latest block.</summary>
    public AuditBlock Head=>_blocks[^1];
    /// <summary>Appends an audit event using the injected clock.</summary>
    public AuditBlock Append(string actor,string payload)
    {
        ArgumentNullException.ThrowIfNull(actor);ArgumentNullException.ThrowIfNull(payload);
        DateTimeOffset time=_clock.GetUtcNow().ToUniversalTime();if(time<_blocks[^1].TimestampUtc)time=_blocks[^1].TimestampUtc;
        long index=_blocks[^1].Index+1;long nonce=0;AuditBlock block;
        do {block=Build(index,time,actor,payload,_blocks[^1].Hash,nonce);if(Difficulty==0||block.Hash.StartsWith(new string('0',Difficulty),StringComparison.Ordinal))break;nonce++;} while(nonce<long.MaxValue);
        _blocks.Add(block);return block;
    }
    /// <inheritdoc/>
    public void Record(string actor,string payload)=>Append(actor,payload);
    /// <summary>Verifies genesis, indices, links, timestamps, recomputed hashes and current proof-of-work.</summary>
    public bool Verify(out long firstInvalidIndex,out string? reason)=>Verify(Difficulty,out firstInvalidIndex,out reason);
    /// <summary>Verifies the chain at the requested proof-of-work difficulty.</summary>
    public bool Verify(int difficulty,out long firstInvalidIndex,out string? reason)
    {
        firstInvalidIndex=-1;reason=null;if(difficulty<0||difficulty>8){reason="Difficulty is outside the supported range.";return false;}
        if(_blocks.Count==0){firstInvalidIndex=0;reason="Genesis block is missing.";return false;}
        for(int i=0;i<_blocks.Count;i++)
        {
            AuditBlock b=_blocks[i];firstInvalidIndex=b.Index;
            if(b.Index!=i){reason="Block index is not contiguous.";return false;}
            if(i==0){if(b.Actor!="SYSTEM"||b.PreviousHash!=ZeroHash||b.Payload!="GENESIS SX0350 2026-12-18"){reason="Genesis fields are invalid.";return false;}}
            else {AuditBlock prev=_blocks[i-1];if(b.PreviousHash!=prev.Hash){reason="Previous hash does not match.";return false;}if(b.TimestampUtc<prev.TimestampUtc){reason="Timestamp moved backwards.";return false;}}
            string expected=ComputeHash(b);if(!FixedEquals(expected,b.Hash)){reason="Block hash does not match its content.";return false;}
            if(difficulty>0&&!b.Hash.StartsWith(new string('0',difficulty),StringComparison.Ordinal)){reason="Proof-of-work difficulty is not satisfied.";return false;}
        }
        firstInvalidIndex=-1;return true;
    }
    /// <summary>Creates the prescribed canonical block representation.</summary>
    public static string Canonicalize(AuditBlock b)=>string.Concat(b.Index.ToString(CultureInfo.InvariantCulture),"|",TimestampFormatter.FormatAudit(b.TimestampUtc),"|",b.Actor.Length.ToString(CultureInfo.InvariantCulture),":",b.Actor,"|",b.Payload.Length.ToString(CultureInfo.InvariantCulture),":",b.Payload,"|",b.PreviousHash,"|",b.Nonce.ToString(CultureInfo.InvariantCulture));
    private static AuditBlock Build(long index,DateTimeOffset time,string actor,string payload,string previous,long nonce)
    {var partial=new AuditBlock(index,time.ToUniversalTime(),actor,payload,previous,nonce,string.Empty);return partial with {Hash=ComputeHash(partial)};}
    private static string ComputeHash(AuditBlock block)=>Convert.ToHexStringLower(SHA384.HashData(Encoding.UTF8.GetBytes(Canonicalize(block))));
    private static bool FixedEquals(string a,string b)
    {byte[] x=Convert.FromHexString(a),y;try{y=Convert.FromHexString(b);}catch(FormatException){return false;}return x.Length==y.Length&&CryptographicOperations.FixedTimeEquals(x,y);}
}

/// <summary>Bounded scope-independent undo/redo history and atomic batches.</summary>
public sealed class OperationHistory
{
    private readonly Stack<SeatOperation> _undo=new();private readonly Stack<SeatOperation> _redo=new();
    /// <summary>Gets the number of undoable operations.</summary>
    public int UndoCount=>_undo.Count;
    /// <summary>Gets the number of redoable operations.</summary>
    public int RedoCount=>_redo.Count;
    /// <summary>Executes an operation and records only successful results.</summary>
    public OperationResult Execute(SeatOperation operation,CabinState state,OperatorContext context,IAuditSink audit)
    {OperationResult r=operation.Run(state,context,audit);if(r.Succeeded){_undo.Push(operation);_redo.Clear();}return r;}
    /// <summary>Attempts to undo the latest successful operation.</summary>
    public bool TryUndo(CabinState state,out SeatOperation? operation)
    {operation=null;if(!_undo.TryPop(out SeatOperation? item))return false;if(!item.Undo(state)){_undo.Push(item);return false;}_redo.Push(item);operation=item;return true;}
    /// <summary>Attempts an audited undo while rechecking the operation role and freeze rule.</summary>
    public bool TryUndo(CabinState state,OperatorContext context,IAuditSink audit,out SeatOperation? operation)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(audit);operation=null;
        if(!_undo.TryPeek(out SeatOperation? item)){audit.Record(context.Actor,"UNDO REJECTED empty history");return false;}
        if((context.Roles&item.RequiredRole)==OperatorRole.None){audit.Record(context.Actor,$"UNDO {item.Id:D} REJECTED missing role");return false;}
        if(state.Schedule.IsSeatMapFrozen(state.Clock.GetUtcNow())&&!context.Roles.HasFlag(OperatorRole.Supervisor)){audit.Record(context.Actor,$"UNDO {item.Id:D} REJECTED frozen");return false;}
        _undo.Pop();if(!item.Undo(state)){_undo.Push(item);audit.Record(context.Actor,$"UNDO {item.Id:D} REJECTED not undoable");return false;}
        _redo.Push(item);operation=item;audit.Record(context.Actor,$"UNDO {item.Id:D}");return true;
    }
    /// <summary>Attempts an audited redo while rechecking the operation role and freeze rule.</summary>
    public bool TryRedo(CabinState state,OperatorContext context,IAuditSink audit,out SeatOperation? operation)
    {
        ArgumentNullException.ThrowIfNull(state);ArgumentNullException.ThrowIfNull(context);ArgumentNullException.ThrowIfNull(audit);operation=null;
        if(!_redo.TryPeek(out SeatOperation? item)){audit.Record(context.Actor,"REDO REJECTED empty history");return false;}
        OperationResult result=item.Replay(state,context);if(!result.Succeeded){audit.Record(context.Actor,$"REDO {item.Id:D} REJECTED {result.Reason}");return false;}
        _redo.Pop();_undo.Push(item);operation=item;audit.Record(context.Actor,$"REDO {item.Id:D}");return true;
    }
    /// <summary>Runs a batch atomically, undoing successful earlier operations if one fails.</summary>
    public bool ExecuteBatch(IEnumerable<SeatOperation> operations,CabinState state,OperatorContext context,IAuditSink audit,out string? reason)
    {
        ArgumentNullException.ThrowIfNull(operations);var completed=new Stack<SeatOperation>();reason=null;
        foreach(SeatOperation op in operations){OperationResult result=op.Run(state,context,audit);if(!result.Succeeded){reason=result.Reason;while(completed.TryPop(out SeatOperation? previous))previous.Undo(state);return false;}completed.Push(op);}
        SeatOperation[] ordered=completed.ToArray();for(int i=ordered.Length-1;i>=0;i--)_undo.Push(ordered[i]);_redo.Clear();return true;
    }
}
