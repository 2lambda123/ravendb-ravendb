//-----------------------------------------------------------------------
// <copyright file="DeleteCommandData.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using Newtonsoft.Json.Linq;
using Raven.Http;

namespace Raven.Database.Data
{
	/// <summary>
	/// A single batch operation for a document DELETE
	/// </summary>
    public class DeleteCommandData : ICommandData
    {
		/// <summary>
		/// Gets or sets the key.
		/// </summary>
		/// <value>The key.</value>
        public virtual string Key { get; set; }

		/// <summary>
		/// Gets the method.
		/// </summary>
		/// <value>The method.</value>
    	public string Method
    	{
			get { return "DELETE"; }
    	}
		/// <summary>
		/// Gets or sets the etag.
		/// </summary>
		/// <value>The etag.</value>
		public virtual Guid? Etag { get; set; }

        public TransactionInformation TransactionInformation
        {
            get; set;
        }

    	public JObject Metadata
    	{
			get { return null; }
    	}

		/// <summary>
		/// Translate this instance to a Json object.
		/// </summary>
    	public JObject ToJson()
    	{
    		return new JObject(
				new JProperty("Key", Key),
				new JProperty("Etag", new JValue(Etag != null ? (object)Etag.ToString() : null)),
				new JProperty("Method", Method)
				);
    	}
    }
}
