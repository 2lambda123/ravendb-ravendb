﻿using System.Runtime.CompilerServices;
using Corax.Mappings;
using Corax.Querying.Matches.SortingMatches.Meta;
using Corax.Utils.Spatial;
using Spatial4n.Shapes;

namespace Corax.Utils;

public readonly struct OrderMetadata
{
    public readonly FieldMetadata Field;
    public readonly bool HasBoost;
    public readonly bool Ascending;
    public readonly MatchCompareFieldType FieldType;
    public readonly IPoint Point;
    public readonly double Round;
    public readonly SpatialUnits Units;
    public readonly int RandomSeed;

    public override string ToString()
    {
        return FieldType switch
        {
            MatchCompareFieldType.Sequence => Field.FieldName.ToString(),
            MatchCompareFieldType.Integer => Field.FieldName + " as long",
            MatchCompareFieldType.Floating => Field.FieldName + " as double",
            MatchCompareFieldType.Score => "score()",
            MatchCompareFieldType.Alphanumeric => " alphanumeric(" + Field.FieldName + ")",
            MatchCompareFieldType.Spatial => $"spatial(point: {Point}, round: {Round}, units: {Units})",
            _ => FieldType.ToString()
        } + (Ascending ? "" : " desc");
    }

    public OrderMetadata(bool hasBoost, MatchCompareFieldType fieldType, bool ascending = true)
    {
        Unsafe.SkipInit(out Field);
        Unsafe.SkipInit(out Point);
        Unsafe.SkipInit(out Round);
        Unsafe.SkipInit(out Units);
        Unsafe.SkipInit(out RandomSeed);

        HasBoost = hasBoost;
        Ascending = ascending;
        FieldType = fieldType;
    }
    
    public OrderMetadata(int randomSeed)
    {
        Unsafe.SkipInit(out Field);
        Unsafe.SkipInit(out Point);
        Unsafe.SkipInit(out Round);
        Unsafe.SkipInit(out Units);

        HasBoost = false;
        Ascending = true;
        FieldType = MatchCompareFieldType.Random;
        RandomSeed = randomSeed;

    }

    public OrderMetadata(in FieldMetadata field, bool ascending, MatchCompareFieldType fieldType)
    {
        Unsafe.SkipInit(out HasBoost);
        Unsafe.SkipInit(out Point);
        Unsafe.SkipInit(out Round);
        Unsafe.SkipInit(out Units);
        Unsafe.SkipInit(out RandomSeed);

        Field = field;
        Ascending = ascending;
        FieldType = fieldType;
    }

    public OrderMetadata(in FieldMetadata field, bool ascending, MatchCompareFieldType fieldType, IPoint point, double round, SpatialUnits units)
    {
        Unsafe.SkipInit(out HasBoost);  
        Unsafe.SkipInit(out RandomSeed);

        Field = field;
        Ascending = ascending;
        FieldType = fieldType;
        Round = round;
        Point = point;
        Units = units;
    }
}
