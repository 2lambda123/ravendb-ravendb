﻿namespace Raven.Server.Documents.Indexes.Spatial
{
    public sealed class SpatialProperty
    {
        public string LatitudeProperty;
        public string LongitudeProperty;

        public SpatialProperty()
        {
        }

        public SpatialProperty(string latitudePropertyPath, string longitudePropertyPath)
        {
            LatitudeProperty = latitudePropertyPath;
            LongitudeProperty = longitudePropertyPath;
        }
    }
}
