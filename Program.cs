using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;

#nullable enable
namespace dotnet_frame_parser
{
    class Util
    {
        public static string ByteArrayToHexString(byte[] byteArray, int len)
        {
            var s = "";
            for (var i = 0; i < len; i++)
            {
                s += String.Format("0x{0:X2}", byteArray[i]);
                if (i < len - 1)
                {
                    s += " ";
                }
            }

            return s;
        }
    }

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
        public byte[] Tag;
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
        public int TagBytesRead = 0;
        public byte[] TagCandidate = new byte[8];
    }

    class MessagePattern
    {
        public byte[] Tag;
        public byte[] Mask;
    } 
    
    class TagLengthValueProtocolParser : IStreamProtocolParser<TagLengthValueMessage>
    {
        public TagLengthValueProtocolParserState State = new TagLengthValueProtocolParserState();

        public int TagLength = 8;
        public List<MessagePattern> ValidTags = new List<MessagePattern>
        {
            new MessagePattern()
            {
                Tag  = new byte[] {0xFF, 0x00, 0xFF, 0xA5, 0xFF, 0xFF, 0xFF, 0xFF},
                Mask = new byte[] {0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF},
            }
        };

        public static byte LengthModifier = 2;

        public FrameParseResult<TagLengthValueMessage> ReadFramesFromBuffer(byte[] buf)
        {
           var result =  new FrameParseResult<TagLengthValueMessage>();
           result.Frames = new List<TagLengthValueMessage>();

           var position = 0;
           var bufLen = buf.Length;
           var bufEndPosition = bufLen - 1;
           var strideLength = 1;
           var nextValueChunkStartPosition = 0;
           var nextTagChunkStartPosition = 0;
           
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
                       strideLength = 1;
                       var tagSrcStartPosition = nextTagChunkStartPosition;
                       State.TagCandidate[State.TagBytesRead] = buf[tagSrcStartPosition];
                       nextTagChunkStartPosition += 1;
                       State.TagBytesRead += 1;
                       var bytesRemainingInTag = TagLength - State.TagBytesRead;
                       if (bytesRemainingInTag == 0)
                       {
                           var tagIsGood = false;
                           foreach (var validTag in ValidTags)
                           {
                               byte[] maskedCandidate = new byte[8];
                               for (var i = 0; i < 8; i++)
                               {
                                   if (validTag.Mask[i] > 0)
                                   {
                                       maskedCandidate[i] = 0xFF;
                                   }
                                   else
                                   {
                                       maskedCandidate[i] = State.TagCandidate[i];
                                   }
                               }
                               if (validTag.Tag.SequenceEqual(maskedCandidate))
                               {
                                   tagIsGood = true;
                                   break;
                               }
                           }

                           if (tagIsGood)
                           {
                               State.ScannerState = TagLengthValueProtocolScannerState.ExpectingLength;
                               State.CurrentFrame.Tag = State.TagCandidate;
                           }
                           else
                           {
                               State.TagCandidate = State.TagCandidate.TakeLast(TagLength - 1).ToArray();
                               Array.Resize(ref State.TagCandidate, TagLength);
                               State.TagBytesRead = TagLength - 1;
                           }
                       }
                       break;
                   case TagLengthValueProtocolScannerState.ExpectingLength:
                       // TODO: validate length (using protocol properties MinFrameLength/MaxFrameLength)
                       State.CurrentFrame.Length = (byte) (buf[position] + TagLengthValueProtocolParser.LengthModifier);
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
                           State.ValueBytesRead = 0;
                           State.TagCandidate = new byte[TagLength];
                           State.TagBytesRead = 0;
                           strideLength = 1;
                           nextTagChunkStartPosition = position + 1;
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
    
    class Program
    {
        public static void ScanAndDebugResult(TagLengthValueProtocolParser parser, byte[] bufSlice)
        {
            var scanResult = parser.ReadFramesFromBuffer(bufSlice);
            foreach (var frame in scanResult.Frames)
            {
                Console.WriteLine("Got TLV Frame, Tag = {0}, Length = {1:x2}, Value = {2}",
                    Util.ByteArrayToHexString(frame.Tag, 8),
                    frame.Length,
                    Util.ByteArrayToHexString(frame.Value, frame.Length));
            }
        }
        
        public static void ReadAndProcessTlvMultibyteTag(byte[] bufferSlice1)
        {
            var parser = new TagLengthValueProtocolParser();
            Console.WriteLine("All at once");
            ScanAndDebugResult(parser, bufferSlice1);

            Console.WriteLine("1 byte at a time");
            ReadBufferInSteps(parser, bufferSlice1, 1);
            
            for (var i = 2; i <= 32; i++)
            {
                Console.WriteLine("{0} bytes at a time", i);
                ReadBufferInSteps(parser, bufferSlice1, i);
            }
        }

        public static void ReadBufferInSteps(TagLengthValueProtocolParser parser, byte[] bufSlice, int stepSize)
        {
            var bytesRemaining = bufSlice.Length;
            var nextChunkStartPosition = 0;
            while (bytesRemaining > 0)
            {
                var chunk = new byte[stepSize];
                var chunkSize = stepSize;
                if (chunkSize > bytesRemaining)
                {
                    chunkSize = bytesRemaining;
                    Array.Resize(ref chunk, chunkSize);
                }
                Array.Copy(bufSlice,nextChunkStartPosition,chunk,0,chunkSize);
                bytesRemaining -= chunkSize;
                nextChunkStartPosition += chunkSize;
                
                ScanAndDebugResult(parser, chunk);
            }
        }
       
        public static byte[] StringToByteArray(string hexWithSpaces)
        {
            var hex = hexWithSpaces.Replace(" ", String.Empty);
            
            // Shamelessly stolen and modified from: https://stackoverflow.com/a/321404
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        } 
        
        static void Main(string[] args)
        {
            var capturedHexBytesWithSpaces = File.ReadAllText(args[0]);
            var rawBytes = StringToByteArray(capturedHexBytesWithSpaces);
            ReadAndProcessTlvMultibyteTag(rawBytes);
        }
    }
}
#nullable disable
