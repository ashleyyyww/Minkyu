using System.IO;
using System.Text;

namespace QuestPatcher.Axml
{
    internal static class StringPoolSerializer
    {
        private const int Utf8Flag = 0x00000100;

        internal static string[] LoadStringPool(BinaryReader input)
        {
            int beginChunkOffset = (int)input.BaseStream.Position - 8;
            int numStrings = input.ReadInt32();
            input.ReadInt32();

            int flags = input.ReadInt32();
            bool useUtf8 = (flags & Utf8Flag) != 0;

            int stringsOffset = input.ReadInt32();
            input.ReadInt32();

            int[] stringBeginOffsets = new int[numStrings];
            for (int i = 0; i < numStrings; i++)
            {
                stringBeginOffsets[i] = input.ReadInt32();
            }

            string[] result = new string[numStrings];

            int stringsBeginning = beginChunkOffset + stringsOffset;
            for (int i = 0; i < numStrings; i++)
            {
                input.BaseStream.Position = stringsBeginning + stringBeginOffsets[i];

                string currentStr;
                if (useUtf8)
                {
                    ReadUtf8Length(input);
                    int statedLength = ReadUtf8Length(input);

                    MemoryStream stringStream = new MemoryStream();
                    BinaryWriter stringWriter = new BinaryWriter(stringStream);
                    stringWriter.Write(input.ReadBytes(statedLength));
                    while (true)
                    {
                        byte b = input.ReadByte();
                        if (b == 0)
                        {
                            break;
                        }

                        stringWriter.Write(b);
                    }

                    currentStr = Encoding.UTF8.GetString(stringStream.ToArray());
                }
                else
                {
                    int length = ReadUtf16Length(input);

                    byte[] test = input.ReadBytes(length * 2);
                    currentStr = Encoding.Unicode.GetString(test);
                }

                result[i] = currentStr;
            }

            return result;
        }

        internal static int CalculatePoolLength(string[] pool)
        {
            int stringsLength = 0;
            foreach (string str in pool)
            {
                stringsLength += CalculatePooledStringLength(str);
            }

            return 20 + pool.Length * 4 + stringsLength;
        }

        internal static void SaveStringPool(string[] pool, BinaryWriter writer)
        {
            writer.Write(pool.Length);
            writer.Write(0);
            writer.Write(0);




            writer.Write(7 * 4 + pool.Length * 4);
            writer.Write(0);

            int currentStringPos = 0;
            foreach (string str in pool)
            {
                writer.Write(currentStringPos);
                currentStringPos += CalculatePooledStringLength(str);
            }

            foreach (string str in pool)
            {
                WriteUtf16Length(writer, str);
                writer.Write(Encoding.Unicode.GetBytes(str));
                writer.Write((byte)0);
                writer.Write((byte)0);
            }
        }

        private static int ReadUtf8Length(BinaryReader input)
        {
            int length = input.ReadByte();
            if ((length & 0x80) != 0)
            {

                length = ((length & 0x7F) << 8) | input.ReadByte();
            }

            return length;
        }

        private static int ReadUtf16Length(BinaryReader input)
        {
            int length = input.ReadInt16();

            if (length > 0x7FFF)
            {

                length = ((length & 0x7FFF) << 8) | input.ReadUInt16();
            }
            return length;
        }

        private static void WriteUtf16Length(BinaryWriter output, string str)
        {
            if (str.Length > 0x7FFF)
            {
                int x = (str.Length >> 16) | 0x8000;
                output.Write((byte)x);
                output.Write((byte)(x >> 8));
            }
            output.Write((byte)str.Length);
            output.Write((byte)(str.Length >> 8));
        }

        private static int CalculatePooledStringLength(string str)
        {
            return (str.Length > 0x7FFF ? 4 : 2) + 2 + Encoding.Unicode.GetByteCount(str);
        }
    }
}
