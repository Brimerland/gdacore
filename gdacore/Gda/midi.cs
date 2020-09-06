#define Linux

using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Gda
{
    public class MidiIn
    {
#if Linux
        [DllImport("libasound.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_rawmidi_open(ref IntPtr handle_midi_in, ref IntPtr handle_midi_out, string deviceId, int mode);
        [DllImport("libasound.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_rawmidi_open(IntPtr handle_midi_in, ref IntPtr handle_midi_out, string deviceId, int mode);
        [DllImport("libasound.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_rawmidi_open(ref IntPtr handle_midi_in, IntPtr handle_midi_out, string deviceId, int mode);
        [DllImport("libasound.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_rawmidi_read([In] IntPtr handle, [Out] byte[] buffer, int count);
        [DllImport("libasound.so", CallingConvention = CallingConvention.Cdecl)]
        public static extern int snd_rawmidi_write([In] IntPtr handle, [In] byte[] buffer, int count);
#endif
        static public MidiIn Create2()
        {
            var result = new MidiIn();
            result.StartThread();
            return result;
        }
        BufferBlock<byte[]> BufferBlock = new BufferBlock<byte[]>();
        System.Threading.Thread Thread;

        IntPtr handle_out = IntPtr.Zero;
        void StartThread()
        {

            Thread = new System.Threading.Thread(() => {
#if Linux
            var handle_in = IntPtr.Zero;
            
            var err = MidiIn.snd_rawmidi_open(ref handle_in, ref handle_out, 
		"hw:1,0,0", // "hw:3,0,0"
		0);

            if (err == 0)
            {
                var buf = new byte[1024];
                var bufMem = new Memory<byte>(buf);
                while (true)
                {
                    var readBytes = snd_rawmidi_read(handle_in, buf, buf.Length);
                    var newBuf = new byte[readBytes];
                    bufMem.Slice(0,readBytes).CopyTo(new Memory<byte>(newBuf));
                    BufferBlock.Post(newBuf);
                }
            }
#endif
            });

            Thread.Start();
        }

        public async Task<byte[]> GetBytesAsync()
        {
            byte[] result = null;
            
            if (await BufferBlock.OutputAvailableAsync())
                BufferBlock.TryReceive(out result);

            return result;
        }

        public void WriteBytes(ReadOnlyMemory<byte> toWrite)
        {
            if (handle_out != IntPtr.Zero)
            {
                var buf = toWrite.ToArray();
                snd_rawmidi_write(handle_out, buf, buf.Length);
            }
        }

        static public MidiIn Create()
        {
            MidiIn result = null;
#if Linux
            var handle_in = IntPtr.Zero;
            
            var err = snd_rawmidi_open(ref handle_in, IntPtr.Zero, "hw:3,0,0", 0);

            if (err == 0)
            {
                var midBuf = new System.Collections.Generic.List<byte>();
                
                void DumpBuf()
                {
                    if (midBuf.Count>0)
                    {
                        foreach(var b in midBuf)
                            Console.Write($"{b} ");
                        Console.WriteLine();
                        midBuf.Clear();
                    }
                }

                var buf = new byte[1];
                while (true)
                {
                    snd_rawmidi_read(handle_in, buf, 1);
                    var val = buf[0];
                    if (val == 0xfe)
                        continue;
                    var cmd = val > 0x7f;

                    if (cmd)
                        DumpBuf();

                    midBuf.Add(val);

                    if (midBuf.Count>=3)
                        DumpBuf();
                }
            }

#endif
            return result;
        } 
    }
}
