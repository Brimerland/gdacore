using System;
using System.Threading.Tasks;
using System.Linq;
using System.Text;

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
                if (args.Any(s => s == "client"))
                {
                    Console.WriteLine("Running as client");
                    
                    var echoDelayed = new EchoDelayed();
                    var proxyOut = new ProxyDown() { Message = "Sending" };
                    echoDelayed.ConnectDown(proxyOut);

                    var midiGen = new MidiGenerator();
                    midiGen.ConnectDown(proxyOut);
                    _ = midiGen.RunAsync();

                    var proxyIn = new ProxyDown() { Message = "Received" };
                    proxyIn.ConnectDown(echoDelayed);

                    var ssource = new Gda.Streams.SocketSourceClient();
                    var sconsumer = new Gda.Streams.SocketConsumer();
                    ssource.ConnectDown(sconsumer);
                    sconsumer.ConnectDown(new SocketTerminal(){
                        InSink = proxyIn,
                        OutSource = proxyOut,
                    });

                    _ = ssource.StartReceiveAsync();

                    await Task.Delay(10000000);

                }
                else
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
                    
                    var midiSource = new MidiSource();
                    ssource.ConnectDown(sconsumer);
                    sconsumer.ConnectDown(new SocketTerminal(){
                        InSink = midiSource,
                        OutSource = midiSource
                    });
                    _ = midiSource.RunAsync();
                    
                    _ = ssource.StartReceiveAsync();
                }
            }
            catch (Exception e)
            {}
            finally
            {}


            await (shutdownTask??Task.CompletedTask);
        }
    }

    class MidiSource : Gda.Streams.IConnectable<byte>, Gda.Streams.IConnection<byte>
    {
        Gda.Streams.IConnection<byte> Down;

        public void ConnectDown(Gda.Streams.IConnection<byte> newDown)
        {
            Down = newDown;
        }

        Gda.MidiIn midiIn;

        public Task RunAsync()
        {
            return Task.Run(async () =>
            {
                midiIn = Gda.MidiIn.Create2();
                while (true)
                {
                    var midiBytes = await midiIn.GetBytesAsync();
                    var down = Down;
                    if (down != null)
                        await down.SendAsync(new ReadOnlyMemory<byte>(midiBytes));
                }
            });
        }

        public Task SendAsync(ReadOnlyMemory<byte> toSend)
        {
            midiIn?.WriteBytes(toSend);
            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
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
            Console.WriteLine($"Processing {toSend.Length} bytes");
            Console.WriteLine($"'{System.Text.Encoding.UTF8.GetString(toSend.Span)}'");
            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class SocketTerminal : Gda.Streams.IConnection<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)>
    {
        public Gda.Streams.IConnectable<byte> OutSource;
        public Gda.Streams.IConnection<byte> InSink;


        public async Task SendAsync(ReadOnlyMemory<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)> toSend)
        {
            foreach(var socketPair in toSend.ToArray())
            {
                var inCon = socketPair.Item1;
                inCon.ConnectDown(InSink);
                _ = inCon.StartReceiveAsync();
                
                var outCon = socketPair.Item2;
                OutSource?.ConnectDown(outCon);
            }
        }

        public Task WhenShutdownAsync() => null;
    }

    class EchoDelayed : Gda.Streams.IConnection<byte>, Gda.Streams.IConnectable<byte>
    {
        Gda.Streams.IConnection<byte> Down;
        public void ConnectDown(Gda.Streams.IConnection<byte> newDown)
        {
            Down = newDown;
        }

        public Task SendAsync(ReadOnlyMemory<byte> toSend)
        {
            var bufMem = new Memory<byte>(new byte[toSend.Length]);
            toSend.CopyTo(bufMem);

            _ = Task.Run(async () => {
                await Task.Delay(1000);
                var down = Down;
                if (down != null)
                    _ = down.SendAsync(bufMem);
            });

            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class ProxyDown : Gda.Streams.IConnection<byte>, Gda.Streams.IConnectable<byte>
    {
        public String Message = "";

        Gda.Streams.IConnection<byte> Down;
        public void ConnectDown(Gda.Streams.IConnection<byte> newDown)
        {
            Down = newDown;
        }

        public Task SendAsync(ReadOnlyMemory<byte> toSend)
        {
            var sb = new StringBuilder();
            foreach(var b in toSend.ToArray())
                sb.Append($"{b} ");
            Console.WriteLine($"{Message} '{sb.ToString()}'");
            return Down?.SendAsync(toSend) ?? Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class MidiGenerator : Gda.Streams.IConnectable<byte>
    {
        Gda.Streams.IConnection<byte> Down;
        public void ConnectDown(Gda.Streams.IConnection<byte> newDown)
        {
            Down = newDown;
        }

        public async Task RunAsync()
        {
            var seq = new byte[] { 35, 42, 38, 42 };
            byte note = 0;
            int counter = 0;
            while (true)
            {
                var down = Down;
                if (down != null)
                {
                    var oldNote = note;
                    note = seq[counter % seq.Length]; // base / snare

                    _ = down.SendAsync(new Memory<byte>(new byte[] {0xb1, 0, 0x7f, // channel 1 bank select 127 (msb)
                                                                    0xc1, 88,      // channel 1 program 88 (power kit on bank 127/0)
                                                                    0x91, oldNote, 0, 0x91, note, 61}));
                }
                
                counter ++;

                await Task.Delay(250);
            }
        }
         
    }
}
