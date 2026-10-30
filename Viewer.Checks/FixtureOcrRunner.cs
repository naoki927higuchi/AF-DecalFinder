using AF.DecalFinder;
namespace AF.DecalFinder.Checks;

// Existing interaction regressions use a deterministic recognition fixture;
// OcrChecks separately exercises the real subprocess and all eight input images.
internal sealed class FixtureOcrRunner(string root) : IOcrRunner
{
    public Task<OcrCatalog> RunAsync(ImageIdentity image, CancellationToken cancellation) => Task.FromResult(
        Phase1Seed.Load(image.AbsolutePath, new Size(image.Width, image.Height), Path.Combine(root, "results", "600dpi.json"))
        ?? new OcrCatalog(new Size(image.Width, image.Height), []));
}
