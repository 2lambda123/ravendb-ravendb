using System;

namespace Raven.Server.Documents.Queries.AST
{
    public class BinaryExpression : QueryExpression
    {
        public QueryExpression Left;
        public OperatorType Operator;
        public QueryExpression Right;

        public BinaryExpression(QueryExpression left, QueryExpression right, OperatorType op)
        {
            Left = left;
            Right = right;
            Operator = op;
            Type = ExpressionType.Binary;
        }

        public override string ToString()
        {
            return Left + " " + Operator + " " + Right;
        }

        public override string GetText()
        {
            string op;
            switch (Operator)
            {
                case OperatorType.Equal:
                    op = "=";
                    break;
                case OperatorType.NotEqual:
                    op = "!=";
                    break;
                case OperatorType.LessThan:
                    op = "<";
                    break;
                case OperatorType.GreaterThan:
                    op = ">";
                    break;
                case OperatorType.LessThanEqual:
                    op = "<=";
                    break;
                case OperatorType.GreaterThanEqual:
                    op = ">=";
                    break;
                case OperatorType.And:
                    op = "AND";
                    break;
                case OperatorType.AndNot:
                    op = "AND NOT";
                    break;
                case OperatorType.Or:
                    op = "OR";
                    break;
                case OperatorType.OrNot:
                    op = "OR NOT";
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return Left.GetText() + " " + op + " " + Right.GetText();
        }
    }
}
