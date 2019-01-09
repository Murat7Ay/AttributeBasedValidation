using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValidationHelper.Attributes;

namespace ValidationHelper.Models
{
    public class Child
    {
        [SmartString]
        public string Name { get; set; }

        [SmartString(minLength:10,maxLength:15)]
        public string Barcode { get; set; }

        [SmartChildClass(isCollection:true,minCollectionCount:0)]
        public IList<Child> NestedChildren { get; set; }

        [SmartChildClass(false)]
        public Child NestedChild { get; set; }
    }
}
