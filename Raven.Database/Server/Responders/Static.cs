//-----------------------------------------------------------------------
// <copyright file="Static.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Raven.Database.Data;
using Raven.Database.Extensions;
using Raven.Http.Abstractions;
using Raven.Http.Extensions;

namespace Raven.Database.Server.Responders
{
	public class Static : RequestResponder
	{
		public override string UrlPattern
		{
			get { return "^/static/(.+)"; }
		}

		public override string[] SupportedVerbs
		{
			get { return new[] {"GET", "PUT", "DELETE"}; }
		}

		public override void Respond(IHttpContext context)
		{
			var match = urlMatcher.Match(context.GetRequestUrl());
			var filename = match.Groups[1].Value;
			var etag = context.GetEtag();
			switch (context.Request.HttpMethod)
			{
				case "GET":
					var attachmentAndHeaders = Database.GetStatic(filename);
					if (attachmentAndHeaders == null)
					{
						context.SetStatusToNotFound();
						return;
					}
					if (context.MatchEtag(attachmentAndHeaders.Etag))
					{
						context.SetStatusToNotModified();
						return;
					}
					context.WriteData(attachmentAndHeaders.Data, attachmentAndHeaders.Metadata,
					                  attachmentAndHeaders.Etag);
					break;
				case "PUT":
					Database.PutStatic(filename, context.GetEtag(), context.Request.InputStream.ReadData(),
					                   context.Request.Headers.FilterHeaders(isServerDocument:false));
					context.SetStatusToCreated("/static/" + filename);
					break;
				case "DELETE":
					Database.DeleteStatic(filename, etag);
					context.SetStatusToDeleted();
					break;
			}
		}
	}
}
