
namespace LaunchDarkly.Sdk
{
    internal static class Errors
    {
        internal const string AttrEmpty = "attribute reference cannot be empty";
        internal const string AttrExtraSlash = "attribute reference contained a double slash or a trailing slash";
        internal const string AttrInvalidEscape =
            "attribute reference contained an escape character (~) that was not followed by 0 or 1";

        internal const string ContextUninitialized = "tried to use uninitialized Context";
        internal const string ContextFromNullUser = "tried to use a null User reference";
        internal const string ContextNoKey = "context key must not be null or empty";
        internal const string ContextKindCannotBeKind = "\"kind\" is not a valid context kind";
        internal const string ContextKindInvalidChars = "context kind contains disallowed characters";
        internal const string ContextKindMultiForSingle = "context of kind \"multi\" must be created with NewMulti or NewMultiBuilder";
        internal const string ContextKindMultiWithNoKinds = "multi-kind context must contain at least one kind";
        internal const string ContextKindMultiDuplicates = "multi-kind context cannot have same kind more than once";

        internal const string JsonContextEmptyKind = "context kind cannot be empty";
        internal static string JsonMissingProperty(string name) =>
             string.Format(@"missing required property ""{0}""", name);
        internal static string JsonWrongType(string name, LdValueType badType) =>
             string.Format(@"unsupported type ""{0}"" for property ""{1}""", badType, name);
        internal static string JsonSerializeInvalidContext(string error) =>
            "cannot serialize invalid Context: " + error;
    }
}
