using System;
using System.Linq.Expressions;
using Lambda2Js;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Replication.Messages;

namespace Raven.Client.Documents.Subscriptions
{
    public class SubscriptionCriteria 
    {
        public SubscriptionCriteria()
        {
            // for deserialization
        }

        public SubscriptionCriteria(string collection)
        {
            Collection = collection ?? throw new ArgumentNullException(nameof(collection));
        }

        public string Collection { get;  set; }
        public string Script { get; set; }
        public bool IncludeRevisions { get; set; }
    }

    public class SubscriptionCriteria<T>
    {
        private class SubscriptionCriteriaConvertor : JavascriptConversionExtension
        {
            public ParameterExpression Parameter;

            public override void ConvertToJavascript(JavascriptConversionContext context)
            {
                var node = context.Node as MemberExpression;
                if (node == null )
                    return;

                context.PreventDefault();
                var javascriptWriter = context.GetWriter();

                using (javascriptWriter.Operation(node))
                {
                    if (node.Expression == Parameter)
                    {
                        javascriptWriter.Write("this.");
                        javascriptWriter.Write(node.Member.Name);
                        return;
                    }

                    context.Visitor.Visit(node.Expression as MemberExpression);
                    javascriptWriter.Write(".");
                    javascriptWriter.Write(node.Member.Name);
                }
            }
        }


        public SubscriptionCriteria(Expression<Func<T, bool>> predicate)
        {
            var script = predicate.CompileToJavascript(
                new JavascriptCompilationOptions(
                    JsCompilationFlags.BodyOnly, 
                    new SubscriptionCriteriaConvertor { Parameter = predicate.Parameters[0] }));
            Script = $"return {script};";
        }
        
        public SubscriptionCriteria()
        {
            
        }
        
        public string Script { get; set; }
        public bool? IncludeRevisions { get; set; }
    }
    
    
    public class SubscriptionTryout
    {
        public string ChangeVector { get; set; }
        public string Collection { get; set; }
        public string Script { get; set; }
        public bool IncludeRevisions { get; set; }
    }

    public class SubscriptionCreationOptions
    {
        public const string DefaultRevisionsScript = "return {Current:this.Current, Previous:this.Previous};";
        public string Name { get; set; }
        public SubscriptionCriteria Criteria { get; set; }
        public string ChangeVector { get; set; }
    }

    public class SubscriptionCreationOptions<T>
    {
        public SubscriptionCreationOptions()
        {
            Criteria = new SubscriptionCriteria<T>();
        }


        public SubscriptionCriteria CreateOptions(DocumentConventions conventions)
        {
            var tType = typeof(T);
            var includeRevisions = tType.IsConstructedGenericType && tType.GetGenericTypeDefinition() == typeof(Revision<>);

            return new SubscriptionCriteria(conventions.GetCollectionName(includeRevisions ? tType.GenericTypeArguments[0] : typeof(T)))
            {
                Script = Criteria?.Script ?? (includeRevisions ? SubscriptionCreationOptions.DefaultRevisionsScript : null),
                IncludeRevisions =  includeRevisions || (Criteria?.IncludeRevisions ?? false) 
            };

        }

        public string Name { get; set; }
        public SubscriptionCriteria<T> Criteria { get; set; }
        public string ChangeVector { get; set; }
    }

    public class Revision<T> where T : class
    {
        public T Previous;
        public T Current;
    }
}
