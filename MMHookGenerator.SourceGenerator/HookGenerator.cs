using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MMHookGenerator.SourceGenerator
{
    [Generator]
    public class HookGenerator : ISourceGenerator
    {
        private enum HookType { On, IL }

        public void Execute(GeneratorExecutionContext context)
        {
            var syntaxReceiver = context.SyntaxReceiver as HookSyntaxReceiver;
            foreach (var sourceFile in ExtractSourceFiles(context, HookType.IL, syntaxReceiver.ILHook))
            {
                GenerateSource(context, sourceFile);
            }
            foreach (var sourceFile in ExtractSourceFiles(context, HookType.On, syntaxReceiver.OnHook))
            {
                GenerateSource(context, sourceFile);
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
#if DEBUG
            //if (!Debugger.IsAttached)
            //{
            //    Debugger.Launch();
            //}
#endif 
            context.RegisterForSyntaxNotifications(() => new HookSyntaxReceiver());
        }

        private IEnumerable<HookSourceFile> ExtractSourceFiles(GeneratorExecutionContext context, HookType hookType, List<MemberAccessExpressionSyntax> members)
        {
            var filesByType = new Dictionary<string, HookSourceFile>();

            foreach (var member in members)
            {
                var typeText = member.Expression.GetText().ToString().Trim();
                var typeSymbol = context.Compilation.GetTypeByMetadataName(typeText.Substring(3));
                if (typeSymbol == null)
                {
                    continue;
                }

                var memberText = member.GetText().ToString().Trim();
                var methodName = memberText.Substring(memberText.LastIndexOf('.') + 1);
                IMethodSymbol methodSymbol = null;

                foreach (var typeMember in typeSymbol.GetMembers())
                {
                    if (typeMember.Kind != SymbolKind.Method)
                    {
                        continue;
                    }
                    if (typeMember.Name == methodName)
                    {
                        methodSymbol = typeMember as IMethodSymbol;
                        break;
                    }
                }

                if (methodSymbol == null)
                {
                    continue;
                }

                if (!filesByType.TryGetValue(typeText, out var file))
                {
                    filesByType[typeText] = file = new HookSourceFile
                    {
                        prefix = hookType,
                        type = typeSymbol
                    };
                }
                
                file.methods.Add(methodSymbol);
            }

            return filesByType.Values;
        }

        private void GenerateSource(GeneratorExecutionContext context, HookSourceFile sourceFile)
        {
            var namespaceText = sourceFile.type.ContainingNamespace?.ToDisplayString();
            var namespaceEmpty = string.IsNullOrWhiteSpace(namespaceText);

            var sourceBuilder = new StringBuilder($@"
using MonoMod.Cil;
using MonoMod.RuntimeDetour.HookGen;
using System;
using System.Reflection;

namespace {sourceFile.prefix}{(namespaceEmpty ? "" : $".{namespaceText}")}
{{
");

            AppendClass(sourceBuilder, sourceFile);

            sourceBuilder.Append(@"
}");
            context.AddSource($"{sourceFile.prefix}{(namespaceEmpty ? "" : $".{namespaceText}")}.{sourceFile.type.Name}.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        private void AppendClass(StringBuilder sourceBuilder, HookSourceFile file)
        {
            var typesCount = AppendClassHeaderRecursive(sourceBuilder, file.type);

            foreach (var method in file.methods)
            {
                sourceBuilder.Append(GenerateHookText(file.prefix, file.type, method));
            }

            for (var i = 0; i < typesCount; i++)
            {
                sourceBuilder.Append(@"
    }");
            }
        }

        private int AppendClassHeaderRecursive(StringBuilder sourceBuilder, INamedTypeSymbol typeSymbol)
        {
            var count = 1;
            if (typeSymbol.ContainingType != null)
            {
                count += AppendClassHeaderRecursive(sourceBuilder, typeSymbol.ContainingType);
            }

            sourceBuilder.Append($@"
    public static partial class {typeSymbol.Name}
    {{");

            return count;
        }

        private string GenerateHookText(HookType prefix, INamedTypeSymbol typeSymbol, IMethodSymbol method)
        {
            switch (prefix)
            {
                case HookType.On:
                    return GenerateOnHookText(typeSymbol, method);
                case HookType.IL:
                    return GenerateILHookText(typeSymbol, method);
                default:
                    throw new NotSupportedException($"Prefix '{prefix}' is not supported");
            }
        }

        private string GenerateOnHookText(INamedTypeSymbol typeSymbol, IMethodSymbol method)
        {
            var displayString = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var hookType = GenerateHookType(typeSymbol, method);

            return $@"
        public static event {hookType} {method.Name}
        {{
            add
            {{
                HookEndpointManager.Add(typeof({displayString}).GetMethod(""{method.Name}"", (BindingFlags)60), value);
            }}
            remove
            {{
                HookEndpointManager.Remove(typeof({displayString}).GetMethod(""{method.Name}"", (BindingFlags)60), value);
            }}
        }}";
        }

        private string GenerateHookType(ITypeSymbol typeSymbol, IMethodSymbol method)
        {
            var delegateType = method.ReturnsVoid ? "Action" : "Func";
            var hasParameters = !method.ReturnsVoid || method.Parameters.Length != 0;
            var parametersString = "";

            if (hasParameters)
            {
                var parameters = new List<ITypeSymbol>();
                if (!method.IsStatic)
                {
                    parameters.Add(typeSymbol);
                }

                foreach (var parameter in method.Parameters)
                {
                    parameters.Add(parameter.Type);
                }

                if (!method.ReturnsVoid)
                {
                    parameters.Add(method.ReturnType);
                }

                parametersString = string.Join(", ", parameters.Select(param => param.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            if (hasParameters)
            {
                return string.Format("{0}<{0}<{1}>, {1}>", delegateType, parametersString);
            }
            else
            {
                return string.Format("{0}<{0}>", delegateType);
            }
        }

        private string GenerateILHookText(INamedTypeSymbol typeSymbol, IMethodSymbol method)
        {
            var displayString = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $@"
        public static event ILContext.Manipulator {method.Name}
        {{
            add
            {{
                HookEndpointManager.Modify(typeof({displayString}).GetMethod(""{method.Name}"", (BindingFlags)60), value);
            }}
            remove
            {{
                HookEndpointManager.Unmodify(typeof({displayString}).GetMethod(""{method.Name}"", (BindingFlags)60), value);
            }}
        }}";
        }

        private class HookSourceFile
        {
            public HookType prefix;
            public INamedTypeSymbol type;
            public readonly List<IMethodSymbol> methods = new List<IMethodSymbol>();
        }

        private class HookSyntaxReceiver : ISyntaxReceiver
        {
            public readonly List<MemberAccessExpressionSyntax> OnHook = new List<MemberAccessExpressionSyntax>();
            public readonly List<MemberAccessExpressionSyntax> ILHook = new List<MemberAccessExpressionSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (!(syntaxNode is AssignmentExpressionSyntax assigmentNode))
                {
                    return;
                }
                if (!(assigmentNode.IsKind(SyntaxKind.SubtractAssignmentExpression) || assigmentNode.IsKind(SyntaxKind.AddAssignmentExpression)))
                {
                    return;
                }
                if (!(assigmentNode.Left is MemberAccessExpressionSyntax memberAccess))
                {
                    return;
                }

                var valueText = memberAccess.GetText().ToString().Trim();
                if (valueText.StartsWith("On."))
                {
                    if (OnHook.All(member => member.GetText().ToString().Trim() != valueText))
                    {
                        OnHook.Add(memberAccess);
                    }
                }
                else if (valueText.StartsWith("IL."))
                {
                    if (ILHook.All(member => member.GetText().ToString().Trim() != valueText))
                    {
                        ILHook.Add(memberAccess);
                    }
                }
            }
        }
    }
}
