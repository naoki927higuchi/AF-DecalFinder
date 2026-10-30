using System.Security.Cryptography;

namespace AF.DecalFinder;

internal static class Phase1Seed
{
    // inputs/manifest.json: Phase 1's actual source bytes, not just a matching filename or size.
    internal const string SourceSha256 = "6BBFC36F3ECDD204B40C9BA35D6382DA1AA5005862182D0C5CDE70BAACA53BFB";
    internal static OcrCatalog? Load(string imagePath, Size size, string jsonPath)
    {
        if (size != new Size(7783, 6579)) return null;
        using var image = File.OpenRead(imagePath);
        if (!Convert.ToHexString(SHA256.HashData(image)).Equals(SourceSha256, StringComparison.Ordinal)) return null;
        return OcrCatalog.Load(jsonPath);
    }
}
