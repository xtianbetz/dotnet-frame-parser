using System;
using System.Collections.Generic;
using System.Reflection.Metadata;

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
    class TagLengthValueMessage
    {
        public byte Tag = 0;
        public byte Length = 0;
        public byte[] Value = new byte[1024];
    }
    enum TagLengthValueProtocolScannerState
    {
        ExpectingTag,
        ExpectingLength,
        ExpectingMoreValueBytes,
    }

    class TagLengthValueProtocolParserState
    {
        public TagLengthValueProtocolScannerState ScannerState = TagLengthValueProtocolScannerState.ExpectingTag;
        public TagLengthValueMessage CurrentFrame = new TagLengthValueMessage();
        public int ValueBytesRead = 0;
    }
    class TagLengthValueProtocolParser : IStreamProtocolParser<TagLengthValueMessage>
    {
        public TagLengthValueProtocolParserState State = new TagLengthValueProtocolParserState();
        public FrameParseResult<TagLengthValueMessage> ReadFramesFromBuffer(byte[] buf)
        {
           var result =  new FrameParseResult<TagLengthValueMessage>();
           result.Frames = new List<TagLengthValueMessage>();

           var position = 0;
           var bufLen = buf.Length;
           var bufEndPosition = bufLen - 1;
           var strideLength = 1;
           var nextValueChunkStartPosition = 0;
           
           if (State.ScannerState == TagLengthValueProtocolScannerState.ExpectingMoreValueBytes)
           {
               var bytesWeAreStillExpecting = State.CurrentFrame.Length - State.ValueBytesRead;
               strideLength = Math.Min(bufLen, bytesWeAreStillExpecting);
               position = strideLength - 1;
           }
            
           while (true)
           {
               var bytesRemainingInBuffer = bufEndPosition - position;
               switch (State.ScannerState)
               {
                   case TagLengthValueProtocolScannerState.ExpectingTag:
                       State.CurrentFrame.Tag = buf[position];
                       State.ScannerState = TagLengthValueProtocolScannerState.ExpectingLength;
                       strideLength = 1;
                       break;
                   case TagLengthValueProtocolScannerState.ExpectingLength:
                       State.CurrentFrame.Length = buf[position];
                       State.ScannerState = TagLengthValueProtocolScannerState.ExpectingMoreValueBytes;
                       State.ValueBytesRead = 0;
                       strideLength = Math.Min(bytesRemainingInBuffer, State.CurrentFrame.Length);
                       nextValueChunkStartPosition = position + 1;
                       break;
                   case TagLengthValueProtocolScannerState.ExpectingMoreValueBytes:
                       var srcStartPosition = nextValueChunkStartPosition;
                       var destStartPosition = State.ValueBytesRead;
                       Array.Copy(buf, srcStartPosition, State.CurrentFrame.Value, destStartPosition, strideLength);
                       nextValueChunkStartPosition += strideLength;
                       State.ValueBytesRead += strideLength;
                       var bytesRemainingInValue = State.CurrentFrame.Length - State.ValueBytesRead; 
                       if (bytesRemainingInValue == 0)
                       {
                           State.ScannerState = TagLengthValueProtocolScannerState.ExpectingTag;
                           result.Frames.Add(State.CurrentFrame);
                           State.CurrentFrame = new TagLengthValueMessage();
                           strideLength = 1;
                       }
                       else
                       {
                           strideLength = Math.Min(bytesRemainingInBuffer, bytesRemainingInValue);
                       }
                       break;
               }

               if (strideLength == 0)
                   break;
               position += strideLength;
               if (position >= bufLen)
                   break;
           }

           return result;
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
        public static void ReadAndProcessTlvFramesInPieces()
        {
            // Three frames of different sizes scattered across 8 buffers
            
            // Frame 1
            var bufferSlice1 = new byte[]
            {
                0x42, 0x03, 0x1,
            };
                
            var bufferSlice2 = new byte[] {
                                 0x2, 0x3,
            };
           
            // Frame 2
            var bufferSlice3 = new byte[]
            {
                0x42, 0x04,
            };
                
            var bufferSlice4 = new byte[] {
                            0x1, 0x2,
            };
            
            var bufferSlice5 = new byte[] {
                                     0x3, 0x4,
            };
           
            // Frame 3
            var bufferSlice6 = new byte[] {
                0x42, 
            };
            var bufferSlice7 = new byte[] {
                     0x01, 
            };
            var bufferSlice8 = new byte[] {
                          0x01, 
            };

            var parser = new TagLengthValueProtocolParser();
            
            ScanAndDebug(parser, bufferSlice1);
            ScanAndDebug(parser, bufferSlice2);
            ScanAndDebug(parser, bufferSlice3);
            ScanAndDebug(parser, bufferSlice4);
            ScanAndDebug(parser, bufferSlice5);
            ScanAndDebug(parser, bufferSlice6);
            ScanAndDebug(parser, bufferSlice7);
            ScanAndDebug(parser, bufferSlice8);
        }

        public static string ByteArrayToHexString(byte[] byteArray, int len)
        {
            var s = "";
            for (var i = 0; i < len; i++)
            {
                s += String.Format("{0:X}",byteArray[i]);
            }

            return s;
        }
        
        public static void ScanAndDebug(TagLengthValueProtocolParser parser, byte[] bufSlice)
        {
            var scanResult = parser.ReadFramesFromBuffer(bufSlice);
            foreach (var frame in scanResult.Frames)
            {
                Console.WriteLine("Got TLV Frame: {0}", ByteArrayToHexString(frame.Value, frame.Length));
            }
        }
        
        public static void ReadAndProcessTlvFramesSmashedTogether()
        {
            var bufferSlice1 = new byte[]
            {
                0x42, 0x03, 0x1, 0x2, 0x3,
                0x42, 0x04, 0x1, 0x2, 0x3, 0x4,
                0x42, 0x05, 0x1, 0x2, 0x3, 0x4, 0x5
            };

            var parser = new TagLengthValueProtocolParser();
            ScanAndDebug(parser, bufferSlice1);
        }
        
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
            ReadAndProcessSerialFrames();
            ReadAndProcessTlvFramesSmashedTogether();
            ReadAndProcessTlvFramesInPieces();
        }
    }
}
#nullable disable
