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
            if (args.Any(s => s == "test1"))
                await Test1(args);
            else
                await Main2(args);
        }

        static async Task Main2(string[] args)
        {
            Console.WriteLine("Hello World! " + Gda.Dingens.SomeString);
            //Gda.MidiIn.Create();

            var tcs_neverend = new TaskCompletionSource<bool>();
            var shutdownTask = tcs_neverend.Task;

            try
            {
                if (args.Any(s => s == "client"))
                {
                    Console.WriteLine("Running as client");
                    
                    var echoDelayed = new EchoDelayed();
                    var proxyOut = new ProxyDown() { Message = "Sending" };
                    echoDelayed.AttachConsumer(proxyOut);

                    var midiGen = new MidiGenerator();
                    midiGen.AttachConsumer(proxyOut);
                    _ = midiGen.RunAsync();

                    var proxyIn = new ProxyDown() { Message = "Received" };
                    proxyIn.AttachConsumer(echoDelayed);

                    var ssource = new Gda.Streams.SocketSourceClient();
                    var sconsumer = new Gda.Streams.SocketConsumer();
                    ssource.AttachConsumer(sconsumer);
                    sconsumer.AttachConsumer(new SocketTerminal(){
                        InSink = proxyIn,
                        OutSource = proxyOut,
                    });

                    _ = ssource.StartReceiveAsync();
                }
                else
                {
                    var conn = new Gda.Streams.Connection();
                    var bbCon = new Gda.Streams.BufferBlockConnection<byte>();
                    bbCon.AttachConsumer(new DownTerminal());
                    await bbCon.ConsumeAsync(new ReadOnlyMemory<byte>(new byte[255]));
                    var downSource = new DownSource();
                    downSource.AttachConsumer(bbCon);
                    _= downSource.RunAsync();

                    var ssource = new Gda.Streams.SocketSource();
                    var sconsumer = new Gda.Streams.SocketConsumer();
                    
                    var midiSource = new MidiSource();
                    ssource.AttachConsumer(sconsumer);
                    sconsumer.AttachConsumer(new SocketTerminal(){
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


            await shutdownTask;
        }

        static async Task Test1(string[] args)
        {
            {
                var buf = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(new string("0123456789abcdefghijklmnopqrstuvwxyz".Reverse().ToArray())));
                Gda.Streams.ExtraProducer2<ReadOnlyMemory<byte>> producer = null;

                var step = 7;
                var pos = 0;
                while (pos<buf.Length)
                {
                    var len = Math.Min(step, buf.Length - pos);
                    producer = new Gda.Streams.ExtraProducer2<ReadOnlyMemory<byte>>(producer, buf.Slice(pos,len));
                    pos += len;
                }

                
                var httpConsumer = new HTTPConsumer();
                await httpConsumer.ConsumeFromAsync(producer);
            }

            {
                var buf = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(new string("0123456789abcdefghijklmnopqrstuvwxyz".Reverse().ToArray())));
                Gda.Streams.ExtraProducer3<ReadOnlyMemory<byte>> producer = null;

                var step = 7;
                var pos = 0;
                while (pos < buf.Length)
                {
                    var len = Math.Min(step, buf.Length - pos);
                    producer = new Gda.Streams.ExtraProducer3<ReadOnlyMemory<byte>>(producer, buf.Slice(pos, len));
                    pos += len;
                }


                var httpConsumer = new HTTPConsumer3();
                await httpConsumer.ConsumeFromAsync(producer);
            }

            {
                var start = new FakeProtokolSource();
                var end = start;

                start.AttachConsumer(end);

                await start.RunAsync();
            }
        }
    }

    class MidiSource : Gda.Streams.IProducer<byte>, Gda.Streams.IConsumer<byte>
    {
        Gda.Streams.IConsumer<byte> Down;

        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
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
                        await down.ConsumeAsync(new ReadOnlyMemory<byte>(midiBytes));
                }
            });
        }

        public Task ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            midiIn?.WriteBytes(toSend);
            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class DownSource : Gda.Streams.IProducer<byte>
    {
        Gda.Streams.IConsumer<byte> Down;
        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
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
                        await down.ConsumeAsync(new ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes(line)));
                }
            });
        }

    }

    class DownTerminal : Gda.Streams.IConsumer<byte>
    {
        public Task ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            Console.WriteLine($"Processing {toSend.Length} bytes");
            Console.WriteLine($"'{System.Text.Encoding.UTF8.GetString(toSend.Span)}'");
            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class SocketTerminal : Gda.Streams.IConsumer<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)>
    {
        public Gda.Streams.IProducer<byte> OutSource;
        public Gda.Streams.IConsumer<byte> InSink;


        public async Task ConsumeAsync(ReadOnlyMemory<(Gda.Streams.SocketConnectionIn, Gda.Streams.SocketConnectionOut)> toSend)
        {
            foreach(var socketPair in toSend.ToArray())
            {
                var inCon = socketPair.Item1;
                inCon.AttachConsumer(InSink);
                _ = inCon.StartReceiveAsync();
                
                var outCon = socketPair.Item2;
                OutSource?.AttachConsumer(outCon);
            }
        }

        public Task WhenShutdownAsync() => null;
    }

    class EchoDelayed : Gda.Streams.IConsumer<byte>, Gda.Streams.IProducer<byte>
    {
        Gda.Streams.IConsumer<byte> Down;
        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
        {
            Down = newDown;
        }

        public Task ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            var bufMem = new Memory<byte>(new byte[toSend.Length]);
            toSend.CopyTo(bufMem);

            _ = Task.Run(async () => {
                await Task.Delay(1000);
                var down = Down;
                if (down != null)
                    _ = down.ConsumeAsync(bufMem);
            });

            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class ProxyDown : Gda.Streams.IConsumer<byte>, Gda.Streams.IProducer<byte>
    {
        public String Message = "";

        Gda.Streams.IConsumer<byte> Down;
        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
        {
            Down = newDown;
        }

        public Task ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            var sb = new StringBuilder();
            foreach(var b in toSend.ToArray())
                sb.Append($"{b} ");
            Console.WriteLine($"{Message} '{sb.ToString()}'");
            return Down?.ConsumeAsync(toSend) ?? Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }

    class MidiGenerator : Gda.Streams.IProducer<byte>
    {
        Gda.Streams.IConsumer<byte> Down;
        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
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

                    _ = down.ConsumeAsync(new Memory<byte>(new byte[] {0xb1, 0, 0x7f, // channel 1 bank select 127 (msb)
                                                                    0xc1, 88,      // channel 1 program 88 (power kit on bank 127/0)
                                                                    0x91, oldNote, 0, 0x91, note, 61}));
                }
                
                counter ++;

                await Task.Delay(250);
            }
        }
         
    }



    class FakeProtokolSource : Gda.Streams.IProducer<byte>, Gda.Streams.IConsumer<byte>
    {
        Gda.Streams.IConsumer<byte> Down;

        public void AttachConsumer(Gda.Streams.IConsumer<byte> newDown)
        {
            Down = newDown;
        }

        public async Task RunAsync()
        {
            var down = Down;

            if (down != null)
            {
                await Task.Delay(1000);
                await down.ConsumeAsync(new Memory<byte>(Encoding.UTF8.GetBytes("/TestCommand/")));
            }
        }

        public Task ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            Console.WriteLine($"Received {Encoding.UTF8.GetString(toSend.Span)}");
            return Task.CompletedTask;
        }
    }

    class HTTPConsumer : Gda.Streams.IConsumer2<ReadOnlyMemory<byte>>
    {
        public async Task ConsumeFromAsync(Gda.Streams.IProducer2<ReadOnlyMemory<byte>> producer)
        {
            var parser = new HTTPHeaderParser();
            var consumeTask = parser.ConsumeFromAsync(producer);
            var headerTask =  parser.GetAsync();
            var completedTask = await Task.WhenAny(consumeTask, headerTask);

            await completedTask; // throw exception if any

            if (headerTask.IsCompleted)
            {
                Console.WriteLine("Headertask done.");
                producer = headerTask.Result.Item2.Item2;
                while (producer != null)
                {
                    ReadOnlyMemory<byte> data;
                    (producer, data) = await producer.GetAsync();
                    Console.WriteLine($"Read {Encoding.UTF8.GetString(data.Span)}");
                }

                Console.WriteLine("Reading done.");
            }
        }
    }

    class HTTPHeaderParser : Gda.Streams.IConsumer2<ReadOnlyMemory<byte>>, Gda.Streams.IProducer2<(HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>)>
    {
        TaskCompletionSource<(Gda.Streams.IProducer2<(HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>)>, (HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>))> TCS 
            = new TaskCompletionSource<(Gda.Streams.IProducer2<(HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>)>, (HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>))>();

        public async Task ConsumeFromAsync(Gda.Streams.IProducer2<ReadOnlyMemory<byte>> producer)
        {
            try
            {
                ReadOnlyMemory<byte> data = new ReadOnlyMemory<byte>();
                var count = 0;

                while (count < 10)
                {
                    (producer, data) = await producer.GetAsync();
                    count += data.Length;
                }

                if (count>10)
                {
                    var length = count - 10;
                    var mem = new Memory<byte>(new byte[length]);
                    data.Slice(data.Length - length, length).CopyTo(mem);
                    producer = new Gda.Streams.ExtraProducer2<ReadOnlyMemory<byte>>(producer, mem);
                }

                TCS.SetResult((this, (new HTTPHeader(), producer)));
            }
            catch (Exception e)
            {
                TCS.TrySetException(e);
            }

        }

        public Task<(Gda.Streams.IProducer2<(HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>)>, (HTTPHeader, Gda.Streams.IProducer2<ReadOnlyMemory<byte>>))> GetAsync()
            => TCS.Task;
    }

    class HTTPConsumer3 : Gda.Streams.IConsumer3<ReadOnlyMemory<byte>>
    {
        public async Task ConsumeFromAsync(Gda.Streams.IProducer3<ReadOnlyMemory<byte>> producer)
        {
            var parser = new HTTPHeaderParser3();
            var consumeTask = parser.ConsumeFromAsync(producer);
            var headerTask = parser.GetCurrentDataAsync();
            var completedTask = await Task.WhenAny(consumeTask, headerTask);

            await completedTask; // throw exception if any

            if (headerTask.IsCompleted)
            {
                Console.WriteLine("Headertask done.");
                producer = headerTask.Result.Item2;
                while (producer != null)
                {
                    var data = await producer.GetCurrentDataAsync();
                    producer = await producer.NextAsync();
                    Console.WriteLine($"Read {Encoding.UTF8.GetString(data.Span)}");
                }

                Console.WriteLine("Reading done.");
            }
        }
    }

    class HTTPHeaderParser3 : Gda.Streams.IConsumer3<ReadOnlyMemory<byte>>, Gda.Streams.IProducer3<(HTTPHeader, Gda.Streams.IProducer3<ReadOnlyMemory<byte>>)>
    {
        TaskCompletionSource<(HTTPHeader, Gda.Streams.IProducer3<ReadOnlyMemory<byte>>)> TCS = new TaskCompletionSource<(HTTPHeader, Gda.Streams.IProducer3<ReadOnlyMemory<byte>>)>();

        public async Task ConsumeFromAsync(Gda.Streams.IProducer3<ReadOnlyMemory<byte>> producer)
        {
            try
            {
                ReadOnlyMemory<byte> data = new ReadOnlyMemory<byte>();
                var count = 0;

                while (count < 10)
                {
                    data = await producer.GetCurrentDataAsync();
                    count += data.Length;
                    producer = await producer.NextAsync();                    
                }

                if (count > 10)
                {
                    var length = count - 10;
                    var mem = new Memory<byte>(new byte[length]);
                    data.Slice(data.Length - length, length).CopyTo(mem);
                    producer = new Gda.Streams.ExtraProducer3<ReadOnlyMemory<byte>>(producer, mem);
                }

                TCS.SetResult((new HTTPHeader(), producer));
            }
            catch (Exception e)
            {
                TCS.TrySetException(e);
            }

        }

        public Task<(HTTPHeader, Gda.Streams.IProducer3<ReadOnlyMemory<byte>>)> GetCurrentDataAsync()
        {
            return TCS.Task;
        }

        public async Task<Gda.Streams.IProducer3<(HTTPHeader, Gda.Streams.IProducer3<ReadOnlyMemory<byte>>)>> NextAsync()
        {
            await TCS.Task;
            return null;
        }
    }

    class HTTPHeader
    {}
}
