using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WpfApp1
{
    public class Album
    {

        public string Name { get; set; }
        public int YearOfRelease { get; set; }
        public int Sales { get; set; }

        public Album(string name, int yearOfRelease, int sales)
        {
            Name = name;
            YearOfRelease = yearOfRelease;
            Sales = sales;
        }

        public Album()
        {

        }

        public override string ToString()
        {
            return string.Format($"{Name} - released for { DateTime.Now.Year - YearOfRelease} years. Sales of  {Sales}");
        }
    }
}
