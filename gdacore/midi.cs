using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Gda
{
    public class MidiIn
    {
        //snd_rawmidi_open(&handle_midi_in, &handle_midi_out, "hw:3,0,0", 0 );
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


        static public MidiIn Create()
        {
            MidiIn result = null;

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


            return result;
        } 
    }
}