﻿using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Devlooped;

/// <summary>
/// Exposes the diagnostics we report so they show up in the IDE with 
/// their full information and links.
/// </summary>
// [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic, LanguageNames.FSharp)]
#pragma warning disable RS1001 // Missing diagnostic analyzer attribute
public class SponsorLinkAnalyzer : DiagnosticAnalyzer
#pragma warning restore RS1001 // Missing diagnostic analyzer attribute
{
    readonly string sponsorable;
    readonly string product;
    ImmutableArray<DiagnosticDescriptor> diagnostics;

    /// <summary>
    /// Initializes the analyzer for the given sponsorable.
    /// </summary>
    /// <param name="sponsorable">The sponsor account that users should sponsor.</param>
    /// <param name="product">The product that is using sponsor link.</param>
    /// <param name="idPrefix">A prefix to prepend to diagnostic IDs generated by SponsorLink, which will always end up in 'SL[ID]', 
    /// for example, if 'DL' is provided, an emitted diagnostic ID would be 'DLSL03'.</param>
    protected SponsorLinkAnalyzer(string sponsorable, string product, string idPrefix)
    {
        this.sponsorable = sponsorable;
        this.product = product;
        diagnostics = SponsorLink.Diagnostics.GetDescriptors(sponsorable, idPrefix);
    }

    /// <summary>
    /// Exposes the supported diagnostics.
    /// </summary>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => diagnostics;

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        if (bool.TryParse(Environment.GetEnvironmentVariable("DEBUG_SPONSORLINK"), out var debug) && debug && 
            !Debugger.IsAttached)
            Debugger.Launch();

        var opt = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        if (!opt.TryGetValue("build_property.MSBuildProjectFullPath", out var projectPath))
            return;

        var diagnostic = SponsorLink.Diagnostics.Pop(sponsorable, product, projectPath);
        if (diagnostic != null)
        {
            SponsorLink.Diagnostics.ReportDiagnosticOnce(context, diagnostic, sponsorable, product);
            return;
        }

        var productDir = Path.Combine(Path.GetDirectoryName(projectPath), "obj", "SponsorLink", sponsorable, product);
        if (!Directory.Exists(productDir))
            return;

        foreach (var file in Directory.EnumerateFiles(productDir, "*.txt"))
        {
            var parts = Path.GetFileName(file).Split('.');
            if (parts.Length < 2)
                continue;

            // We always report here, since this is the easiest to disable by 
            // users. The generator will check for these being disabled and 
            // emit its own in turn (since that's harder to disable becuase 
            // it comes with the same assembly as the consuming project.
            var id = parts[0];
            var severity = parts[1];
            var descriptor = SupportedDiagnostics.FirstOrDefault(x => x.Id == id);
            if (descriptor == null)
                continue;

            var text = File.ReadAllText(file).Trim();
            diagnostic = Diagnostic.Create(new DiagnosticDescriptor(
                id: descriptor.Id,
                title: descriptor.Title,
                messageFormat: text,
                category: descriptor.Category,
                defaultSeverity: descriptor.DefaultSeverity,
                isEnabledByDefault: descriptor.IsEnabledByDefault,
                description: descriptor.Description,
                helpLinkUri: descriptor.HelpLinkUri,
                customTags: descriptor.CustomTags.ToArray()),
                // If we provide a non-null location, the entry for some reason is no longer shown in VS :/
                //Location.Create(projectPath, new TextSpan(), new LinePositionSpan())));
                null);

            SponsorLink.Diagnostics.ReportDiagnosticOnce(context, diagnostic, sponsorable, product);
        }
    }
}
