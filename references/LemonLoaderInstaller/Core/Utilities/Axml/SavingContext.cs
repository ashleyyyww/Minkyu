using System.IO;

namespace QuestPatcher.Axml
{
    internal class SavingContext
    {
        public StringPool StringPool { get; } = new StringPool();

        public ResourceMap ResourceMap { get; } = new ResourceMap();

        public BinaryWriter Writer { get; } = new BinaryWriter(new MemoryStream());
    }
}
