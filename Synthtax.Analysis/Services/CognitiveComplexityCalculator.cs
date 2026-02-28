using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synthtax.Analysis.Services;

public static class CognitiveComplexityCalculator
{
    public static int Calculate(MethodDeclarationSyntax method, string methodName)
    {
        var walker = new CognitiveComplexityWalker(methodName);
        walker.Visit(method);
        return walker.Complexity;
    }

    private sealed class CognitiveComplexityWalker : CSharpSyntaxWalker
    {
        private readonly string _methodName;
        private int _complexity;
        private int _nestingLevel;

        public int Complexity => _complexity;

        public CognitiveComplexityWalker(string methodName)
            : base(SyntaxWalkerDepth.Node)
        {
            _methodName = methodName;
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            bool isElseIf = node.Parent is ElseClauseSyntax;
            if (isElseIf)
            {
                _complexity += 1;
                VisitBooleanSequences(node.Condition);
                _nestingLevel++;
                Visit(node.Statement);
                _nestingLevel--;
            }
            else
            {
                IncrementWithNesting();
                VisitBooleanSequences(node.Condition);
                _nestingLevel++;
                Visit(node.Statement);
                _nestingLevel--;
            }
            if (node.Else is not null)
            {
                if (node.Else.Statement is not IfStatementSyntax)
                {
                    _complexity += 1;
                    _nestingLevel++;
                    Visit(node.Else.Statement);
                    _nestingLevel--;
                }
                else
                {
                    Visit(node.Else.Statement);
                }
            }
        }

        public override void VisitForStatement(ForStatementSyntax node)
        { IncrementWithNesting(); _nestingLevel++; DefaultVisit(node); _nestingLevel--; }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        { IncrementWithNesting(); _nestingLevel++; DefaultVisit(node); _nestingLevel--; }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            IncrementWithNesting();
            VisitBooleanSequences(node.Condition);
            _nestingLevel++;
            Visit(node.Statement);
            _nestingLevel--;
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            IncrementWithNesting();
            VisitBooleanSequences(node.Condition);
            _nestingLevel++;
            Visit(node.Statement);
            _nestingLevel--;
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            foreach (var section in node.Sections) Visit(section);
            _nestingLevel--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            foreach (var arm in node.Arms) Visit(arm);
            _nestingLevel--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        { IncrementWithNesting(); _nestingLevel++; Visit(node.Block); _nestingLevel--; }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            Visit(node.WhenTrue);
            Visit(node.WhenFalse);
            _nestingLevel--;
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        { IncrementWithNesting(); _nestingLevel++; Visit(node.Body); _nestingLevel--; }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        { IncrementWithNesting(); _nestingLevel++; Visit(node.Body); _nestingLevel--; }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            if (node.Body is not null)          Visit(node.Body);
            if (node.ExpressionBody is not null) Visit(node.ExpressionBody);
            _nestingLevel--;
        }

        public override void VisitGotoStatement(GotoStatementSyntax node) => _complexity += 1;
        public override void VisitBreakStatement(BreakStatementSyntax node) { }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var name = node.Expression switch
            {
                IdentifierNameSyntax id       => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _                              => null
            };
            if (name is not null &&
                string.Equals(name, _methodName, StringComparison.Ordinal))
                _complexity += 1;

            DefaultVisit(node);
        }

        private void VisitBooleanSequences(ExpressionSyntax? expression)
        {
            if (expression is null) return;
            var operators = CollectBooleanOperators(expression);
            SyntaxKind? lastKind = null;
            foreach (var kind in operators)
            {
                if (kind != lastKind) { _complexity += 1; lastKind = kind; }
            }
        }

        private static List<SyntaxKind> CollectBooleanOperators(SyntaxNode node)
        {
            var result = new List<SyntaxKind>();
            foreach (var descendant in node.DescendantNodesAndSelf())
            {
                if (descendant is BinaryExpressionSyntax bin)
                {
                    var kind = bin.Kind();
                    if (kind is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression)
                        result.Add(kind);
                }
            }
            return result;
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            var kind = node.Kind();
            if (kind is not SyntaxKind.LogicalAndExpression
                    and not SyntaxKind.LogicalOrExpression)
                DefaultVisit(node);
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            Visit(node.Block);
            foreach (var c in node.Catches) Visit(c);
            if (node.Finally is not null) Visit(node.Finally);
        }

        public override void VisitElseClause(ElseClauseSyntax node) { }

        private void IncrementWithNesting() => _complexity += 1 + _nestingLevel;
    }
}
