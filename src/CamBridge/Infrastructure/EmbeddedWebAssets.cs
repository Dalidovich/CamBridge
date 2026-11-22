using Microsoft.Extensions.FileProviders;

namespace CamBridge.Infrastructure;

public sealed class EmbeddedWebAssets
{
    private const string ResourceNamespace = "CamBridge.wwwroot";

    public EmbeddedWebAssets()
    {
        FileProvider = new EmbeddedFileProvider(typeof(EmbeddedWebAssets).Assembly, ResourceNamespace);
    }

    public IFileProvider FileProvider { get; }

    public IFileInfo IndexPage => FileProvider.GetFileInfo("index.html");
}
