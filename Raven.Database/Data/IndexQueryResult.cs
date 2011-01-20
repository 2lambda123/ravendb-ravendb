//-----------------------------------------------------------------------
// <copyright file="IndexQueryResult.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Newtonsoft.Json.Linq;

namespace Raven.Database.Data
{
	public class IndexQueryResult
	{
		public string Key { get; set; }
		public JObject Projection { get; set; }
	}
}
