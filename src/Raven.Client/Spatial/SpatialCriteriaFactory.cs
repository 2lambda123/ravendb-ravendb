using Raven.Client.Data;
using Raven.Client.Indexing;

namespace Raven.Client.Spatial
{
    public class SpatialCriteriaFactory
    {
        public SpatialCriteria RelatesToShape(object shape, SpatialRelation relation, double distErrorPercent = 0.025)
        {
            return new SpatialCriteria
                   {
                       Relation = relation,
                       Shape = shape,
                       DistanceErrorPct = distErrorPercent
                   };
        }

        public SpatialCriteria Intersects(object shape, double distErrorPercent = 0.025)
        {
            return RelatesToShape(shape, SpatialRelation.Intersects, distErrorPercent);
        }

        public SpatialCriteria Contains(object shape, double distErrorPercent = 0.025)
        {
            return RelatesToShape(shape, SpatialRelation.Contains, distErrorPercent);
        }

        public SpatialCriteria Disjoint(object shape, double distErrorPercent = 0.025)
        {
            return RelatesToShape(shape, SpatialRelation.Disjoint, distErrorPercent);
        }

        public SpatialCriteria Within(object shape, double distErrorPercent = 0.025)
        {
            return RelatesToShape(shape, SpatialRelation.Within, distErrorPercent);
        }

        public SpatialCriteria WithinRadius(double radius, double latitude, double longitude, double distErrorPercent=0.025)
        {
            return RelatesToShape(SpatialIndexQuery.GetQueryShapeFromLatLon(latitude, longitude, radius), SpatialRelation.Within,distErrorPercent);
        }
    }
}
