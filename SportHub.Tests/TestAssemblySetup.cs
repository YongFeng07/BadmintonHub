using QuestPDF.Infrastructure;
using System.Runtime.CompilerServices;

namespace SportHub.Tests;

/// <summary>
/// Runs once per test-assembly load, before any test class is instantiated.
/// Several services (MarkPaidAsync and friends) generate receipt PDFs, so the
/// QuestPDF licence declaration must be in place regardless of which test class
/// the parallel scheduler starts first — not only inside ReceiptPdfGeneratorTests.
/// </summary>
internal static class TestAssemblySetup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }
}
