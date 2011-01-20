//-----------------------------------------------------------------------
// <copyright file="Root.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using Raven.Http.Abstractions;

namespace Raven.Database.Server.Responders
{
	public class Root : RequestResponder
	{
		public override string UrlPattern
		{
			get { return "^/$"; }
		}

		public override string[] SupportedVerbs
		{
			get { return new[] {"GET"}; }
		}

		public override void Respond(IHttpContext context)
		{
			if (Settings.VirtualDirectory != "/")
				context.Response.Redirect(Settings.VirtualDirectory + "/raven/index.html");
			else
				context.Response.Redirect("/raven/index.html");
		}
	}
}
