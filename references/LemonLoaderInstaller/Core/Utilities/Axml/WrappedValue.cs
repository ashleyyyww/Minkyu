namespace QuestPatcher.Axml
{
    public enum WrappedValueType
    {
        Id,
        Style,
        Class,
        Reference
    }

    public class WrappedValue
    {
        public WrappedValueType Type
        {
            get;
        }

        public string? RawValue
        {
            get;
        }

        public int ReferenceId
        {
            get;
        }

        public WrappedValue(WrappedValueType type, string? rawValue, int referenceId)
        {
            Type = type;
            RawValue = rawValue;
            ReferenceId = referenceId;
        }
    }
}
