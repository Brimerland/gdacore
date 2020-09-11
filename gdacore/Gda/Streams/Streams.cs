using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using System.Net.Sockets;
using System.Net;

namespace Gda.Streams
{
    class Connector
    {
    }

    class Connection
    {
    }

    interface IConsumer<T>
    {
        ValueTask<int> ConsumeAsync(ReadOnlyMemory<T> toConsume);
    }

    interface IProducer<T>
    {
        void AttachConsumer(IConsumer<T> newConsumer);
    }

    interface IProducerPassive<T>
    {
        Task<T> GetProductAsync();
    }

    interface IConsumer2<T>
    {
        Task ConsumeFromAsync(IProducer2<T> producer);
    }

    interface IProducer2<T>
    {
        Task<(IProducer2<T>,T)> GetAsync();
    }

    class ExtraProducer2<T> : IProducer2<T>
    {
        Task<(IProducer2<T>, T)> getTask;
        
        public ExtraProducer2(IProducer2<T> nextProducer, T data)
        {
            getTask = Task.FromResult<(IProducer2<T>, T)>((nextProducer, data));
        }

        public Task<(IProducer2<T>, T)> GetAsync() 
            => getTask;
    }

    interface IConsumer3<T>
    {
        Task ConsumeFromAsync(IProducer3<T> producer);
    }

    interface IProducer3<T>
    {
        Task<IProducer3<T>> NextAsync();
        Task<T> GetCurrentDataAsync();
    }

    class ExtraProducer3<T> : IProducer3<T>
    {
        Task<T> Data;
        Task<IProducer3<T>> NextProducer;

        public ExtraProducer3(IProducer3<T> nextProducer, T data)
        {
            Data = Task.FromResult(data);
            NextProducer = Task.FromResult(nextProducer);
        }

        public Task<T> GetCurrentDataAsync()
            => Data;

        public Task<IProducer3<T>> NextAsync()
            => NextProducer;
    }


    class BufferBlockConnection<T> : IConsumer<T>, IProducer<T> 
    {
        IConsumer<T> Down;
        BufferBlock<ReadOnlyMemory<T>> OutBufferBlock = new BufferBlock<ReadOnlyMemory<T>>();

        Task SendLoopTask;
        TaskCompletionSource<bool> TCS_Shutdown = new System.Threading.Tasks.TaskCompletionSource<bool>();

        public void AttachConsumer(IConsumer<T> newDown)
        {
            Down = newDown;
        }

        public async ValueTask<int> ConsumeAsync(ReadOnlyMemory<T> toSend)
        {
            if (SendLoopTask == null)
            {
                lock(this)
                {
                    if (SendLoopTask == null)
                        SendLoopTask = Task.Run(() => SendLoopAsync(TCS_Shutdown.Task));
                }
            }

            return await OutBufferBlock.SendAsync(toSend) ? -1 : toSend.Length;
        }

        async Task SendLoopAsync(Task shutdownTask)
        {
            var availableTask = OutBufferBlock.OutputAvailableAsync();
            var buffer = new T[65535];
            var currentItems = new List<ReadOnlyMemory<T>>();
            while (shutdownTask != await Task.WhenAny(shutdownTask, availableTask) && availableTask.Result)
            {
                try 
                {
                    currentItems.Clear();
                    
                    var totalLength = 0; 
                    while (totalLength < buffer.Length && OutBufferBlock.TryReceive(out var item))
                    {
                        totalLength += item.Length;
                        currentItems.Add(item);
                    }

                    var down = Down;
                    if (down != null)
                    {
                        if (totalLength > buffer.Length)
                            buffer = new T[totalLength];

                        ReadOnlyMemory<T> _concat()
                        {
                            var bufferSpan = new Span<T>(buffer);
                            int pos = 0;
                            foreach(var item in currentItems)
                            {
                                item.Span.CopyTo(bufferSpan.Slice(pos));
                                pos += item.Length;
                            }

                            return new ReadOnlyMemory<T>(buffer, 0, pos);
                        }

                        await Down.ConsumeAsync(_concat());
                    }
                }
                catch
                {}

                availableTask = OutBufferBlock.OutputAvailableAsync();
            }
        }
    }

    class SocketConnectionOut : IConsumer<byte>
    {
        public Socket Socket;
        
        public ValueTask<int> ConsumeAsync(ReadOnlyMemory<byte> toSend)
        {
            return Socket.SendAsync(toSend, SocketFlags.None);
        }
    }

    class SocketConnectionIn : IProducer<byte>
    {
        public Socket Socket;

        IConsumer<byte> Down;

        public void AttachConsumer(IConsumer<byte> newDown)
        {
            Down = newDown;
        }

        public Task StartReceiveAsync()
        {
            return Task.Run(async () =>
            {
                var buffer = new byte[65535];
                var bufferMem = new Memory<byte>(buffer);
                var readBytes = 0;
                do 
                {
                    readBytes = await Socket.ReceiveAsync(bufferMem, SocketFlags.None);
                    await Down.ConsumeAsync(bufferMem.Slice(0,readBytes));
                } while (readBytes > 0);
            });
        }
    }

    class SocketSource : IProducer<Socket>
    {
        IConsumer<Socket> Down;
        public void AttachConsumer(IConsumer<Socket> newDown)
        {
            Down = newDown;
        }

        public Task StartReceiveAsync()
        {
            return Task.Run(async () => 
            {
                var lSocket = new Socket(AddressFamily.InterNetwork,
                                     SocketType.Stream,
                                     ProtocolType.Tcp);

                lSocket.Bind(new IPEndPoint(IPAddress.Any, 1704));

                // start listening
                lSocket.Listen(10);
                while (true)
                {
                    var asocket = await lSocket.AcceptAsync();
                    var down = Down;
                    if (down != null)
                        _ = down.ConsumeAsync(new ReadOnlyMemory<Socket>(new Socket[] { asocket }));
                }
            });
        }
    }

    class SocketSourceClient : IProducer<Socket>
    {
        IConsumer<Socket> Down;
        public void AttachConsumer(IConsumer<Socket> newDown)
        {
            Down = newDown;
        }

        public Task StartReceiveAsync()
        {
            return Task.Run(async () => 
            {
                var socket = new Socket(AddressFamily.InterNetwork,
                                     SocketType.Stream,
                                     ProtocolType.Tcp);

                // start listening
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("192.168.178.59"), 1704));
                var down = Down;
                if (down != null)
                    _ = down.ConsumeAsync(new ReadOnlyMemory<Socket>(new Socket[] { socket }));
            });
        }
    }

    class SocketConsumer : IConsumer<Socket>, IProducer<(SocketConnectionIn, SocketConnectionOut)>
    {
        IConsumer<(SocketConnectionIn, SocketConnectionOut)> Down;

        public void AttachConsumer(IConsumer<(SocketConnectionIn, SocketConnectionOut)> newDown)
        {
            Down = newDown;
        }

        public ValueTask<int> ConsumeAsync(ReadOnlyMemory<Socket> toSend)
        {
            foreach(var socket in toSend.ToArray())
            {
                Console.WriteLine($"new Socket from {socket.RemoteEndPoint.ToString()}");
                var down = Down;
                if (down != null)
                    _ = down.ConsumeAsync(new ReadOnlyMemory<(SocketConnectionIn, SocketConnectionOut)>( new (SocketConnectionIn, SocketConnectionOut)[] 
                        { (new SocketConnectionIn() {Socket = socket}, new SocketConnectionOut() {Socket = socket})  }));
                else
                    socket.Close();
            }

            return new ValueTask<int>(-1);
        }
    }
}
