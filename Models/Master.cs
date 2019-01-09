using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValidationHelper.Attributes;

namespace ValidationHelper.Models
{
    public class Master
    {
        [SmartString]
        public string Name { get; set; }

        [SmartString(regexPattern: @"5\d{9}\s*?$", minLength: 10, maxLength: 15, onlyDigit: true, changeProperty: true, subStringIndex: 10)]
        public string GsmNumber { get; set; }

        [SmartDateTime(notNullOrDefault: false, greaterOrEqualThanTomorrow: true)]
        public DateTime AppointmentDate { get; set; }

        [SmartDateTime(false, false, changeIfDefault: true, year: 2020, day: 31, month: 12)]
        public DateTime? EndDate { get; set; }

        public IList<Child> CanbeNull { get; set; }

        [SmartChildClass(true, minCollectionCount: 1)]
        public IList<Child> CantBeNull { get; set; }

        [SmartChildClass(isCollection: false)]
        public Child Child { get; set; }

        [SmartString(ignoreWhenPrompted: true)]
        public string IgnoreAbleProperty { get; set; }

    }
}
