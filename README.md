# AttributeBasedValidation
Attribute based validation -- reflection


            var masterModel = new Master
            {
                CantBeNull = new List<Child>
                {
                    new Child
                    {
                        NestedChildren = new List<Child>(),
                        Barcode = "12345678900",
                        Name = "Murat Ay"
                    }
                },
                Child = new Child
                {
                    NestedChildren = new List<Child>(),
                    Barcode = "12345678900",
                    Name = "Murat Ay"
                },
                Name = "Murat Ay",
                AppointmentDate = DateTime.Today.AddDays(-1),
                CanbeNull = null,
                EndDate = null,
                GsmNumber = "+90 588 888 88 88",
                IgnoreAbleProperty = null
            };

            var result = ValidationHelper.CheckModel(masterModel, true);
            Console.WriteLine(result.IsValid+" "+result.Error);
            Console.ReadLine();
            
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
