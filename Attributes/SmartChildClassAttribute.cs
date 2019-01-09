using System;

namespace ValidationHelper.Attributes
{

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SmartChildClassAttribute : Attribute
    {
        public readonly bool NotNull;
        public readonly bool IsCollection;
        public readonly int MinCollectionCount;
        public readonly bool IgnoreWhenPrompted;
        public SmartChildClassAttribute(bool isCollection, int minCollectionCount = 1, bool notNull = true, bool ignoreWhenPrompted = false)
        {
            NotNull = notNull;
            IsCollection = isCollection;
            MinCollectionCount = minCollectionCount;
            IgnoreWhenPrompted = ignoreWhenPrompted;
        }
    }
}