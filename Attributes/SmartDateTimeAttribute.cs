using System;

namespace ValidationHelper.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class SmartDateTimeAttribute : Attribute
    {
        public readonly bool NotNullOrDefault;
        public readonly int Year;
        public readonly int Month;
        public readonly int Day;
        public readonly bool ChangeIfDefault;
        public readonly bool GreaterOrEqualThanTomorrow;

        public SmartDateTimeAttribute(bool notNullOrDefault, bool greaterOrEqualThanTomorrow, int year = 1900, int month = 1, int day = 1, bool changeIfDefault = false)
        {
            NotNullOrDefault = notNullOrDefault;
            GreaterOrEqualThanTomorrow = greaterOrEqualThanTomorrow;
            Year = year;
            Month = month;
            Day = day;
            ChangeIfDefault = changeIfDefault;
        }
    }
}