using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Synthtax.API.Services.Analysis;

/// <summary>
/// Cognitive Complexity as defined by G. Ann Campbell / SonarSource.
///
/// Rules (simplified):
///   1. Each control-flow structure adds 1 + current nesting depth.
///   2. Sequences of the same binary logical operator (&&, ||) count as ONE, not per-operator.
///   3. Jumps out of normal flow (break, continue, goto) add 1 with no nesting bonus.
///   4. Recursion adds 1.
///   5. The ternary operator ? : adds 1 with nesting bonus.
///
/// Key difference from Cyclomatic: cognitive complexity rewards readable nesting
/// by penalising deeply nested code MORE than flat equivalent logic.
/// </summary>
public static class CognitiveComplexityCalculator
{
    public static int Calculate(MethodDeclarationSyntax method, SemanticModel? model = null)
    {
        var visitor = new CognitiveVisitor(method, model);
        visitor.Visit(method);
        return visitor.Score;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private sealed class CognitiveVisitor : CSharpSyntaxWalker
    {
        private readonly MethodDeclarationSyntax _targetMethod;
        private readonly SemanticModel? _model;
        private int _nesting;
        public int Score { get; private set; }

        public CognitiveVisitor(MethodDeclarationSyntax method, SemanticModel? model)
            : base(SyntaxWalkerDepth.Node)
        {
            _targetMethod = method;
            _model = model;
        }

        // ── Structural increments (1 + nesting) ──────────────────────────────

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            // else-if does NOT add nesting; it is already counted as an else branch
            bool isElseIf = node.Parent is ElseClauseSyntax;
            Increment(isElseIf ? 0 : _nesting);   // +1 is implied inside Increment()

            _nesting++;
            Visit(node.Condition);
            Visit(node.Statement);
            _nesting--;

            if (node.Else is not null)
            {
                Increment(0); // else adds +1 but NO nesting bonus
                // Don't increase nesting for else itself; the body of else-if will bump it
                if (node.Else.Statement is IfStatementSyntax nestedIf)
                {
                    // Recurse at same nesting — the VisitIfStatement above will add its +1
                    VisitIfStatement(nestedIf);
                }
                else
                {
                    _nesting++;
                    Visit(node.Else.Statement);
                    _nesting--;
                }
            }
        }

        public override void VisitWhileStatement(WhileStatementSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitForStatement(ForStatementSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitDoStatement(DoStatementSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitSwitchStatement(SwitchStatementSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitCatchClause(CatchClauseSyntax node)
        {
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            // Ternary: +1 + nesting
            Increment(_nesting);
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        // ── Logical operator sequences ────────────────────────────────────────

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            // Only count at the ROOT of a logical chain to avoid double-counting.
            // e.g. a && b && c is ONE increment (not two).
            bool isLogical = node.IsKind(SyntaxKind.LogicalAndExpression)
                          || node.IsKind(SyntaxKind.LogicalOrExpression);
            bool parentIsSameKind = node.Parent is BinaryExpressionSyntax parent
                                    && parent.IsKind(node.Kind());

            if (isLogical && !parentIsSameKind)
                Increment(0); // flat +1, no nesting bonus

            DefaultVisit(node);
        }

        // ── Jumps (flat +1, no nesting bonus) ────────────────────────────────

        public override void VisitBreakStatement(BreakStatementSyntax _) => Increment(0);
        public override void VisitContinueStatement(ContinueStatementSyntax _) => Increment(0);
        public override void VisitGotoStatement(GotoStatementSyntax _) => Increment(0);

        // ── Recursion ─────────────────────────────────────────────────────────

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (_model is not null)
            {
                var sym = _model.GetSymbolInfo(node).Symbol as IMethodSymbol;
                if (sym is not null)
                {
                    var targetSym = _model.GetDeclaredSymbol(_targetMethod) as IMethodSymbol;
                    if (targetSym is not null &&
                        SymbolEqualityComparer.Default.Equals(sym.OriginalDefinition,
                                                               targetSym.OriginalDefinition))
                        Increment(0); // recursive call
                }
            }
            DefaultVisit(node);
        }

        // ── Nested lambdas/local functions increase nesting ───────────────────

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            _nesting++;
            DefaultVisit(node);
            _nesting--;
        }

        // ── Utility ───────────────────────────────────────────────────────────

        /// <summary>Add 1 (the structural increment) plus the nesting penalty.</summary>
        private void Increment(int nestingBonus) => Score += 1 + nestingBonus;
    }
}
