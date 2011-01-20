﻿#if !NET_3_5
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raven.Database.Linq;

namespace Raven.Database.Json
{
	public class JsonToJsonConverter : JsonConverter
	{
		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			if(value is DynamicNullObject)
				writer.WriteNull();
			else if (value is JObject)
				((JObject)value).WriteTo(writer);
			else
				((DynamicJsonObject)value).Inner.WriteTo(writer);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}

		public override bool CanConvert(Type objectType)
		{
			return objectType == typeof(JObject) || objectType == typeof(DynamicJsonObject) || objectType == typeof(DynamicNullObject);
		}
	}
}
#endif
