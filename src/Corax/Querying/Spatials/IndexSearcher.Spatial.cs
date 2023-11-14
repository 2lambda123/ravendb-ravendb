﻿using System.Threading;
using Corax.Mappings;
using Corax.Querying.Matches;
using Corax.Querying.Matches.Meta;
using Corax.Querying.Matches.SpatialMatch;
using Spatial4n.Shapes;
using SpatialContext = Spatial4n.Context.SpatialContext;

namespace Corax.Querying;

public partial class IndexSearcher
{
    public IQueryMatch SpatialQuery(FieldMetadata field, double error, IShape shape, SpatialContext spatialContext, Utils.Spatial.SpatialRelation spatialRelation, bool isNegated = false, in CancellationToken token = default)
    {
        var terms = _fieldsTree?.CompactTreeFor(field.FieldName);
        if (terms == null)
        {
            // If either the term or the field does not exist the request will be empty. 
            return TermMatch.CreateEmpty(this, Allocator);
        }

        var match = new SpatialMatch(this, _transaction.Allocator, spatialContext, field, shape, terms, error, spatialRelation, token);
        if (isNegated)
        {
            return AndNot(AllEntries(), match);
        }
        
        return match;
    }
}
