using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValidationHelper.Models;

namespace ValidationHelper
{
    class Program
    {
        static void Main(string[] args)
        {
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
        }
    }
}
