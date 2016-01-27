using System.Dynamic;

namespace Raven.Server.Json
{
    public unsafe class DynamicBlittableJson : DynamicObject
    {
        protected BlittableJsonReaderObject BlittableJsonReaderObject;

        public class DynamicBlittableArray : DynamicObject
        {
            protected BlittableJsonReaderArray BlittableJsonReaderArray;

            public DynamicBlittableArray(BlittableJsonReaderArray blittableJsonReaderArray)
            {
                BlittableJsonReaderArray = blittableJsonReaderArray;
            }
          

            public int Length => BlittableJsonReaderArray.Length;

            public int Count => BlittableJsonReaderArray.Count;

            public override bool TryGetMember(GetMemberBinder binder, out object result)
            {
                const string LengthName = "Length";
                const string CountName = "Count";
                result = null;
                if (string.CompareOrdinal(binder.Name, LengthName)==0 || string.CompareOrdinal(binder.Name, CountName) == 0)
                {
                    result = Length;
                    return true;
                }

                return false;
            }


            public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
            {
                var i = (int) (indexes[0]);
                result = null;
                object resultObject = BlittableJsonReaderArray.GetByIndex(i);

                if (resultObject is BlittableJsonReaderObject)
                {
                    result = new DynamicBlittableJson((BlittableJsonReaderObject)resultObject);
                }
                else if (resultObject is BlittableJsonReaderArray)
                {
                    result = new DynamicBlittableArray((BlittableJsonReaderArray)resultObject);
                }
                else
                {
                    result = resultObject;
                }
                return true;
            }
        }


        public DynamicBlittableJson(BlittableJsonReaderObject blittableJsonReaderObject)
        {
            BlittableJsonReaderObject = blittableJsonReaderObject;
        }      

        public string[] GetPropertyNames()
        {
            return BlittableJsonReaderObject.GetPropertyNames();
        }

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            result = null;
            object objectResult = null;
            if (!BlittableJsonReaderObject.TryGetMember(binder.Name, out objectResult))
                return false;

            if (objectResult is BlittableJsonReaderObject)
            {
                result = new DynamicBlittableJson((BlittableJsonReaderObject)objectResult);
            }
            else if (objectResult is BlittableJsonReaderArray)
            {
                result = new DynamicBlittableArray((BlittableJsonReaderArray)objectResult);
            }
            else
                result = objectResult;

            return true;
        }
    }
}