// -----------------------------------------------------------------------
//  <copyright file="LinqPathProvider.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Queries;
using Raven.Client.Documents.Session;
using Raven.Client.Extensions;
using Raven.Client.Util;

namespace Raven.Client.Documents.Linq
{
    public class LinqPathProvider
    {
        public class Result
        {
            public Type MemberType;
            public string Path;
            public bool IsNestedPath;
            public PropertyInfo MaybeProperty;
            public string[] Args;
        }

        private readonly DocumentConventions _conventions;

        public LinqPathProvider(DocumentConventions conventions)
        {
            _conventions = conventions;
        }

        /// <summary>
        /// Get the path from the expression, including considering dictionary calls
        /// </summary>
        public Result GetPath(Expression expression)
        {
            expression = SimplifyExpression(expression);

            if (expression is MethodCallExpression callExpression)
            {
                var customMethodResult = _conventions.TranslateCustomQueryExpression(this, callExpression);
                if (customMethodResult != null)
                    return customMethodResult;

                if (callExpression.Method.Name == "Count" && callExpression.Method.DeclaringType == typeof(Enumerable))
                {
                    if (callExpression.Arguments.Count != 1)
                        throw new ArgumentException("Not supported computation: " + callExpression +
                                            ". You cannot use computation in RavenDB queries (only simple member expressions are allowed).");

                    var target = GetPath(callExpression.Arguments[0]);
                    return new Result
                    {
                        MemberType = callExpression.Method.ReturnType,
                        IsNestedPath = false,
                        Path = target.Path + @".Count"
                    };
                }

                if (callExpression.Method.Name == "get_Item")
                {
                    var parent = GetPath(callExpression.Object);

                    var itemKey = GetValueFromExpression(callExpression.Arguments[0], callExpression.Method.GetParameters()[0].ParameterType).ToString();

                    itemKey = QueryFieldUtil.EscapeIfNecessary(itemKey);

                    return new Result
                    {
                        MemberType = callExpression.Method.ReturnType,
                        IsNestedPath = false,
                        Path = parent.Path + "." + itemKey
                    };
                }

                if (callExpression.Method.Name == "ToString" && callExpression.Method.ReturnType == typeof(string))
                {
                    var target = GetPath(callExpression.Object);
                    return new Result
                    {
                        MemberType = typeof(string),
                        IsNestedPath = false,
                        Path = target.Path
                    };
                }

                if (IsCounterCall(callExpression))
                {
                    return CreateCounterResult(callExpression);
                }

                if (IsTimeSeriesCall(callExpression))
                {
                    return CreateTimeSeriesResult(callExpression);
                }

                throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
            }

            var memberExpression = GetMemberExpression(expression);

            var customMemberResult = _conventions.TranslateCustomQueryExpression(this, memberExpression);
            if (customMemberResult != null)
                return customMemberResult;

            // we truncate the nullable .Value because in json all values are nullable
            if (memberExpression.Member.Name == "Value" &&
                Nullable.GetUnderlyingType(memberExpression.Expression.Type) != null)
            {
                return GetPath(memberExpression.Expression);
            }

            AssertNoComputation(memberExpression);

            var result = new Result
            {
                Path = memberExpression.ToString(),
                IsNestedPath = memberExpression.Expression is MemberExpression,
                MemberType = memberExpression.Member.GetMemberType(),
                MaybeProperty = memberExpression.Member as PropertyInfo
            };

            result.Path = HandleMemberExpressionPropertyRenames(memberExpression, result.Path);

            return result;
        }


        internal static Result CreateCounterResult(MethodCallExpression callExpression)
        {
            var counterName = (callExpression.Arguments[callExpression.Arguments.Count - 1] as ConstantExpression)?.Value.ToString();

            string[] args;
            if (callExpression.Method.DeclaringType != typeof(RavenQuery))
            {
                // session.CountersFor().Get()
                var path = (callExpression.Object as MethodCallExpression)?.Arguments[0].ToString();
                args = new[] {RemoveTransparentIdentifiersIfNeeded(path), counterName};
            }
            else if (callExpression.Arguments.Count == 2)
            {                
                var path = callExpression.Arguments[0].ToString();
                args = new[] {RemoveTransparentIdentifiersIfNeeded(path), counterName};               
            }
            else
            {
                args = new[] { counterName };
            }

            return new Result
            {
                MemberType = typeof(long?),
                IsNestedPath = false,
                Path = "counter",
                Args = args
            };
        }

        internal class TimeSeriesWhereClauseModifier : ExpressionVisitor
        {
            private string _parameter;

            public TimeSeriesWhereClauseModifier(string parameter)
            {
                _parameter = parameter;
            }

            public Expression Modify(Expression expression)
            {
                return Visit(expression);
            }

/*            protected override Expression VisitBinary(BinaryExpression b)
            {
                Expression left = this.Visit(b.Left);
                Expression right = this.Visit(b.Right);

                // Make this binary expression an OrElse operation instead of an AndAlso operation.  
                return Expression.MakeBinary(ExpressionType.OrElse, left, right, b.IsLiftedToNull, b.Method);

                //return base.VisitBinary(b);
            }*/

            protected override Expression VisitMember(MemberExpression node)
            {
                if (node.Expression is ParameterExpression p && p.Name == _parameter)
                    return Expression.Parameter(node.Type, node.Member.Name);

                return base.VisitMember(node);
            }
        }

        internal static Result CreateTimeSeriesResult(MethodCallExpression callExpression)
        {
            MethodCallExpression mce = callExpression;
            string tsName = null;
            string where = null;
            string groupBy = null;
            string select = null;

            while (mce != null)
            {
                if (mce.Arguments.Count == 0)
                    throw new InvalidOperationException("Cannot understand how to translate " + callExpression);

                if (mce.Arguments[0] is MethodCallExpression inner)
                {
                    var operand = (mce.Arguments[1] as UnaryExpression)?.Operand;
                    if (!(operand is LambdaExpression lambda))
                        throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
                    var body = lambda.Body; 

                    switch (mce.Method.Name)
                    {
                        case "Where":
                            // turn where ts.Tag = 'tag' into where Tag = 'tag'
                            var parameter = lambda?.Parameters[0].Name;
                            var filterExpression = new TimeSeriesWhereClauseModifier(parameter).Modify(body);

                            where = $" where {filterExpression}";
                            break;
                        case "GroupBy":
                            groupBy = $" group by '{body}'";
                            break;
                        case "Select":
                            string selectArgs = null;

                            switch (body.NodeType)
                            {
                                case ExpressionType.New:
                                    var newExp = (NewExpression)body;

                                    foreach (var c in newExp.Arguments)
                                    {
                                        if (!(c is MethodCallExpression selectCall))
                                            throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
                                        switch (selectCall.Method.Name)
                                        {
                                            case "Max":
                                            case "Min":
                                            case "Sum":
                                            case "Count":
                                                if (selectArgs != null)
                                                    selectArgs += ", ";
                                                selectArgs += $"{selectCall.Method.Name.ToLower()}()";
                                                break;
                                            case "Average":
                                                if (selectArgs != null)
                                                    selectArgs += ", ";
                                                selectArgs += "avg()";
                                                break;
                                            default:
                                                throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
                                        }
                                    }
                                    break;
                                case ExpressionType.MemberInit:
                                    var initExp = (MemberInitExpression)body;

                                    foreach (var c in initExp.Bindings)
                                    {
/*                                        if (!(c.Member. is MethodCallExpression selectCall))
                                            throw new InvalidOperationException("Cannot understand how to translate " + callExpression);*/
                                        switch (c.Member.Name)
                                        {
                                            case "Max":
                                            case "Min":
                                            case "Sum":
                                            case "Count":
                                                if (selectArgs != null)
                                                    selectArgs += ", ";
                                                selectArgs += $"{c.Member.Name.ToLower()}()";
                                                break;
                                            case "Average":
                                                if (selectArgs != null)
                                                    selectArgs += ", ";
                                                selectArgs += "avg()";
                                                break;
                                            default:
                                                throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
                                        }
                                    }
                                    break;
                                case ExpressionType.Call:
                                    var call = (MethodCallExpression)body;
                                    switch (call.Method.Name)
                                    {
                                        case "Max":
                                        case "Min":
                                        case "Sum":
                                        case "Count":
                                            selectArgs = $"{call.Method.Name.ToLower()}()";
                                            break;
                                        case "Average":
                                            selectArgs = "avg()";
                                            break;
                                        default:
                                            throw new InvalidOperationException("Cannot understand how to translate " + callExpression);
                                    }
                                    break;
                                default:
                                    throw new InvalidOperationException("Cannot understand how to translate " + callExpression);

                            }

                            select = $" select {selectArgs}";

                            break;
                    }

                    mce = inner;
                    continue;
                }

                tsName = (mce.Arguments[mce.Arguments.Count - 1] as ConstantExpression)?.Value.ToString();
                if (mce.Arguments.Count == 2)
                {
                    var path = mce.Arguments[0].ToString();
                    //tsName = path + "." + tsName;
                }

                break;

            }

            if (tsName == default)
                throw new InvalidOperationException("Cannot understand how to translate " + callExpression);

            var expressionBuilder = new StringBuilder();

            expressionBuilder.Append("from ").Append(tsName);

            if (where != null)
                expressionBuilder.Append(where);
            if (groupBy != null)
                expressionBuilder.Append(groupBy);
            if (select != null)
                expressionBuilder.Append(select);

            string[] args =
            {
                expressionBuilder.ToString()
            };

            return new Result
            {
                MemberType = typeof(IRavenQueryable<TimeSeriesValue>),
                IsNestedPath = false,
                Path = "timeseries",
                Args = args
            };
        }

        private static string HandleMemberExpressionPropertyRenames(MemberExpression memberExpression, string name)
        {
            var member = memberExpression.Member;

            if (memberExpression.Expression is MemberExpression innerMemberExpression)
            {
                var innerName = name.Substring(0, name.Length - member.Name.Length - 1);
                name = HandleMemberExpressionPropertyRenames(innerMemberExpression, innerName);

                if (member.Name != "Value" ||
                    Nullable.GetUnderlyingType(memberExpression.Expression.Type) == null)
                {
                    name += $".{member.Name}";
                }
            }

            return HandlePropertyRenames(member, name);
        }

        public static string HandlePropertyRenames(MemberInfo member, string name)
        {
            var jsonPropAttributes = member.GetCustomAttributes(false)
                                                     .OfType<JsonPropertyAttribute>()
                                                     .ToArray();

            if (jsonPropAttributes.Length != 0)
            {
                string propertyName = jsonPropAttributes[0].PropertyName;
                if (string.IsNullOrEmpty(propertyName) == false)
                {
                    return name.Substring(0, name.Length - member.Name.Length) + propertyName;
                }
            }

            var dataMemberAttributes = member.GetCustomAttributes(false)
                                                       .OfType<DataMemberAttribute>()
                                                       .ToArray();

            if (dataMemberAttributes.Length != 0)
            {
                string propertyName = ((dynamic)dataMemberAttributes[0]).Name;
                if (string.IsNullOrEmpty(propertyName) == false)
                {
                    return name.Substring(0, name.Length - member.Name.Length) + propertyName;
                }
            }
            return name;
        }

        private static Expression SimplifyExpression(Expression expression)
        {
            while (true)
            {
                switch (expression.NodeType)
                {
                    case ExpressionType.Convert:
                    case ExpressionType.ConvertChecked:
                    case ExpressionType.Quote:
                        expression = ((UnaryExpression)expression).Operand;
                        break;
                    case ExpressionType.Lambda:
                        expression = ((LambdaExpression)expression).Body;
                        break;
                    default:
                        return expression;
                }
            }
        }

        /// <summary>
        /// Get the actual value from the expression
        /// </summary>
        public object GetValueFromExpression(Expression expression, Type type)
        {
            if (expression == null)
                throw new ArgumentNullException(nameof(expression));

            // Get object
            if (GetValueFromExpressionWithoutConversion(expression, out var value))
            {
                var nonNullableType = Nullable.GetUnderlyingType(type) ?? type;
                if (value is Enum || nonNullableType.GetTypeInfo().IsEnum)
                {
                    if (value is IEnumerable valueAsEnumerable)
                    {
                        return valueAsEnumerable
                            .Cast<object>()
                            .Select(x => ConvertEnum(x, nonNullableType));
                    }

                    return ConvertEnum(value, nonNullableType);
                }

                return value;
            }

            throw new InvalidOperationException("Can't extract value from expression of type: " + expression.NodeType);

            object ConvertEnum(object val, Type enumType)
            {
                if (val == null)
                    return null;
                if (_conventions.SaveEnumsAsIntegers == false)
                    return Enum.GetName(enumType, val);
                return Convert.ToInt32(val);
            }
        }


        /// <summary>
        /// Get the member expression from the expression
        /// </summary>
        public static MemberExpression GetMemberExpression(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
                return GetMemberExpression(unaryExpression.Operand);

            if (expression is LambdaExpression lambdaExpression)
                return GetMemberExpression(lambdaExpression.Body);

            if (!(expression is MemberExpression memberExpression))
            {
                throw new InvalidOperationException("Could not understand how to translate '" + expression + "' to a RavenDB query." +
                                                    Environment.NewLine +
                                                    "Are you trying to do computation during the query?" + Environment.NewLine +
                                                    "RavenDB doesn't allow computation during the query, computation is only allowed during index. Consider moving the operation to an index.");
            }

            return memberExpression;
        }


        public static bool GetValueFromExpressionWithoutConversion(Expression expression, out object value)
        {
            switch (expression.NodeType)
            {
                case ExpressionType.Constant:
                    value = ((ConstantExpression)expression).Value;
                    return true;
                case ExpressionType.MemberAccess:
                    value = GetMemberValue(((MemberExpression)expression));
                    return true;
                case ExpressionType.MemberInit:
                    var memberInitExpression = ((MemberInitExpression)expression);
                    value = Expression.Lambda(memberInitExpression).Compile().DynamicInvoke();
                    return true;
                case ExpressionType.New:
                    value = GetNewExpressionValue(expression);
                    return true;
                case ExpressionType.Lambda:
                    var lambda = ((LambdaExpression)expression);
                    value = lambda.Compile().DynamicInvoke();
                    return true;
                case ExpressionType.Call:
                    if (expression is MethodCallExpression mce)
                    {
                        if (mce.Method.DeclaringType == typeof(RavenQuery) &&
                            mce.Method.Name == nameof(RavenQuery.CmpXchg))
                        {
                            if (TryGetMethodArguments(mce, out var args) == false)
                            {
                                value = null;
                                return false;
                            }
                            value = CmpXchg.Value((string)args[0]);
                            return true;
                        }
                    }
                    value = Expression.Lambda(expression).Compile().DynamicInvoke();
                    return true;
                case ExpressionType.Convert:
                    var unaryExpression = (UnaryExpression)expression;
                    if (unaryExpression.Type.IsNullableType())
                        return GetValueFromExpressionWithoutConversion(unaryExpression.Operand, out value);
                    value = Expression.Lambda(expression).Compile().DynamicInvoke();
                    return true;
                case ExpressionType.NewArrayInit:
                    var expressions = ((NewArrayExpression)expression).Expressions;
                    var values = new object[expressions.Count];
                    value = null;
                    if (expressions.Where((t, i) => !GetValueFromExpressionWithoutConversion(t, out values[i])).Any())
                        return false;
                    value = values;
                    return true;
                case ExpressionType.NewArrayBounds:
                    value = null;
                    expressions = ((NewArrayExpression)expression).Expressions;
                    var constantExpression = (ConstantExpression)expressions.FirstOrDefault();
                    if (constantExpression == null)
                        return false;
                    if (constantExpression.Value.GetType() != typeof(int))
                        return false;
                    var length = (int)constantExpression.Value;
                    value = new object[length];
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        private static bool TryGetMethodArguments(MethodCallExpression mce,  out object[] args)
        {
            args = new object[mce.Arguments.Count];
            for (var index = 0; index < mce.Arguments.Count; index++)
            {
                if (mce.Arguments[index].NodeType == ExpressionType.Lambda)
                {
                    args[index] = ((LambdaExpression)mce.Arguments[index]).Compile();
                    continue;
                }
                if (GetValueFromExpressionWithoutConversion(mce.Arguments[index], out var value) == false)
                    return false;
                args[index] = value;
            }
            return true;
        }

        private static object GetNewExpressionValue(Expression expression)
        {
            var newExpression = ((NewExpression)expression);
            var instance = Activator.CreateInstance(newExpression.Type, newExpression.Arguments.Select(e =>
            {
                object o;
                if (GetValueFromExpressionWithoutConversion(e, out o))
                    return o;
                throw new InvalidOperationException("Can't extract value from expression of type: " + expression.NodeType);
            }).ToArray());
            return instance;
        }


        private static object GetMemberValue(MemberExpression memberExpression)
        {
            object obj = null;

            if (memberExpression == null)
                throw new ArgumentNullException(nameof(memberExpression));

            // Get object
            if (memberExpression.Expression is ConstantExpression)
                obj = ((ConstantExpression)memberExpression.Expression).Value;
            else if (memberExpression.Expression is MemberExpression)
                obj = GetMemberValue((MemberExpression)memberExpression.Expression);
            else if (memberExpression.Expression is MethodCallExpression && GetValueFromExpressionWithoutConversion(memberExpression.Expression, out var value) &&
                     value is MethodCall mc)
            {
                mc.AccessPath = memberExpression.Member.Name;
                return mc;
            }
            //Fix for the issue here http://github.com/ravendb/ravendb/issues/#issue/145
            //Needed to allow things like ".Where(x => x.TimeOfDay > DateTime.MinValue)", where Expression is null
            //(applies to DateTime.Now as well, where "Now" is a property
            //can just leave obj as it is because it's not used below (because Member is a MemberInfo), so won't cause a problem
            else if (memberExpression.Expression != null)
                throw new NotSupportedException("Expression type not supported: " + memberExpression.Expression.GetType().FullName);

            if (obj is MethodCall m)
            {
                m.AccessPath += "." + memberExpression.Member.Name;
                return m;
            }
            // Get value
            var memberInfo = memberExpression.Member;
            if (memberInfo is PropertyInfo)
            {
                var property = (PropertyInfo)memberInfo;
                return property.GetValue(obj, null);
            }
            if (memberInfo is FieldInfo)
            {
                var value = Expression.Lambda(memberExpression).Compile().DynamicInvoke();
                return value;
            }
            throw new NotSupportedException("MemberInfo type not supported: " + memberInfo.GetType().FullName);
        }


        private static void AssertNoComputation(MemberExpression memberExpression)
        {
            var cur = memberExpression;

            while (cur != null)
            {
                switch (cur.Expression?.NodeType)
                {
                    case ExpressionType.Call:
                    case ExpressionType.Invoke:
                    case ExpressionType.Add:
                    case ExpressionType.And:
                    case ExpressionType.AndAlso:
                    case ExpressionType.AndAssign:
                    case ExpressionType.Decrement:
                    case ExpressionType.Increment:
                    case ExpressionType.PostDecrementAssign:
                    case ExpressionType.PostIncrementAssign:
                    case ExpressionType.PreDecrementAssign:
                    case ExpressionType.PreIncrementAssign:
                    case ExpressionType.AddAssign:
                    case ExpressionType.AddAssignChecked:
                    case ExpressionType.AddChecked:
                    case ExpressionType.Index:
                    case ExpressionType.Assign:
                    case ExpressionType.Block:
                    case ExpressionType.Conditional:
                    case ExpressionType.ArrayIndex:
                    case null:

                        throw new ArgumentException("Not supported computation: " + memberExpression +
                                                    ". You cannot use computation in RavenDB queries (only simple member expressions are allowed).");
                }
                cur = cur.Expression as MemberExpression;
            }
        }

        internal static string RemoveTransparentIdentifiersIfNeeded(string path)
        {
            while (path.StartsWith(JavascriptConversionExtensions.TransparentIdentifier))
            {
                var indexOf = path.IndexOf(".", StringComparison.Ordinal);
                path = path.Substring(indexOf + 1);
            }

            return path;
        }

        public static bool IsCounterCall(MethodCallExpression mce)
        {
            return mce.Method.DeclaringType == typeof(RavenQuery) && mce.Method.Name == "Counter"
                   || mce.Object?.Type == typeof(ISessionDocumentCounters) && mce.Method.Name == "Get";
        }

        public static bool IsTimeSeriesCall(MethodCallExpression mce)
        {
            MethodCallExpression methodCallExpression = mce;
            while (methodCallExpression != null)
            {
                if (methodCallExpression.Arguments.Count > 0)
                {
                    if (methodCallExpression.Arguments[0] is MethodCallExpression inner)
                    {
                        methodCallExpression = inner;
                        continue;
                    }

                    return methodCallExpression.Method.DeclaringType == typeof(RavenQuery) && methodCallExpression.Method.Name == "TimeSeries";
                }

                break;
            }

            return false;
        }

    }
}

