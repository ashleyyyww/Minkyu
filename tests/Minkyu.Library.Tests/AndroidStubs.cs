namespace Android.Content
{
    public sealed class Context(string directory)
    {
        public TestDirectory FilesDir { get; } = new(directory);
    }

    public sealed class TestDirectory(string path)
    {
        public string AbsolutePath => path;
    }
}
