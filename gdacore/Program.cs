using System;

namespace gdacore
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World! " + Gda.Dingens.SomeString);
            Gda.MidiIn.Create();

            var conn = new Gda.Streams.Connection();

        }
    }
}
