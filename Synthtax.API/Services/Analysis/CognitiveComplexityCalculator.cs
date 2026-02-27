using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Calculates Cognitive Complexity following the SonarSource specification.
/// https://www.sonarsource.com/docs/CognitiveComplexity.pdf
///
/// Key differences from Cyclomatic Complexity:
/// - Structural elements that cause nesting accumulate a penalty per nesting level.
/// - Boolean operator sequences count once per sequence, not per operator.
/// - Recursion is penalised (flat +1).
/// - Lambdas and local functions increase the nesting context.
/// </summary>
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

        // ── Structural increments (add nesting level + increment) ──────────────

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // "else if" is part of the else branch of the outer if – treat as flat +1
            bool isElseIf = node.Parent is ElseClauseSyntax;

            if (isElseIf)
            {
                _complexity += 1; // flat, no extra nesting penalty
                // Visit the condition and body but don't increment nesting again
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
                // "else" itself counts +1 (flat)
                if (node.Else.Statement is not IfStatementSyntax)
                {
                    _complexity += 1;
                    _nestingLevel++;
                    Visit(node.Else.Statement);
                    _nestingLevel--;
                }
                else
                {
                    // else-if: handled on the recursive visit above
                    Visit(node.Else.Statement);
                }
            }
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            DefaultVisit(node);
            _nestingLevel--;
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            DefaultVisit(node);
            _nestingLevel--;
        }

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
            foreach (var section in node.Sections)
                Visit(section);
            _nestingLevel--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            // Switch expressions are structural, increment with nesting
            IncrementWithNesting();
            _nestingLevel++;
            foreach (var arm in node.Arms)
                Visit(arm);
            _nestingLevel--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            Visit(node.Block);
            _nestingLevel--;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            // Ternary operator – structural increment with nesting
            IncrementWithNesting();
            _nestingLevel++;
            Visit(node.WhenTrue);
            Visit(node.WhenFalse);
            _nestingLevel--;
        }

        // ── Lambda and local functions: increase nesting context ───────────────

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            Visit(node.Body);
            _nestingLevel--;
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            Visit(node.Body);
            _nestingLevel--;
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            IncrementWithNesting();
            _nestingLevel++;
            if (node.Body is not null) Visit(node.Body);
            if (node.ExpressionBody is not null) Visit(node.ExpressionBody);
            _nestingLevel--;
        }

        // ── Flat increments ────────────────────────────────────────────────────

        public override void VisitGotoStatement(GotoStatementSyntax node)
        {
            _complexity += 1; // goto always breaks linear flow
        }

        public override void VisitBreakStatement(BreakStatementSyntax node)
        {
            // Only penalise labelled break (break to outer loop in C# is rare but possible via goto)
            // In standard C#, break inside switch/loop is idiomatic – don't penalise
            // SonarSource: penalise break only with label. C# doesn't have labelled break,
            // so we skip this for standard C# code.
        }

        // ── Recursion detection ────────────────────────────────────────────────

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var name = node.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                _ => null
            };

            if (name is not null &&
                string.Equals(name, _methodName, StringComparison.Ordinal))
            {
                _complexity += 1; // recursive call: flat +1
            }

            DefaultVisit(node);
        }

        // ── Boolean sequence handling ──────────────────────────────────────────

        /// <summary>
        /// Counts distinct boolean operator sequences in an expression.
        /// a &amp;&amp; b &amp;&amp; c = +1 (one sequence of &amp;&amp;)
        /// a &amp;&amp; b || c = +2 (two sequences: one &amp;&amp;, one ||)
        /// </summary>
        private void VisitBooleanSequences(ExpressionSyntax? expression)
        {
            if (expression is null) return;

            // We flatten the expression tree and look for operator-kind changes
            var operators = CollectBooleanOperators(expression);
            if (operators.Count == 0) return;

            SyntaxKind? lastKind = null;
            foreach (var kind in operators)
            {
                if (kind != lastKind)
                {
                    _complexity += 1;
                    lastKind = kind;
                }
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

        // ── Override default visit to suppress double-visiting ─────────────────

        // Suppress default visits for nodes we handle manually to avoid double-counting
        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            // Boolean sequences are handled by VisitBooleanSequences, not here
            // For non-boolean binary expressions, visit children normally
            var kind = node.Kind();
            if (kind is not SyntaxKind.LogicalAndExpression and
                not SyntaxKind.LogicalOrExpression)
            {
                DefaultVisit(node);
            }
        }

        public override void VisitTryStatement(TryStatementSyntax node)
        {
            // Try itself doesn't increment; catch clauses do (handled by VisitCatchClause)
            Visit(node.Block);
            foreach (var c in node.Catches)
                Visit(c);
            if (node.Finally is not null)
                Visit(node.Finally);
        }

        public override void VisitElseClause(ElseClauseSyntax node)
        {
            // Handled explicitly in VisitIfStatement
        }

        // ── Helper ─────────────────────────────────────────────────────────────

        private void IncrementWithNesting()
        {
            _complexity += 1 + _nestingLevel;
        }
    }
}
