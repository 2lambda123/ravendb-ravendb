﻿using System;
using Jint;
using System.Drawing;
using JetBrains.Annotations;
using Raven.Server.Documents.TransactionMerger.Commands;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;

namespace Raven.Server.Rachis.Commands;

public class CandidateCastVoteInTermCommand : MergedTransactionCommand<ClusterOperationContext, ClusterTransaction>
{
    private readonly RachisConsensus _engine;
    private readonly long _electionTerm;
    private readonly string _reason;

    public CandidateCastVoteInTermCommand([NotNull] RachisConsensus engine, long electionTerm, string reason)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _electionTerm = electionTerm;
        _reason = reason;
    }
    protected override long ExecuteCmd(ClusterOperationContext context)
    {
        _engine.CastVoteInTerm(context, _electionTerm, _engine.Tag, _reason);

        return 1;
    }

    public override IReplayableCommandDto<ClusterOperationContext, ClusterTransaction, MergedTransactionCommand<ClusterOperationContext, ClusterTransaction>> ToDto(JsonOperationContext context)
    {
        throw new System.NotImplementedException();
    }
}
