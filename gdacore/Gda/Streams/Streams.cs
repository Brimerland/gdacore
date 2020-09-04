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

    interface IConnection<T>
    {
        Task SendAsync(ReadOnlyMemory<T> toSend);

        Task WhenShutdownAsync();
    }

    interface IConnectable<T>
    {
        void ConnectDown(IConnection<T> newDown);
    }

    class BufferBlockConnection<T> : IConnection<T>, IConnectable<T> 
    {
        IConnection<T> Down;
        BufferBlock<ReadOnlyMemory<T>> OutBufferBlock = new BufferBlock<ReadOnlyMemory<T>>();

        Task SendLoopTask;
        TaskCompletionSource<bool> TCS_Shutdown = new System.Threading.Tasks.TaskCompletionSource<bool>();

        public Task WhenShutdownAsync() => TCS_Shutdown.Task;

        public void ConnectDown(IConnection<T> newDown)
        {
            Down = newDown;
        }

        public Task SendAsync(ReadOnlyMemory<T> toSend)
        {
            if (SendLoopTask == null)
            {
                lock(this)
                {
                    if (SendLoopTask == null)
                        SendLoopTask = Task.Run(() => SendLoopAsync(TCS_Shutdown.Task));
                }
            }

            return OutBufferBlock.SendAsync(toSend);
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

                        await Down.SendAsync(_concat());
                    }
                }
                catch
                {}

                availableTask = OutBufferBlock.OutputAvailableAsync();
            }
        }
    }

    class SocketConnectionOut : IConnection<byte>
    {
        public Socket Socket;
        
        public async Task SendAsync(ReadOnlyMemory<byte> toSend)
        {
            await Socket.SendAsync(toSend, SocketFlags.None);
        }

        public Task WhenShutdownAsync() => null;
    }

    class SocketConnectionIn : IConnectable<byte>
    {
        public Socket Socket;

        IConnection<byte> Down;

        public void ConnectDown(IConnection<byte> newDown)
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
                    await Down.SendAsync(bufferMem.Slice(0,readBytes));
                } while (readBytes > 0);
            });
        }
    }

    class SocketSource : IConnectable<Socket>
    {
        IConnection<Socket> Down;
        public void ConnectDown(IConnection<Socket> newDown)
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
                        _ = down.SendAsync(new ReadOnlyMemory<Socket>(new Socket[] { asocket }));
                }
            });
        }
    }

    class SocketSourceClient : IConnectable<Socket>
    {
        IConnection<Socket> Down;
        public void ConnectDown(IConnection<Socket> newDown)
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
                    _ = down.SendAsync(new ReadOnlyMemory<Socket>(new Socket[] { socket }));
            });
        }
    }

    class SocketConsumer : IConnection<Socket>, IConnectable<(SocketConnectionIn, SocketConnectionOut)>
    {
        IConnection<(SocketConnectionIn, SocketConnectionOut)> Down;

        public void ConnectDown(IConnection<(SocketConnectionIn, SocketConnectionOut)> newDown)
        {
            Down = newDown;
        }

        public Task SendAsync(ReadOnlyMemory<Socket> toSend)
        {
            foreach(var socket in toSend.ToArray())
            {
                Console.WriteLine($"new Socket from {socket.RemoteEndPoint.ToString()}");
                var down = Down;
                if (down != null)
                    _ = down.SendAsync(new ReadOnlyMemory<(SocketConnectionIn, SocketConnectionOut)>( new (SocketConnectionIn, SocketConnectionOut)[] 
                        { (new SocketConnectionIn() {Socket = socket}, new SocketConnectionOut() {Socket = socket})  }));
                else
                    socket.Close();
            }

            return Task.CompletedTask;
        }

        public Task WhenShutdownAsync() => null;
    }
}
