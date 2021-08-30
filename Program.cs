using System;
using System.Collections.Generic;
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
        public byte[] Tag = new byte[] {0xDA, 0xBB, 0xAD, 0x00};
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
        public byte[] TagCandidate = new byte[4];
    }

    class TagLengthValueProtocolParser : IStreamProtocolParser<TagLengthValueMessage>
    {
        public TagLengthValueProtocolParserState State = new TagLengthValueProtocolParserState();

        public int TagLength = 4;
        public List<byte[]> ValidTags = new List<byte[]>
        {
            new byte[] {0xDA, 0xBB, 0xAD, 0x00}
        };

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
           else if (State.ScannerState == TagLengthValueProtocolScannerState.ExpectingTag)
           {
               var tagBytesWeAreStillExpecting = TagLength - State.TagBytesRead;
               strideLength = Math.Min(bufLen, tagBytesWeAreStillExpecting);
               position = strideLength - 1;
           }
             
           while (true)
           {
               var bytesRemainingInBuffer = bufEndPosition - position;
               switch (State.ScannerState)
               {
                   case TagLengthValueProtocolScannerState.ExpectingTag:
                       var tagSrcStartPosition = nextTagChunkStartPosition;
                       var tagDestStartPosition = State.TagBytesRead;
                       Array.Copy(buf, tagSrcStartPosition, State.TagCandidate, tagDestStartPosition, strideLength);
                       nextTagChunkStartPosition += strideLength;
                       State.TagBytesRead += strideLength;
                       var bytesRemainingInTag = TagLength - State.TagBytesRead; 
                       if (bytesRemainingInTag == 0)
                       {
                           var tagIsGood = false;
                           foreach (var validTag in ValidTags)
                           {
                               if (validTag.SequenceEqual(State.TagCandidate))
                               {
                                   tagIsGood = true;
                                   break;
                               }
                           }

                           if (tagIsGood)
                           {
                               State.ScannerState = TagLengthValueProtocolScannerState.ExpectingLength;
                           }
                           else
                           {
                               State.TagCandidate = new byte[TagLength];
                               State.TagBytesRead = 0;
                               nextTagChunkStartPosition = position + 1;
                           }
                           strideLength = 1;
                       }
                       else
                       {
                           strideLength = Math.Min(bytesRemainingInBuffer, bytesRemainingInTag);
                       }
                       break;
                   case TagLengthValueProtocolScannerState.ExpectingLength:
                       // TODO: validate length (using protocol properties MinFrameLength/MaxFrameLength)
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
                           State.ValueBytesRead = 0;
                           State.TagCandidate = new byte[TagLength];
                           State.TagBytesRead = 0;
                           strideLength = Math.Min(bytesRemainingInBuffer, TagLength);
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
                Console.WriteLine("Got TLV Frame, Tag = {0}, Value = {1}",
                    Util.ByteArrayToHexString(frame.Tag, 4),
                    Util.ByteArrayToHexString(frame.Value, frame.Length));
            }
        }
        
        public static void ReadAndProcessTlvMultibyteTag()
        {
            // Three frames of different sizes all with same four-byte tag
            var bufferSlice1 = new byte[]
            {
                0xDA, 0xBB, 0xAD, 0x00, 0x03, 0x1, 0x2, 0x3,
                0xDA, 0xBB, 0xAD, 0x00, 0x04, 0x1, 0x2, 0x3, 0x4,
                0xDA, 0xBB, 0xAD, 0x00, 0x05, 0x1, 0x2, 0x3, 0x4, 0x5
            };

            var parser = new TagLengthValueProtocolParser();
            Console.WriteLine("All at once");
            ScanAndDebugResult(parser, bufferSlice1);

            Console.WriteLine("1 byte at a time");
            ReadBufferInSteps(parser, bufferSlice1, 1);
            
            for (var i = 2; i <= bufferSlice1.Length; i++)
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

        static void Main(string[] args)
        {
            ReadAndProcessTlvMultibyteTag();
        }
    }
}
#nullable disable
