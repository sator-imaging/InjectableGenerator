using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InjectableGenerator.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InjectableGeneratorCodeFixProvider))]
    [Shared]
    public sealed class InjectableGeneratorCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
            "INJECT003"
        );

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null)
            {
                return;
            }

            foreach (var diagnostic in context.Diagnostics)
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);

                var attributeSyntax = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault()
                    ?? node.FirstAncestorOrSelf<AttributeSyntax>();

                if (attributeSyntax == null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Add Generate method",
                        createChangedSolution: c => AddGenerateMethodAsync(context.Document, attributeSyntax, c),
                        equivalenceKey: "AddGenerateMethod"),
                    diagnostic);
            }
        }

        private async Task<Solution> AddGenerateMethodAsync(Document document, AttributeSyntax attributeSyntax, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel == null)
            {
                return document.Project.Solution;
            }

            // Get the type argument from the attribute
            if (attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not TypeOfExpressionSyntax typeOfExpression)
            {
                return document.Project.Solution;
            }

            var typeSymbol = semanticModel.GetTypeInfo(typeOfExpression.Type, cancellationToken).Type as INamedTypeSymbol;
            if (typeSymbol == null)
            {
                return document.Project.Solution;
            }

            // Find the source document for the type
            var syntaxReference = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxReference == null)
            {
                return document.Project.Solution;
            }

            var targetDocument = document.Project.Solution.GetDocument(syntaxReference.SyntaxTree);
            if (targetDocument == null)
            {
                return document.Project.Solution;
            }

            var targetRoot = await targetDocument.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (targetRoot == null)
            {
                return document.Project.Solution;
            }

            var typeDeclaration = targetRoot.FindNode(syntaxReference.Span) as TypeDeclarationSyntax;
            if (typeDeclaration == null)
            {
                return document.Project.Solution;
            }

            // Create the Generate method
            var generateMethod = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)),
                SyntaxFactory.Identifier("Generate"))
                .WithModifiers(
                    SyntaxFactory.TokenList(
                        SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                .WithParameterList(
                    SyntaxFactory.ParameterList(
                        SyntaxFactory.SeparatedList<ParameterSyntax>(
                            new[]
                            {
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("type"))
                                    .WithType(SyntaxFactory.ParseTypeName("System.Type")),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("isPartial"))
                                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("isRecord"))
                                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword))),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("info"))
                                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)))
                                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("warning"))
                                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)))
                                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("error"))
                                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)))
                                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword)))),
                                SyntaxFactory.Parameter(SyntaxFactory.Identifier("source"))
                                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.OutKeyword)))
                                    .WithType(SyntaxFactory.NullableType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.StringKeyword))))
                            })))
                .WithBody(
                    SyntaxFactory.Block(
                        SyntaxFactory.ParseStatement("throw new NotImplementedException();")))
                .WithAdditionalAnnotations(Formatter.Annotation);

            // Add the method to the class
            var newTypeDeclaration = typeDeclaration.AddMembers(generateMethod);
            var newRoot = targetRoot.ReplaceNode(typeDeclaration, newTypeDeclaration);

            // Add using System; if not present
            var compilationUnit = newRoot as CompilationUnitSyntax;
            if (compilationUnit != null && !compilationUnit.Usings.Any(u => u.Name.ToString() == "System"))
            {
                newRoot = compilationUnit.AddUsings(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")));
            }

            return targetDocument.Project.Solution.WithDocumentSyntaxRoot(targetDocument.Id, newRoot);
        }
    }
}
