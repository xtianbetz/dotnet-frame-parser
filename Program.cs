using System;
using System.Collections.Generic;

#nullable enable
namespace dotnet_frame_parser
{
    interface IStreamProtocolParser<TM>
    {
        public FrameParseResult<TM> ReadFramesFromBuffer(byte[] bufSlice);
    }
    
    class FrameParseResult<TF>
    {
        public List<TF> Frames = new List<TF>();
    }

    class SimpleTwoByteMessage
    {
        public byte Value = 0;
    }

    enum SimpleTwoByteProtocolScannerState
    {
        ExpectingHeader,
        ExpectingValue,
    }

    class SimpleTwoByteProtocolParserState
    {
        public SimpleTwoByteProtocolScannerState ScannerState = SimpleTwoByteProtocolScannerState.ExpectingHeader;
    }

    class TwoByteProtocolParser : IStreamProtocolParser<SimpleTwoByteMessage>
    {
        public SimpleTwoByteProtocolParserState State = new SimpleTwoByteProtocolParserState();
        public FrameParseResult<SimpleTwoByteMessage> ReadFramesFromBuffer(byte[] buf)
        {
           var result =  new FrameParseResult<SimpleTwoByteMessage>();
           result.Frames = new List<SimpleTwoByteMessage>();

           var position = 0;
           var bufLen = buf.Length;
           while (position < bufLen)
           {
               switch (State.ScannerState)
               {
                   case SimpleTwoByteProtocolScannerState.ExpectingHeader:
                       if (buf[position] == 0x99)
                       {
                           State.ScannerState = SimpleTwoByteProtocolScannerState.ExpectingValue;
                       }
                       else
                       {
                           Console.WriteLine("Warning: Invalid/unexpected byte in stream: {0}", buf[position]);
                       }
                       break;
                   case SimpleTwoByteProtocolScannerState.ExpectingValue:
                       State.ScannerState = SimpleTwoByteProtocolScannerState.ExpectingHeader;
                       result.Frames.Add(new SimpleTwoByteMessage() {Value = buf[position]});
                       break;
               }
               position++;
           }

           return result;
        }
    }


    class Program
    {
        public static void ReadAndProcessSerialFrames()
        {
            var bufferSlice1 = new byte[]
            {
                0x99, 0x04,
                0x22, 0x39,
                0x99, 0x03,
                0x99, 0x02,
                0x99,
            };
            var bufferSlice2 = new byte[]
            {
                0x01,
                0x99, 0x07,
                0x99, 0x08,
            };

            var parser = new TwoByteProtocolParser();
            var scanResult = parser.ReadFramesFromBuffer(bufferSlice1);

            foreach (var frame in scanResult.Frames)
            {
                Console.WriteLine($"Got Frame {frame.Value}");
            }

            var scanResult2 = parser.ReadFramesFromBuffer(bufferSlice2);
            foreach (var frame in scanResult2.Frames)
            {
                Console.WriteLine($"Got Frame {frame.Value}");
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            ReadAndProcessSerialFrames();
        }
    }
}
#nullable disable
