using System;
using System.Collections.Generic;
using System.IO.Ports;

#nullable enable
namespace dotnet_frame_parser
{
    class ParseResult<TF,TP>
    {
        public List<TF> Frames = new List<TF>();
        public TP ParserState;

        public ParseResult(TP parserState)
        {
            ParserState = parserState;
        }
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
        public byte[] PartialFrame = new byte[]{};
    }

    class TwoByteProtocolParser : IStreamProtocolParser<SimpleTwoByteMessage, SimpleTwoByteProtocolParserState>
    {
        public ParseResult<SimpleTwoByteMessage, SimpleTwoByteProtocolParserState> DecodeBytes(byte[] buf, SimpleTwoByteProtocolParserState parserState)
        {
           var result =  new ParseResult<SimpleTwoByteMessage,SimpleTwoByteProtocolParserState>(parserState);
           result.Frames = new List<SimpleTwoByteMessage>();

           var position = 0;
           while (position < buf.Length)
           {
               switch (parserState.ScannerState)
               {
                   case SimpleTwoByteProtocolScannerState.ExpectingHeader:
                       if (buf[position] == 0x99)
                       {
                           parserState.ScannerState = SimpleTwoByteProtocolScannerState.ExpectingValue;
                       }
                       else
                       {
                           Console.WriteLine("Warning: Invalid/unexpected byte in stream: {0}", buf[position]);
                       }
                       break;
                   case SimpleTwoByteProtocolScannerState.ExpectingValue:
                       parserState.ScannerState = SimpleTwoByteProtocolScannerState.ExpectingHeader;
                       result.Frames.Add(new SimpleTwoByteMessage() {Value = buf[position]});
                       break;
               }
               position++;
           }

           result.ParserState = parserState;

           return result;
        }
    }

    interface IStreamProtocolParser<TM, TP>
    {
        public ParseResult<TM,TP> DecodeBytes(byte[] bufSlice, TP parserState);
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
            var initialParserState = new SimpleTwoByteProtocolParserState();

            var scanResult = parser.DecodeBytes(bufferSlice1, initialParserState);

            foreach (var frame in scanResult.Frames)
            {
                Console.WriteLine($"Got Frame {frame.Value}");
            }

            var scanResult2 = parser.DecodeBytes(bufferSlice2, scanResult.ParserState);
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
