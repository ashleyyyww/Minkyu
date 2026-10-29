using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuestPatcher.Axml
{
    public class AxmlElement
    {
        public int OpeningTextLineNumber
        {
            get; set;
        }

        public int ClosingTextLineNumber
        {
            get; set;
        }

        public List<AxmlAttribute> Attributes { get; } = new List<AxmlAttribute>();

        public List<AxmlElement> Children { get; } = new List<AxmlElement>();

        public Dictionary<string, Uri> DeclaredNamespaces { get; } = new Dictionary<string, Uri>();

        public Uri? DeclaredDefaultNamespace
        {
            get; set;
        }

        public string Name
        {
            get; set;
        }

        public Uri? NamespaceUri
        {
            get; set;
        }

        public AxmlElement(string name, Uri? namespaceUri = null, int openingTextLineNumber = 0, int closingTextLineNumber = 0)
        {
            OpeningTextLineNumber = openingTextLineNumber;
            ClosingTextLineNumber = closingTextLineNumber;
            Name = name;
            NamespaceUri = namespaceUri;
        }

        internal void PreparePooling(SavingContext ctx)
        {

            foreach (KeyValuePair<string, Uri> pair in DeclaredNamespaces)
            {
                ctx.StringPool.Add(pair.Key);
                ctx.StringPool.Add(pair.Value.ToString());
            }

            if (NamespaceUri != null)
            {
                ctx.StringPool.Add(NamespaceUri.ToString());
            }

            ctx.StringPool.Add(Name);



            Attributes.Sort((a, b) =>
            {
                int resourceIdDiff = (a.ResourceId ?? -1) - (b.ResourceId ?? -1);
                if (resourceIdDiff != 0)
                {
                    return resourceIdDiff;
                }

                if (a.Namespace == null)
                {
                    return b.Namespace == null ? 0 : -1;
                }
                else
                {
                    return b.Namespace == null ? 1 : String.CompareOrdinal(a.Namespace.ToString(), b.Namespace.ToString());
                }
            });

            foreach (AxmlAttribute attribute in Attributes)
            {
                attribute.PreparePooling(ctx);
            }

            foreach (AxmlElement element in Children)
            {
                element.PreparePooling(ctx);
            }
        }

        internal void Save(SavingContext ctx)
        {

            foreach (KeyValuePair<string, Uri> pair in DeclaredNamespaces)
            {
                ctx.Writer.WriteChunkHeader(ResourceType.XmlStartNamespace, 16);
                ctx.Writer.Write(OpeningTextLineNumber);
                ctx.Writer.Write(0xFFFFFFFF);
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Key));
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Value.ToString()));
            }

            ctx.Writer.WriteChunkHeader(ResourceType.XmlStartElement, 28 + 20 * Attributes.Count);
            ctx.Writer.Write(OpeningTextLineNumber);
            ctx.Writer.Write(0xFFFFFFFF);
            ctx.Writer.Write(NamespaceUri == null ? -1 : ctx.StringPool.GetIndex(NamespaceUri.ToString()));
            ctx.Writer.Write(ctx.StringPool.GetIndex(Name));
            ctx.Writer.Write(0x00140014);


            short idAttributeIndex = -1;
            short classAttributeIndex = -1;
            short styleAttributeIndex = -1;
            for (short i = 0; i < Attributes.Count; i++)
            {
                WrappedValue? wrappedValue = Attributes[i].Value as WrappedValue;
                if (wrappedValue == null)
                {
                    continue;
                }


                switch (wrappedValue.Type)
                {
                    case WrappedValueType.Id:
                        if (idAttributeIndex != -1)
                        {
                            throw new InvalidDataException("Cannot have multiple ID attributes on one element");
                        }
                        idAttributeIndex = i;
                        break;
                    case WrappedValueType.Class:
                        if (classAttributeIndex != -1)
                        {
                            throw new InvalidDataException("Cannot have multiple class attributes on one element");
                        }
                        classAttributeIndex = i;
                        break;
                    case WrappedValueType.Style:
                        if (styleAttributeIndex != -1)
                        {
                            throw new InvalidDataException("Cannot have multiple style attributes on one element");
                        }
                        styleAttributeIndex = i;
                        break;
                }
            }

            ctx.Writer.Write((short)Attributes.Count);

            ctx.Writer.Write((short)(idAttributeIndex + 1));
            ctx.Writer.Write((short)(classAttributeIndex + 1));
            ctx.Writer.Write((short)(styleAttributeIndex + 1));
            foreach (AxmlAttribute attribute in Attributes)
            {
                attribute.Save(ctx);
            }

            foreach (AxmlElement child in Children)
            {
                child.Save(ctx);
            }
            ctx.Writer.WriteChunkHeader(ResourceType.XmlEndElement, 16);
            ctx.Writer.Write(ClosingTextLineNumber);
            ctx.Writer.Write(0xFFFFFFFF);
            ctx.Writer.Write(NamespaceUri == null ? -1 : ctx.StringPool.GetIndex(NamespaceUri.ToString()));
            ctx.Writer.Write(ctx.StringPool.GetIndex(Name));


            foreach (KeyValuePair<string, Uri> pair in DeclaredNamespaces.Reverse())
            {
                ctx.Writer.WriteChunkHeader(ResourceType.XmlEndNamespace, 16);
                ctx.Writer.Write(ClosingTextLineNumber);
                ctx.Writer.Write(0xFFFFFFFF);
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Key));
                ctx.Writer.Write(ctx.StringPool.GetIndex(pair.Value.ToString()));
            }
        }
    }
}
