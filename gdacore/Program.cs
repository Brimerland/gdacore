using System;
using System.Threading.Tasks;

namespace gdacore
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Hello World! " + Gda.Dingens.SomeString);
            //Gda.MidiIn.Create();

            Task shutdownTask = null;

            try
            {
                var conn = new Gda.Streams.Connection();
                var bbCon = new Gda.Streams.BufferBlockConnection<byte>();
                bbCon.ConnectDown(new DownTerminal());
                await bbCon.SendAsync(new ReadOnlyMemory<byte>(new byte[255]));
                shutdownTask = bbCon.WhenShutdownAsync();
                var downSource = new DownSource();
                downSource.ConnectDown(bbCon);
                _= downSource.RunAsync();

                var ssource = new Gda.Streams.SocketSource();
                var sconsumer = new Gda.Streams.SocketConsumer();
                ssource.ConnectDown(sconsumer);
                sconsumer.ConnectDown(new SocketTerminal());
                _ = ssource.StartReceiveAsync();
            }
            catch (Exception e)
            {}
            finally
            {}


            await (shutdownTask??Task.CompletedTask);
        }
    }

    class DownSource : Gda.Streams.IConnectable<byte>
    {
        Gda.Streams.IConnection<byte> Down;
        public void ConnectDown(Gda.Streams.IConnection<byte> newDown)
        {
            Down = newDown;
        }

        public Task RunAsync()
        {
            return Task.Run(async () =>
            {
                while (true)
                {
                    var line = await Console.In.ReadLineAsync();
                    var down = Down;
                    if (down != null)
                        await down.SendAsync(new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(line)));
                }
            });
        }

    }

    class DownTerminal : Gda.Streams.IConnection<byte>
    {
        public Task SendAsync(ReadOnlyMemory<byte> toSend)
        {
            Console.WriteLine($"Sending {toSend.Length} bytes");
            Console.WriteLine($"'{System.Text.Encoding.UTF8.GetString(toSend.Span)}'");
            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class SocketTerminal : Gda.Streams.IConnection<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)>
    {

        public async Task SendAsync(ReadOnlyMemory<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)> toSend)
        {
            foreach(var socketPair in toSend.ToArray())
            {
                var inCon = socketPair.Item1;
                inCon.ConnectDown(new DownTerminal());
                _ = inCon.StartReceiveAsync();
            }
        }

        public Task WhenShutdownAsync() => null;
    }
}
