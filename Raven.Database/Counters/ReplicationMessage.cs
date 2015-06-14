using System;
using System.Collections.Generic;

namespace Raven.Database.Counters
{
    public class ReplicationMessage
    {
		public ReplicationMessage()
		{
			Counters = new List<ReplicationCounter>();
		}

        public string SendingServerName { get; set; }

        public List<ReplicationCounter> Counters { get; set; }

	    public Guid ServerId { get; set; }
    }
}