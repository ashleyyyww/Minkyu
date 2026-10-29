namespace Android.Content
{
    public sealed class Context(string files, string assets)
    {
        public Context ApplicationContext => this;
        public PrivateDirectory FilesDir => new(files);
        public AssetManager Assets => new(assets);
    }
    public sealed class PrivateDirectory(string path)
    {
        public string AbsolutePath => path;
    }
    public sealed class AssetManager(string root)
    {
        public Stream Open(string name) => File.OpenRead(Path.Combine(root, name));
    }
}
