﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raven.Server.Documents.Queries.Graph
{
    public interface IQueryStep
    {
        ValueTask Initialize();

        HashSet<string> GetAllAliases();

        string GetOuputAlias();

        bool GetNext(out GraphQueryRunner.Match match);

        bool TryGetById(string id, out GraphQueryRunner.Match match);
    }
}
