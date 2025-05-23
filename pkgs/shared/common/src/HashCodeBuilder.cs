
namespace LaunchDarkly.Sdk
{
    internal struct HashCodeBuilder
    {
        private readonly int _value;
        public int Value => _value;
        
        internal HashCodeBuilder(int value)
        {
            _value = value;
        }

        public HashCodeBuilder With(object o)
        {
            return new HashCodeBuilder(_value * 17 + (o == null ? 0 : o.GetHashCode()));
        }
    }
}
