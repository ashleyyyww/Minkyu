using System.IO;

namespace QuestPatcher.Axml
{
    internal static class BinaryStreamExtensions
    {
        internal static ResourceType ReadResourceType(this BinaryReader reader)
        {
            int t = reader.ReadInt32();
            return (ResourceType)(t & 0xFFFF);
        }

        internal static void WriteChunkHeader(this BinaryWriter writer, ResourceType typeEnum, int length = 0)
        {
            length += 8;

            int typePrefix = typeEnum switch
            {
                ResourceType.Xml => 0x0008,
                ResourceType.StringPool => 0x001C,
                ResourceType.XmlResourceMap => 0x0008,
                _ => 0x0010
            };
            writer.Write((int)typeEnum | typePrefix << 16);
            writer.Write(length);
        }
    }
}
