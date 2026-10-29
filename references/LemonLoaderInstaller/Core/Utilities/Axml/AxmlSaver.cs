using System.IO;

namespace QuestPatcher.Axml
{
    public static class AxmlSaver
    {
        public static void SaveDocument(Stream stream, AxmlElement rootElement)
        {
            BinaryWriter mainOutput = new BinaryWriter(stream);


            SavingContext ctx = new SavingContext();
            rootElement.PreparePooling(ctx);
            string[] stringPool = ctx.StringPool.PrepareForSavePhase(ctx.ResourceMap);

            rootElement.Save(ctx);
            MemoryStream mainChunkStream = (MemoryStream)ctx.Writer.BaseStream;


            int[] resourcePool = ctx.ResourceMap.Save();

            int stringPoolLength = StringPoolSerializer.CalculatePoolLength(stringPool);
            int stringPoolPadding = (4 - stringPoolLength % 4) % 4;
            stringPoolLength += stringPoolPadding;

            int resourcePoolLength = resourcePool.Length * 4;


            mainOutput.WriteChunkHeader(ResourceType.Xml, stringPoolLength + resourcePoolLength + (int)mainChunkStream.Position + 16);

            mainOutput.WriteChunkHeader(ResourceType.StringPool, stringPoolLength);
            StringPoolSerializer.SaveStringPool(stringPool, mainOutput);
            for (int i = 0; i < stringPoolPadding; i++)
            {
                mainOutput.Write((byte)0);
            }

            mainOutput.WriteChunkHeader(ResourceType.XmlResourceMap, resourcePoolLength);
            foreach (int resource in resourcePool)
            {
                mainOutput.Write(resource);
            }


            mainChunkStream.Position = 0;
            mainChunkStream.CopyTo(stream);
        }
    }
}
