﻿using System;
using System.Text;

namespace Raven.Client.Documents.Session.Tokens
{
    public class FromToken : QueryToken
    {
        public string CollectionName { get; }

        public string IndexName { get; }

        public bool IsDynamic { get; }

        public string Alias { get; }

        private FromToken(string indexName, string collectionName, string alias = null)
        {
            CollectionName = collectionName;
            IndexName = indexName;
            IsDynamic = CollectionName != null;
            Alias = alias;
        }

        public static FromToken Create(string indexName, string collectionName, string alias = null)
        {
            return new FromToken(indexName, collectionName, alias);
        }

        public override void WriteTo(StringBuilder writer)
        {
            if (IndexName == null && CollectionName == null)
                throw new NotSupportedException("Either IndexName or CollectionName must be specified");

            if (IsDynamic)
            {
                writer
                    .Append("from ");

                if (RequiresQuotes(CollectionName, out var escapedCollectionName))
                    writer.Append("'").Append(escapedCollectionName).Append("'");
                else
                    WriteField(writer, CollectionName);
            }
            else
            {
                writer
                    .Append("from index '")
                    .Append(IndexName)
                    .Append("'");
            }

            if (Alias != null)
            {
                writer.Append(" as ").Append(Alias);
            }
        }

        private static bool RequiresQuotes(string collectionName, out string escapedCollectionName)
        {
            var requiresQuotes = false;
            for (var i = 0; i < collectionName.Length; i++)
            {
                var ch = collectionName[i];

                if (i == 0 && char.IsDigit(ch))
                {
                    requiresQuotes = true;
                    break;
                }

                if (char.IsLetterOrDigit(ch) == false && ch != '_')
                {
                    requiresQuotes = true;
                    break;
                }
            }

            if (requiresQuotes)
            {
                escapedCollectionName = collectionName.Replace("'", "\\'");
                return true;
            }

            escapedCollectionName = null;
            return false;
        }
    }
}
