using System;

namespace ValidationHelper.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SmartStringAttribute : Attribute
    {
        public readonly string RegexPattern;
        public readonly int MaxLength;
        public readonly int MinLength;
        public readonly bool NotEmptyOrNullOrWhiteSpace;
        public readonly bool OnlyDigit;
        public readonly bool ChangeProperty;
        public readonly int SubStringIndex;
        public readonly bool IgnoreWhenPrompted;
        public SmartStringAttribute(string regexPattern = null, int minLength = -1, int maxLength = -1, bool notEmptyOrNullOrWhiteSpace = true, bool onlyDigit = false, bool changeProperty = false, int subStringIndex = -1, bool ignoreWhenPrompted = false)
        {
            RegexPattern = regexPattern;
            MaxLength = maxLength;
            MinLength = minLength;
            NotEmptyOrNullOrWhiteSpace = notEmptyOrNullOrWhiteSpace;
            OnlyDigit = onlyDigit;
            ChangeProperty = changeProperty;
            SubStringIndex = subStringIndex;
            IgnoreWhenPrompted = ignoreWhenPrompted;
        }
    }
}