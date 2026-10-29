using System;
using System.Diagnostics;

namespace QuestPatcher.Axml
{
    public class AxmlAttribute
    {
        public string Name
        {
            get; set;
        }

        public Uri? Namespace
        {
            get; set;
        }

        public int? ResourceId
        {
            get; set;
        }

        public object Value
        {
            get => _value;
            set
            {
                if (value is string)
                {
                    _valueType = AttributeType.String;
                }
                else if (value is WrappedValue)
                {
                    _valueType = null;
                }
                else if (value is bool)
                {
                    _valueType = AttributeType.Boolean;
                }
                else if (value is int)
                {
                    _valueType = AttributeType.FirstInt;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Cannot set value of axml attribute to type of {value.GetType().Name}: must be {nameof(WrappedValue)} or string");
                }

                _value = value;
            }
        }

        private object _value;
        private AttributeType? _valueType;

        internal AxmlAttribute(string name, Uri? ns, int? resourceId, object value, AttributeType valueType)
        {
            Name = name;
            Namespace = ns;
            ResourceId = resourceId;
            Value = value;
            _valueType = valueType;
            Debug.Assert(_value != null);
        }

        public AxmlAttribute(string name, Uri? ns, int? resourceId, object value)
        {
            Name = name;
            Namespace = ns;
            ResourceId = resourceId;
            Value = value;
            Debug.Assert(_value != null);
        }

        internal void PreparePooling(SavingContext ctx)
        {
            if (ResourceId != null)
            {
                ctx.ResourceMap.Add(Name, (int)ResourceId);
            }
            else
            {
                ctx.StringPool.Add(Name);
            }

            if (Namespace != null)
            {
                ctx.StringPool.Add(Namespace.ToString());
            }

            if (Value is WrappedValue wrappedValue)
            {
                if (wrappedValue.RawValue != null)
                {
                    ctx.StringPool.Add(wrappedValue.RawValue);
                }
            }
            else if (Value is string asString)
            {
                ctx.StringPool.Add(asString);
            }
        }

        internal void Save(SavingContext ctx)
        {
            ctx.Writer.Write(Namespace == null ? -1 : ctx.StringPool.GetIndex(Namespace.ToString()));

            if (ResourceId != null)
            {
                int resourceIdIdx = ctx.ResourceMap.GetIndex(Name, (int)ResourceId);
                ctx.Writer.Write(resourceIdIdx);
            }
            else
            {
                ctx.Writer.Write(ctx.StringPool.GetIndex(Name));
            }

            int rawStringIndex = -1;
            int type = _valueType == null ? -1 : ((int)_valueType << 24) | 0x000008;
            int rawValue;
            if (Value is WrappedValue wrappedValue)
            {
                if (wrappedValue.RawValue != null)
                {
                    rawStringIndex = ctx.StringPool.GetIndex(wrappedValue.RawValue);
                }
                rawValue = wrappedValue.ReferenceId;
            }
            else if (Value is bool asBool)
            {
                rawValue = asBool ? -1 : 0;
            }
            else if (Value is string asString)
            {
                rawValue = ctx.StringPool.GetIndex(asString);
                rawStringIndex = rawValue;
            }
            else
            {
                rawValue = (int)Value;
            }

            ctx.Writer.Write(rawStringIndex);
            ctx.Writer.Write(type);
            ctx.Writer.Write(rawValue);
        }
    }
}
