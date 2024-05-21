using FishNet.Serializing;
using System;
using FishNet.Managing;
using System.Diagnostics;
using FishNet.Documenting;

namespace FishNet.Serializing {

    /// <summary>
    /// Special reader/writer buffer struct that can be used in Fishnet RPCs or Broadcasts, as arguments or part of structs
    ///
    /// Use cases:
    ///     - replacement for stream sort of
    ///     - instead of always allocating some arrays T[] and sending that over RPCs/Broadcast, you can use SubStream
    ///     - you can pass SubStream into objects via reference 'ref', and those objects write/read state, useful for dynamic length reconcile (items, inventory, buffs, etc...)
    ///     - sending data inside OnServerSpawn to clients via TargetRPC
    ///     - instead of writting custom serializers for big struct, you can use SubStream inside RPCs/Broadcasts
    /// 
    /// Pros:
    ///     - reading is zero copy, reads directly from FishNet buffers
    ///     - everything is pooled
    ///     - ease of use
    ///     - SubStream can also be left uninitialized (default)
    ///     - Can work safely with multiple receivers in Broadcasts, as long as you read data in the same order
    /// Cons:
    ///     - no reading over length protection, you have to know how much data you are reading, due to buffer being red can be larger than substreams buffer
    ///     - writing buffers are also pooled, but there is a copy (since you write into it, then what is written is copied into fishnet internal buffer, but it's byte copy (fast)
    ///     - have to use Dispose() to return buffers to pool, or it may result in memory leak
    ///     - reading in multiple receiver methods (for same client) in Broadcasts, you have extra deserialization processing per each method
    ///     - might be unsafe to use this to send from clients (undefined data length), but so is sending T[] or List<T> from clients
    /// 
    /// </summary>

    
    public struct SubStream : IDisposable
    {
        public bool Initialized { get; private set; }
        public int Length { get => _writer != null ? _writer.Length : _reader != null ? _reader.Length : 1; }
        public int Remaining { get => _reader != null ? _reader.Remaining : -1; }
        public NetworkManager Manager { get => _writer != null ? _writer.NetworkManager : _reader != null ? _reader.NetworkManager: null; }

        [NonSerialized]
        private PooledReader _reader;
        
        [NonSerialized]
        private int _startPosition;
        
        [NonSerialized] 
        private PooledWriter _writer;

        [NonSerialized]
        private bool _disposed;
        
        /// <summary>
        /// Creates SubStream for writing, use this before sending into RPC or Broadcast
        /// </summary>
        /// <param name="manager">Need to include network manager for handling of networked IDs</param>
        /// <param name="minimumLength">Minimum expected length of data, that will be written</param>
        /// <returns>Returns writer of SubStream</returns>
        public static SubStream StartWriting(NetworkManager manager, out PooledWriter writer, int minimumLength = 0)
        {
            if (minimumLength == 0)
            {
                writer = WriterPool.Retrieve(manager);
            }
            else
            {
                writer = WriterPool.Retrieve(manager, minimumLength);
            }

            var stream = new SubStream()
            {
                _writer = writer,
                Initialized = true,
            };

            return stream;
        }

        /// <summary>
        /// Starts reading from substream via Reader class. Do not forget do Dispose() after reading
        /// </summary>
        /// <param name="reader">Reader to read data from</param>
        /// <returns>Returns true, if SubStream is initialized else false</returns>
        public bool StartReading(out Reader reader)
        {
            if(Initialized)
            {
                // reset reader, in case we are reading in multiple broadcasts delegates/events
                _reader.Position = _startPosition;
                reader = _reader;
                return true;
            }
            reader = null;
            return false;
        }

        public static SubStream CreateFromReader(Reader originalReader, int subStreamLength)
        {
            if(subStreamLength < 0)
                throw new ArgumentException("SubStream length cannot be less than 0");
            
            var originalReaderBuffer = originalReader.GetByteBuffer();

            // inherits reading buffer directly from fishnet reader
            var arraySegment = new ArraySegment<byte>(originalReaderBuffer, originalReader.Position, subStreamLength);

            var newReader = ReaderPool.Retrieve(arraySegment, originalReader.NetworkManager);

            // advance original reader by length of substream data
            originalReader.Skip(subStreamLength);

            return new SubStream()
            {
                _startPosition = newReader.Position,
                _reader = newReader,
                _writer = null,
                _disposed = false,
                Initialized = true,
            };
        }

        /// <summary>
        /// Resets reader to start position, so you can read data again from start of substream.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public void ResetReaderToStartPosition()
        {
            if (_reader != null)
            {
                _reader.Position = _startPosition;
            }
            else
            {
                throw new ArgumentException("SubStream was not initialized as reader!");
            }
        }


        internal PooledWriter GetWriter()
        {
            if (!Initialized)
                throw new ArgumentException("SubStream was not initialized, it has to be initialized properly either localy or remotely!");

            if (_writer == null)
                throw new ArgumentException($"GetWriter() requires SubStream to be initialized as writer! You have to create SubStream with {nameof(StartWriting)}()!");

            return _writer;
        }

        internal PooledReader GetReader()
        {
            if (!Initialized)
                throw new ArgumentException("SubStream was not initialized, it has to be initialized properly either localy or remotely!");

            if (_reader == null)
                throw new ArgumentException($"GetReader() requires SubStream to be initialized as reader!");

            return _reader;
        }

        /// <summary>
        /// Returns uninitialized SubStream. Can send safely over network, but cannot be read from (StartReading will return false)
        /// </summary>
        /// <returns></returns>
        internal static SubStream GetUninitialized()
        {
            return new SubStream()
            {
                Initialized = false,
            };
        }

        /// <summary>
        /// Do not forget to call this after:
        /// - you stopped writing to Substream AND already sent it via RPCs/Broadcasts
        /// - you stoped reading from it inside RPCs/Broadcast receive event
        /// - if you use it in Reconcile method, you have dispose SubStream inside Dispose() of IReconcileData struct
        /// </summary>
        public void Dispose()
        {
            if (!_disposed) // dispose reader only once
            {
                _disposed = true;

                if (_reader != null)
                {
                    _reader.Store();
                    _reader = null;
                }
            }

            if (_writer != null)
            {
                if (_writer.Length < WriterPool.LENGTH_BRACKET) // 1000 is LENGTH_BRACKET
                    _writer.Store();
                else
                    _writer.StoreLength();
                
                _writer = null;
            }
        }

    }

}

